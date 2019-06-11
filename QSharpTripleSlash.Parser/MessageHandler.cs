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
using System.IO;
using System.IO.Pipes;

namespace QSharpTripleSlash.Parser
{
    /// <summary>
    /// This class handles all of the I/O between the extension and the parsing wrapper,
    /// including message passing and processing.
    /// </summary>
    internal class MessageHandler
    {
        /// <summary>
        /// A logger for recording event information
        /// </summary>
        private readonly Logger Logger;


        /// <summary>
        /// The named pipe IPC channel between the VS extension and this wrapper
        /// </summary>
        private readonly NamedPipeClientStream Stream;


        /// <summary>
        /// The Q# code parser
        /// </summary>
        private readonly QSharpParser Parser;


        /// <summary>
        /// A manager for building messages to send to the remote end
        /// </summary>
        private readonly MessageManager MessageManager;


        /// <summary>
        /// Creates a new MessageHandler instance.
        /// </summary>
        /// <param name="Logger">A logger for recording event information</param>
        /// <param name="Stream">The named pipe IPC channel between the VS extension and this wrapper</param>
        public MessageHandler(Logger Logger, NamedPipeClientStream Stream)
        {
            this.Logger = Logger;
            this.Stream = Stream;
            Parser = new QSharpParser(Logger);
            MessageManager = new MessageManager(Logger);
        }


        /// <summary>
        /// This is the main loop of the wrapper, which listens for messages from the VS extension and 
        /// responds to them.
        /// </summary>
        public void ProcessMessageLoop()
        {
            // This will hold the length of incoming messages on the pipe
            byte[] sizeBuffer = new byte[sizeof(int)];

            // This is going to hold the signature string sent from the extension. 64k would be a ridiculously
            // big signature, so this is a fine default; if it ever got bigger than this, it's more likely that
            // something went wrong on the extension's side.
            byte[] messageBuffer = new byte[ushort.MaxValue];

            while (Stream.IsConnected)
            {
                // Get the size of the incoming message
                if (!ReadFromPipe(sizeBuffer, sizeBuffer.Length))
                {
                    continue;
                }
                int messageLength = BitConverter.ToInt32(sizeBuffer);

                // Get the signature sent by the extension
                if (messageLength > messageBuffer.Length)
                {
                    // This is here just in case there is a legitimate need for a larger message buffer, for future-proofing
                    // the extension
                    messageBuffer = new byte[messageLength];
                }
                if (!ReadFromPipe(messageBuffer, messageLength))
                {
                    continue;
                }
                Logger.Debug($"Got a message of length {messageLength} bytes.");

                // Process the message
                IMessage response;
                try
                {
                    Message message = Message.Parser.ParseFrom(messageBuffer, 0, messageLength);
                    Logger.Debug($"Message type: {message.Type}");

                    switch (message.Type)
                    {
                        case MessageType.MethodSignatureRequest:
                            MethodSignatureRequest request = MethodSignatureRequest.Parser.ParseFrom(message.MessageBody);
                            response = HandleMethodSignatureRequest(request);
                            break;

                        default:
                            Logger.Warn($"Warning: got a message of unknown type ({message.Type}).");
                            response = new Error
                            {
                                Message = $"Unknown message type ({message.Type})"
                            };
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error handling message: {ex.GetType().Name} - {ex.Message}.");
                    Logger.Trace(ex.StackTrace);

                    response = new Error
                    {
                        ErrorType = ex.GetType().Name,
                        Message = ex.Message,
                        StackTrace = ex.StackTrace
                    };
                }

                // Write the response back to the extension.
                try
                {
                    if(response != null)
                    {
                        Message wrapperMessage = MessageManager.WrapMessage(response);
                        byte[] responseBuffer = wrapperMessage.ToByteArray();
                        byte[] responseLength = BitConverter.GetBytes(responseBuffer.Length);
                        Stream.Write(responseLength);
                        Stream.Flush();
                        Stream.Write(responseBuffer);
                        Stream.Flush();
                    }
                }
                catch(Exception ex)
                {
                    Logger.Error($"Error writing response to extension: {ex.GetType().Name} - {ex.Message}.");
                    Logger.Trace(ex.StackTrace);
                    Stream.Close();
                }
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
                Logger.Warn($"Error when reading from the extension pipe: {ex.GetType().Name} - {ex.Message}");
                Logger.Trace($"{ex.StackTrace}");
                return false;
            }
        }


        /// <summary>
        /// Handles a request for parsing a method signature.
        /// </summary>
        /// <param name="Request">The signature parsing request</param>
        /// <returns>A <see cref="MethodSignatureResponse"/> if parsing was successful, or a
        /// <see cref="ErrorMessage"/> if something went wrong.</returns>
        private IMessage HandleMethodSignatureRequest(MethodSignatureRequest Request)
        {
            return Parser.ParseMethodSignature(Request.MethodSignature);
        }

    }
}
