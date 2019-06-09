using Nett;
using NLog;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using System;
using System.IO;

using NLogger = NLog.Logger;

namespace QSharpParsingWrapper
{
    /// <summary>
    /// This class is used for logging events and things that occur on the parsing wrapper side.
    /// </summary>
    internal class Logger
    {
        /// <summary>
        /// The NLog Logger backend that actually implements the logging functionality.
        /// </summary>
        private readonly NLogger LoggerImpl;


        /// <summary>
        /// Initializes NLog with the proper target file location and log level.
        /// </summary>
        static Logger()
        {
            // Set up the target file in the extension's installation directory
            string assemblyPath = typeof(Logger).Assembly.Location;
            string baseDir = Path.GetDirectoryName(assemblyPath);
            string logFile = "${basedir}/../QSharpParsingWrapper.log";
            LoggingConfiguration logConfig = new LoggingConfiguration();
            
            FileTarget logFileTarget = new FileTarget
            {
                Name = "LogFile",
                FileName = logFile,
                Layout = "${longdate} | ${level:uppercase=true} | ${message}"
            };
            logConfig.AddTarget(logFileTarget);

            // Get the log level from the config file
            LogLevel logLevel = LogLevel.Trace;
            string errorMessage = null;
            try
            {
                string configFile = Path.Combine(baseDir, "..", "config.toml");
                if(File.Exists(configFile))
                {
                    TomlTable config = Toml.ReadFile(configFile);
                    TomlTable loggingSection = (TomlTable)config["Logging"];
                    string configuredLogLevel = ((TomlString)loggingSection["log-level"]).Value;
                    logLevel = GetLogLevel(configuredLogLevel);
                }
                else
                {
                    throw new FileNotFoundException("Config file doesn't exist.", configFile);
                }
            }
            catch(Exception ex)
            {
                errorMessage = "Error loading log file config setting, defaulting to INFO. Error details: " +
                    $"{ex.GetType().Name} - {ex.Message}";
            }

            // Set NLog up with the specified log level
            logConfig.AddRule(logLevel, LogLevel.Fatal, logFileTarget);
            LogManager.Configuration = logConfig;

            if (errorMessage != null)
            {
                NLogger logger = LogManager.GetCurrentClassLogger();
                logger.Log(LogLevel.Warn, errorMessage);
            }
        }


        /// <summary>
        /// Converts the log level string from the config file to an NLog level.
        /// </summary>
        /// <param name="ConfiguredLogLevel">The value of the log-level value in the config file</param>
        /// <returns>The NLog level that matches the provided string</returns>
        private static LogLevel GetLogLevel(string ConfiguredLogLevel)
        {
            // Capitalize the first letter
            string firstLetter = ConfiguredLogLevel.Substring(0, 1).ToUpper();
            string nlogName = firstLetter + ConfiguredLogLevel.Substring(1, ConfiguredLogLevel.Length - 1);

            return LogLevel.FromString(nlogName);
        }


        /// <summary>
        /// Creates a new Logger instance.
        /// </summary>
        public Logger()
        {
            LoggerImpl = LogManager.GetCurrentClassLogger();
        }


        /// <summary>
        /// Logs a message at the TRACE level.
        /// </summary>
        /// <param name="Message">The message to log</param>
        public void Trace(string Message)
        {
            LoggerImpl.Trace(Message);
        }


        /// <summary>
        /// Logs a message at the DEBUG level.
        /// </summary>
        /// <param name="Message">The message to log</param>
        public void Debug(string Message)
        {
            LoggerImpl.Debug(Message);
        }


        /// <summary>
        /// Logs a message at the INFO level.
        /// </summary>
        /// <param name="Message">The message to log</param>
        public void Info(string Message)
        {
            LoggerImpl.Info(Message);
        }


        /// <summary>
        /// Logs a message at the WARN level.
        /// </summary>
        /// <param name="Message">The message to log</param>
        public void Warn(string Message)
        {
            LoggerImpl.Warn(Message);
        }


        /// <summary>
        /// Logs a message at the ERROR level.
        /// </summary>
        /// <param name="Message">The message to log</param>
        public void Error(string Message)
        {
            LoggerImpl.Error(Message);
        }


        /// <summary>
        /// Logs a message at the FATAL level.
        /// </summary>
        /// <param name="Message">The message to log</param>
        public void Fatal(string Message)
        {
            LoggerImpl.Fatal(Message);
        }

    }
}
