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
 * 
 * This project contains content developed by The MITRE Corporation.
 * If this code is used in a deployment or embedded within another project,
 * it is requested that you send an email to opensource@mitre.org in order
 * to let us know where this software is being used.
 * ======================================================================== */

using QSharpTripleSlash.Common;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace QSharpTripleSlash.Parser
{
    /// <summary>
    /// This class holds the entry point for the parsing wrapper application.
    /// </summary>
    class Program
    {
        /// <summary>
        /// The main entry point of the parsing wrapper.
        /// </summary>
        /// <param name="Args">This must contain only one argument, which is the name of the named pipe that
        /// the VS extension created for IPC.</param>
        static void Main(string[] Args)
        {
            // Create a logger
            string assemblyPath = typeof(Program).Assembly.Location;
            string basePath = Path.Combine(Path.GetDirectoryName(assemblyPath), "..");
            Logger logger = new Logger(basePath, "Parser.log");

            // Make sure there's exactly 1 argument
            if(Args.Length != 1)
            {
                StringBuilder builder = new StringBuilder();
                builder.Append($"Expected 1 argument, but got {Args.Length}:");
                foreach(string arg in Args)
                {
                    builder.Append($"\t{arg}");
                }
                logger.Fatal(builder.ToString());
                Environment.Exit(-1);
            }

            // Connect to the extension's named pipe
            string pipeName = Args[0];
            logger.Trace($"Extension pipe = {pipeName}");
            using (NamedPipeClientStream stream = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.WriteThrough | PipeOptions.Asynchronous))
            {
                try
                {
                    stream.Connect(3000);
                    logger.Debug("Connected to extension pipe.");
                }
                catch (Exception ex)
                {
                    logger.Fatal($"Error connecting to extension pipe: {ex.GetType().Name} - {ex.Message}");
                    logger.Trace($"Stack trace: {ex.StackTrace}");
                    Environment.Exit(-2);
                }

                // Run the IPC loop (this program is simple enough to be single-threaded without any issues)
                try
                {
                    MessageHandler handler = new MessageHandler(logger, stream);
                    handler.ProcessMessageLoop();
                }
                catch (Exception ex)
                {
                    logger.Fatal($"Error occurred during message processing: {ex.GetType().Name} - {ex.Message}");
                    logger.Trace($"Stack trace: {ex.StackTrace}");
                    Environment.Exit(-3);
                }

                logger.Info("Shutting down.");
            }
        }

    }
}
