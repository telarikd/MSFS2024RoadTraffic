using System;
using System.Diagnostics;
using System.IO;

namespace RoadTraffic
{
    internal static class RuntimeDiagnostics
    {
        private static readonly object Sync = new object();

        public static void Log(string message)
        {
            string line = DateTime.UtcNow.ToString("O") + " " + message;
            Debug.WriteLine(line);

            try
            {
                lock (Sync)
                {
                    File.AppendAllText(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RoadTraffic.runtime.log"),
                        line + Environment.NewLine);
                }
            }
            catch
            {
            }
        }
    }
}
