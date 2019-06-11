/* ========================================================================
 * Copyright (C) 2019 The MITRE Corporation.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * ======================================================================== */

using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.IO;

using NLogger = NLog.Logger;

namespace QSharpTripleSlash.Common
{
    /// <summary>
    /// This class is used for logging events and diagnostics.
    /// </summary>
    public class Logger
    {
        /// <summary>
        /// The NLog Logger backend that actually implements the logging functionality.
        /// </summary>
        private readonly NLogger LoggerImpl;


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
        /// <param name="BaseDirectory">The base installation directory of QSharpTripleSlash</param>
        /// <param name="LogFileName">The name of the log file to create for this instance</param>
        public Logger(string BaseDirectory, string LogFileName)
        {
            string logFile = Path.Combine(BaseDirectory, LogFileName);
            LoggingConfiguration logConfig = new LoggingConfiguration();

            FileTarget logFileTarget = new FileTarget
            {
                Name = "LogFile",
                FileName = logFile,
                Layout = "${longdate} | ${level:uppercase=true} | ${message}"
            };
            logConfig.AddTarget(logFileTarget);

            // Get the log level from the config file
            LogLevel logLevel = LogLevel.Info;
            string errorMessage = null;
            try
            {
                ConfigManager config = new ConfigManager(BaseDirectory);
                if (config.GetConfigSetting("Logging", "log-level", out string logLevelString))
                {
                    logLevel = GetLogLevel(logLevelString);
                }
                else
                {
                    errorMessage = "Config file didn't contain a log-level setting in the Logging section, defaulting to INFO.";
                }
            }
            catch (Exception ex)
            {
                errorMessage = "Error loading log file config setting, defaulting to INFO. Error details: " +
                    $"{ex.GetType().Name} - {ex.Message}";
            }

            // Set NLog up with the specified log level
            logConfig.AddRule(logLevel, LogLevel.Fatal, logFileTarget);
            LogManager.Configuration = logConfig;
            LoggerImpl = LogManager.GetCurrentClassLogger();

            if (errorMessage != null)
            {
                Warn(errorMessage);
            }
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
