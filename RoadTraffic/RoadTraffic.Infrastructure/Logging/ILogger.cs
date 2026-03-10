using System;

namespace RoadTraffic.Infrastructure.Logging
{
    public interface ILogger
    {
        void Info(string message);

        void Warn(string message);

        void Error(string message, Exception ex = null);
    }
}
