using HQ.Logging.FileLogger.Models;
using HQ.Models.Enums;
using HQ.Models.Interfaces;

namespace HQ.Logging.FileLogger;

public class FileLogger: LoggingBase<LoggingConfig>
{
    public override string Name => "HQ.Logging.FileLogger";
    public override string Description => "Save log messages to file";

    protected override async Task DoWork(LoggingConfig config, LogLevel logLevel, string message, Exception exception = null)
    {
        var logLevelMessage = logLevel switch
        {
            LogLevel.Error => "[ERROR]",
            LogLevel.Warning => "[WARNING]",
            _ => "[INFO]"
        };
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var loggingFile = $"{config.LoggingFolder}/{today}.log";
        
        // Ensure the directory exists
        var directory = Path.GetDirectoryName(loggingFile);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        // Format the log entry
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var logEntry = $"{timestamp} {logLevelMessage} {message}";
        
        if (exception is not null)
        {
            logEntry += $"\n{timestamp} [ERROR] Exception: {exception.Message}\n {timestamp} [ERROR] StackTrace: {exception.StackTrace}";
            
            var inner = exception.InnerException;
            if (exception.InnerException is not null)
            {
                logEntry += $"\n{timestamp} [INFO] Inner Exception(s)";
            }
            while (inner is not null)
            {
                logEntry += $"\n{timestamp} [ERROR] Exception: {inner.Message}\n {timestamp} [ERROR] StackTrace: {inner.StackTrace}";
                inner = inner.InnerException;
            }
            
        }
        
        logEntry += "\n";
        
        // Append to the log file
        await File.AppendAllTextAsync(loggingFile, logEntry);
    }

    
    
}