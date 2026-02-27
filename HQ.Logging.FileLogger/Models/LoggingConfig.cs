using HQ.Models.Enums;
using HQ.Models.Interfaces;

namespace HQ.Logging.FileLogger.Models;

public class LoggingConfig: ILoggingConfig
{
    public string Name { get; set; }
    public string Description { get; set; }
    public LogLevel MinimumLogLevel { get; set; } 
    public string LoggingFolder { get; set; }
}