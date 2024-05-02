using System;

namespace LogSystem
{
    public interface ILogger
    {
        void Log(LogLevel logLevel, string msg);
        void LogInfo(string msg);
        void LogWarning(string msg);
        void LogError(string msg);
        void SetLogLevel(LogLevel level);

        void RegisterWriter(Action<string> action);
    }

    public enum LogLevel
    {
        None = 0,
        Information = 1,
        Warning = 2,
        Error = 3
    }
}