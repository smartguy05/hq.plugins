using HQ.Models.Attributes;
using HQ.Models.Enums;
using HQ.Models.Interfaces;

namespace HQ.Logging.FileLogger.Models;

public class LoggingConfig: ILoggingConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Minimum severity to log. Messages below this level are discarded.")]
    public LogLevel MinimumLogLevel { get; set; }

    [Tooltip("Absolute path to the directory where log files are written, e.g. /var/log/hq")]
    public string LoggingFolder { get; set; }
}
