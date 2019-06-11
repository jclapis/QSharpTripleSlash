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
    internal class MessageServer
    {
        private readonly NamedPipeServerStream Stream;


        private readonly Process ParserProcess;


        public static MessageServer Instance { get; }


        static MessageServer()
        {
            Instance = new MessageServer();
        }


        private MessageServer()
        {
            string pipeGuid = Guid.NewGuid().ToString();
            string pipeName = $"QSTS-{pipeGuid}";
            string currentDirectory = Path.GetDirectoryName(typeof(MessageServer).Assembly.Location);

            Stream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough, ushort.MaxValue, ushort.MaxValue);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"QSharpTripleSlash.Parser.dll {pipeName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.Combine(currentDirectory, "Parser")
            };
            try
            {
                ParserProcess = Process.Start(startInfo);
                if(ParserProcess.HasExited)
                {
                    return;
                }
                Stream.WaitForConnection();
            }
            catch(Exception ex)
            {

            }

        }


        public MethodSignatureResponse RequestMethodSignatureParse(string MethodSignature)
        {
            if(!Stream.IsConnected)
            {
                return null;
            }

            MethodSignatureRequest request = new MethodSignatureRequest
            {
                MethodSignature = MethodSignature
            };
            Message message = new Message
            {
                Type = MessageType.MethodSignatureRequest,
                MessageBody = request.ToByteString()
            };

            byte[] requestBuffer = message.ToByteArray();
            byte[] bufferSize = BitConverter.GetBytes(requestBuffer.Length);
            Stream.Write(bufferSize, 0, bufferSize.Length);
            Stream.Flush();
            Stream.Write(requestBuffer, 0, requestBuffer.Length);
            Stream.Flush();

            byte[] lengthBuffer = new byte[sizeof(int)];
            Stream.Read(lengthBuffer, 0, lengthBuffer.Length);

            int responseLength = BitConverter.ToInt32(lengthBuffer, 0);
            byte[] responseBuffer = new byte[responseLength];
            Stream.Read(responseBuffer, 0, responseLength);

            Message response = Message.Parser.ParseFrom(responseBuffer);
            switch(response.Type)
            {
                case MessageType.Error:
                    Error error = Error.Parser.ParseFrom(response.MessageBody);
                    throw new Exception($"Parser failed during method signature processing: {error.ErrorType} - {error.Message}");

                case MessageType.MethodSignatureResponse:
                    MethodSignatureResponse signatureResponse = MethodSignatureResponse.Parser.ParseFrom(response.MessageBody);
                    return signatureResponse;

                default:
                    throw new Exception($"Unexpected message response type: {response.Type}");
            }
        }


    }
}
