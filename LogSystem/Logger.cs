using System;

namespace LogSystem
{
    public class Logger : ILogger
    {
        public Action<string> OnLog;

        private LogLevel _logLevel;


        public void Log(LogLevel logLevel, string msg)
        {
            if (_logLevel == LogLevel.None) return;

            if (logLevel >= _logLevel)
                OnLog?.Invoke($"{logLevel}-{DateTime.Now.Hour}:{DateTime.Now.Minute}:{DateTime.Now.Second}-{msg}");
        }

        public void SetLogLevel(LogLevel level) => _logLevel = level;

        public void RegisterWriter(Action<string> action) => OnLog = action;

        #region shortcuts
        public void LogInfo(string msg) => Log(LogLevel.Information, msg);

        public void LogWarning(string msg) => Log(LogLevel.Warning, msg);

        public void LogError(string msg) => Log(LogLevel.Error, msg);
        #endregion
    }
}
