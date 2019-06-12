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

using Google.Protobuf;
using QSharpTripleSlash.Common;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace QSharpTripleSlash.Extension
{
    /// <summary>
    /// This class is used to handle message traffic between the extension and the Q# parser application.
    /// </summary>
    internal class MessageServer : IDisposable
    {
        /// <summary>
        /// The static singleton instance for this class
        /// </summary>
        private static MessageServer Instance;


        /// <summary>
        /// A logger for recording event information
        /// </summary>
        private readonly Logger Logger;


        /// <summary>
        /// A manager for creating messages to send to the parser application
        /// </summary>
        private readonly MessageManager MessageManager;


        /// <summary>
        /// The named pipe that will act as the IPC channel between this and the parser
        /// </summary>
        private NamedPipeServerStream Stream;


        /// <summary>
        /// A handle to the parser application process that was launched by this server
        /// </summary>
        private Process ParserProcess;


        /// <summary>
        /// Creates the MessageServer singleton, or gets it if it was already initialized.
        /// </summary>
        /// <param name="Logger">A logger for recording event information</param>
        /// <returns>The MessageServer singleton</returns>
        public static MessageServer GetOrCreateServer(Logger Logger)
        {
            if(Instance == null)
            {
                Instance = new MessageServer(Logger);
            }

            return Instance;
        }


        /// <summary>
        /// Creates a new MessageServer instance.
        /// </summary>
        /// <param name="Logger">A logger for recording event information</param>
        private MessageServer(Logger Logger)
        {
            this.Logger = Logger;
            MessageManager = new MessageManager(Logger);
            LaunchParser();
        }


        /// <summary>
        /// Attempts to launch and connect to the parser application.
        /// </summary>
        private void LaunchParser()
        {
            NamedPipeServerStream stream = null;
            try
            {
                // Create the named pipe for IPC
                string pipeGuid = Guid.NewGuid().ToString();
                string pipeName = $"QSTS-{pipeGuid}";
                Logger.Debug($"Creating named pipe [{pipeName}]...");
                try
                {
                    stream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.WriteThrough, ushort.MaxValue, ushort.MaxValue);
                }
                catch(Exception ex)
                {
                    Logger.Error($"Error creating named pipe for the parser application: {ex.GetType().Name} - {ex.Message}");
                    Logger.Trace(ex.StackTrace);
                    return;
                }

                // Make sure the parser DLL is where it's supposed to be
                string parserDllName = "QSharpTripleSlash.Parser.dll";
                string parserFolder = "Parser";
                string currentDirectory = Path.GetDirectoryName(typeof(MessageServer).Assembly.Location);
                string parserDll = Path.Combine(currentDirectory, parserFolder, parserDllName);
                if (!File.Exists(parserDll))
                {
                    Logger.Error($"Error: parser file [{parserDll}] does not exist.");
                    stream?.Dispose();
                    return;
                }

                // Try to get the config setting for the parser's connection timeout
                int parserTimeout = 3000;
                try
                {
                    ConfigManager config = new ConfigManager(currentDirectory);
                    if(!config.GetConfigSetting("Parser", "parser-timeout", out string parserTimeoutString))
                    {
                        Logger.Warn($"Couldn't get the parser-timeout setting from the config file, defaulting to {parserTimeout}.");
                    }
                    else if(!int.TryParse(parserTimeoutString, out parserTimeout))
                    {
                        parserTimeout = 3000;
                        Logger.Warn($"The config file had parser-timeout set to {parserTimeoutString} which isn't a valid integer. " +
                            $"Defaulting to {parserTimeout}.");
                    }
                }
                catch(Exception ex)
                {
                    Logger.Warn($"Couldn't get the parser-timeout setting from the config file: {ex.GetType().Name} - {ex.Message}");
                    Logger.Trace(ex.StackTrace);
                    Logger.Warn($"Defaulting to {parserTimeout}");
                }

                // Try to launch the parser
                Logger.Debug("Pipe created, launching the parser...");
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"{parserDllName} {pipeName}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.Combine(currentDirectory, parserFolder)
                };
                Process parserProcess = new Process
                {
                    StartInfo = startInfo
                };
                try
                {
                    if (!parserProcess.Start())
                    {
                        Logger.Error("Error starting parser application: process failed to start.");
                        stream?.Dispose();
                        return;
                    }
                }
                catch(Exception ex)
                {
                    Logger.Error($"Error starting parser application: {ex.GetType().Name} - {ex.Message}");
                    Logger.Trace(ex.StackTrace);
                    return;
                }

                // Wait for the parser to connect to the named pipe
                Logger.Debug($"Parser started, waiting for it to connect with a timeout of {parserTimeout} ms...");
                try
                {
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
                    if (!stream.WaitForConnectionAsync().Wait(parserTimeout))
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
                    {
                        Logger.Error($"Error connecting to parser application: connection timed out.");
                        stream?.Dispose();
                        return;
                    }
                }
                catch(Exception ex)
                {
                    Logger.Error($"Error connecting to parser application: {ex.GetType().Name} - {ex.Message}");
                    Logger.Trace(ex.StackTrace);
                    stream?.Dispose();
                    return;
                }

                // Success!
                Logger.Debug("Parser connected.");
                Stream = stream;
                ParserProcess = parserProcess;
                ParserProcess.EnableRaisingEvents = true;
                ParserProcess.Exited += ParserProcess_Exited;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error launching parser application: {ex.GetType().Name} - {ex.Message}");
                Logger.Trace(ex.StackTrace);
                stream?.Dispose();
                return;
            }
        }


        /// <summary>
        /// This is triggered if the parser application dies unexpectedly. It attempts to relaunch it
        /// and reconnect to it.
        /// </summary>
        /// <param name="sender">Not used</param>
        /// <param name="e">Not used</param>
        private void ParserProcess_Exited(object sender, EventArgs e)
        {
            try
            {
                Stream?.Dispose();
                Stream = null;
                ParserProcess.Exited -= ParserProcess_Exited;
                ParserProcess?.Dispose();
                ParserProcess = null;

                // Try to restart the process if we aren't shutting down
                if (!DisposedValue)
                {
                    Logger.Warn("The parser application died unexpectedly, restarting it...");
                    LaunchParser();
                    if(Stream?.IsConnected == true)
                    {
                        Logger.Info("Successfully restarted and reconnected.");
                    }
                    else
                    {
                        Logger.Error("Restarting the parser failed, automatic comment block generation" +
                            "will be unavailable.");
                    }
                }
                else
                {
                    Logger.Debug($"{nameof(MessageServer)} is shutting down, parser process closed successfully.");
                }
            }
            catch(Exception ex)
            {
                Logger.Error($"Error restarting parser application: {ex.GetType().Name} - {ex.Message}");
                Logger.Trace(ex.StackTrace);
            }
        }


        /// <summary>
        /// Sends a new parsing request to the parser application, and returns its response.
        /// </summary>
        /// <param name="MethodSignature">The signature string of the method (the operation or
        /// function) to parse.</param>
        /// <returns>The parsed message signature response from the parser, or null if something
        /// went wrong.</returns>
        public MethodSignatureResponse RequestMethodSignatureParse(string MethodSignature)
        {
            if(Stream == null || !Stream.IsConnected)
            {
                Logger.Debug($"Tried to send a {nameof(MethodSignatureRequest)} to the parser but " +
                    $"it isn't connected.");
                return null;
            }

            try
            {
                // Create and wrap the request message
                MethodSignatureRequest request = new MethodSignatureRequest
                {
                    MethodSignature = MethodSignature
                };
                Message message = MessageManager.WrapMessage(request);

                // Send the message to the parser
                byte[] requestBuffer = message.ToByteArray();
                byte[] bufferSize = BitConverter.GetBytes(requestBuffer.Length);
                Stream.Write(bufferSize, 0, bufferSize.Length);
                Stream.Flush();
                Stream.Write(requestBuffer, 0, requestBuffer.Length);
                Stream.Flush();

                // Wait for a response, and read how big it is
                byte[] lengthBuffer = new byte[sizeof(int)];
                if(!ReadFromPipe(lengthBuffer, lengthBuffer.Length))
                {
                    Logger.Warn("Method parsing request failed, something went wrong while reading from the pipe.");
                    return null;
                }

                // Get the actual response from the parser
                int responseLength = BitConverter.ToInt32(lengthBuffer, 0);
                byte[] responseBuffer = new byte[responseLength];
                if(!ReadFromPipe(responseBuffer, responseLength))
                {
                    Logger.Warn("Method parsing request failed, something went wrong while reading from the pipe.");
                    return null;
                }

                // Deserialize the response
                Message response = Message.Parser.ParseFrom(responseBuffer);
                switch (response.Type)
                {
                    case MessageType.Error:
                        Error error = Error.Parser.ParseFrom(response.MessageBody);
                        Logger.Warn($"Parser failed during method signature processing: {error.ErrorType} - {error.Message}");
                        Logger.Trace(error.StackTrace);
                        return null;

                    case MessageType.MethodSignatureResponse:
                        MethodSignatureResponse signatureResponse = MethodSignatureResponse.Parser.ParseFrom(response.MessageBody);
                        return signatureResponse;

                    default:
                        throw new Exception($"Unexpected message response type: {response.Type}");
                }
            }
            catch(Exception ex)
            {
                Logger.Error($"Error during method signature processing: {ex.GetType().Name} - {ex.Message}");
                Logger.Trace(ex.StackTrace);
                return null;
            }
        }


        /// <summary>
        /// Safely reads from the IPC named pipe channel.
        /// </summary>
        /// <param name="Buffer">The buffer to store the received bytes into</param>
        /// <param name="BytesToRead">The number of bytes to read (the expected message length)</param>
        /// <returns>True if the read was successful, false if it failed for some reason.</returns>
        private bool ReadFromPipe(byte[] Buffer, int BytesToRead)
        {
            try
            {
                int bytesRead = Stream.Read(Buffer, 0, BytesToRead);
                if (bytesRead == 0)
                {
                    Logger.Debug("Got 0 bytes while reading from the pipe.");
                    return false;
                }
                return true;
            }
            catch (IOException ex)
            {
                Logger.Error($"Error when reading from the parser: {ex.GetType().Name} - {ex.Message}");
                Logger.Trace($"{ex.StackTrace}");
                return false;
            }
        }


        #region IDisposable Support
        private bool DisposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool Disposing)
        {
            if (!DisposedValue)
            {
                if (Disposing)
                {
                    DisposedValue = true;

                    ParserProcess.Exited -= ParserProcess_Exited;
                    ParserProcess?.Dispose();
                    ParserProcess = null;

                    Stream?.Dispose();
                    Stream = null;
                }
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion

    }
}
