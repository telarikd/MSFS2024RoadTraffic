using System;
using System.IO;
using System.Text;

namespace RoadTraffic.Infrastructure.Logging
{
    public class SimpleFileLogger : ILogger
    {
        private static readonly object SyncRoot = new object();
        private readonly string _logFilePath;

        public SimpleFileLogger()
        {
            string baseDirectory = Path.GetDirectoryName(typeof(SimpleFileLogger).Assembly.Location);
            string logDirectory = Path.Combine(baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            _logFilePath = Path.Combine(logDirectory, "roadtraffic.log");
        }

        public void Info(string message)
        {
            Write("INFO", message, null);
        }

        public void Warn(string message)
        {
            Write("WARN", message, null);
        }

        public void Error(string message, Exception ex = null)
        {
            Write("ERROR", message, ex);
        }

        private void Write(string level, string message, Exception ex)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var builder = new StringBuilder();
            builder.Append(timestamp)
                .Append(" [")
                .Append(level)
                .Append("] ")
                .Append(message);

            if (ex != null)
            {
                builder.Append(" | ")
                    .Append(ex.GetType().Name)
                    .Append(": ")
                    .Append(ex.Message);
            }

            string line = builder.ToString();

            lock (SyncRoot)
            {
                Console.WriteLine(line);
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }
    }
}
