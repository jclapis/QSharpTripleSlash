using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QSharpTripleSlash
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
                Arguments = $"QSharpParsingWrapper.dll {pipeName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.Combine(currentDirectory, "QSharpParsingWrapper")
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
                    ErrorMessage error = ErrorMessage.Parser.ParseFrom(response.MessageBody);
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
