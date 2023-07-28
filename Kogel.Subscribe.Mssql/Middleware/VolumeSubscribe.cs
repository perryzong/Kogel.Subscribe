﻿using Kogel.Subscribe.Mssql.Entites;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Linq;
using System.IO;

namespace Kogel.Subscribe.Mssql.Middleware
{
    /// <summary>
    /// 持久化订阅（记录每次执行的Seqval到本地）
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class VolumeSubscribe<T> : ISubscribe<T>
        where T : class
    {
        /// <summary>
        /// 
        /// </summary>
        private readonly SubscribeContext<T> _context;

        public VolumeSubscribe(SubscribeContext<T> context)
        {
            this._context = context;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="messageList"></param>
        public void Subscribes(List<SubscribeMessage<T>> messageList)
        {
            if (!(messageList is null) && messageList.Any())
            {
                string seqval = messageList.LastOrDefault().Seqval;
                if (!string.IsNullOrEmpty(seqval))
                    this._context.VolumeFile.WriteSeqval(seqval);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 
    /// </summary>
    public class VolumeFile<T> : IDisposable
    {
        private string _path;

        private static readonly object obj = new object();

        private readonly SubscribeContext<T> _context;

        public VolumeFile(SubscribeContext<T> context)
        {
            _context = context;
            if (string.IsNullOrEmpty(_path))
            {
                _path = $"{Directory.GetCurrentDirectory()}//volumes//{DateTime.Now.ToString("yyyyMMdd")}_{context.TableName}.txt";
                string directoryFullName = Path.GetDirectoryName(_path);
                if (!Directory.Exists(directoryFullName))
                {
                    Directory.CreateDirectory(directoryFullName);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="isWriter"></param>
        /// <returns></returns>
        private FileStream GetFileStream(string path, bool isWriter = false)
        {
            return isWriter ? new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Write)
                : File.OpenRead(path);
        }

        /// <summary>
        /// 读取seq
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="context"></param>
        /// <returns></returns>
        public string ReadSeqval()
        {
            FileStream fileStream = default;
            if (File.Exists(_path))
                fileStream = GetFileStream(_path);
            else
            {
                //不存在就读取最近的日志文件
                string directoryFullName = Path.GetDirectoryName(_path);
                var fileInfoList = Directory.GetFiles(directoryFullName, $"*_{_context.TableName}.txt")?
                    .Select(x => new FileInfo(x))
                    .ToList();
                if (fileInfoList != null && fileInfoList.Any())
                {
                    var lastWriteFile = fileInfoList.OrderByDescending(x => x.LastWriteTime).FirstOrDefault();
                    fileStream = GetFileStream(lastWriteFile.FullName);
                }
            }
            if (fileStream is null)
                return null;
            else
            {
                string seqval = GetLastLine(fileStream);
                fileStream.Dispose();
                return seqval;
            }
        }

        private StreamWriter _streamWriter;
        /// <summary>
        /// 写入seq
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="context"></param>
        public void WriteSeqval(string seqval)
        {
            string _newpath = $"{Directory.GetCurrentDirectory()}//volumes//{DateTime.Now.ToString("yyyyMMdd")}_{_context.TableName}.txt";
            lock (obj)
            {
                if (_newpath != _path)
                {
                    _path = _newpath;
                    _streamWriter.Close();
                    _streamWriter = null;
                }
                if (_streamWriter is null)
                {
                    _streamWriter = File.Exists(_path) ? new StreamWriter(GetFileStream(_path, true)) : File.CreateText(_path);
                    _streamWriter.AutoFlush = true;
                }
                _streamWriter.WriteLine(seqval);
            }
        }

        /// <summary>
        /// 提取文本最后一行数据
        /// </summary>
        /// <param name="fileStream">文件流</param>
        /// <returns>最后一行数据</returns>
        private static string GetLastLine(FileStream fileStream)
        {
            //24为每个seqval的长度
            int seekLength = (int)(fileStream.Length < 24 ? fileStream.Length : 24);
            byte[] buffer = new byte[seekLength];
            fileStream.Seek(-buffer.Length, SeekOrigin.End);
            fileStream.Read(buffer, 0, buffer.Length);
            string multLine = Encoding.ASCII.GetString(buffer);
            string[] lines = multLine.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (!lines.Any())
                return null;
            string line = lines[lines.Length - 1];
            return line;
        }

        /// <summary>
        /// 
        /// </summary>
        public void Dispose()
        {
            _streamWriter?.Dispose();
        }
    }
}
