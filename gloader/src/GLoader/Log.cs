using System;
using System.IO;
using System.Text;

namespace GLoader
{
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static StreamWriter _writer;

        public static void Initialize(string logsDirectory, string role)
        {
            Directory.CreateDirectory(logsDirectory);

            var safeRole = string.IsNullOrWhiteSpace(role) ? "process" : role.Trim().ToLowerInvariant();
            var path = Path.Combine(logsDirectory, "gloader-" + safeRole + ".log");
            _writer = new StreamWriter(path, append: false, encoding: new UTF8Encoding(false))
            {
                AutoFlush = true
            };
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);

        public static void Dispose()
        {
            lock (Gate)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        private static void Write(string level, string message)
        {
            var line = string.Format(
                "[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] {2}",
                DateTime.Now,
                level,
                message);

            lock (Gate)
            {
                Console.WriteLine(line);
                _writer?.WriteLine(line);
            }
        }
    }
}
