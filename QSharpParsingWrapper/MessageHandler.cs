using Google.Protobuf;
using QSharpTripleSlash;
using System;
using System.IO;
using System.IO.Pipes;

namespace QSharpParsingWrapper
{
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
        /// Creates a new MessageHandler instance.
        /// </summary>
        /// <param name="Logger">A logger for recording event information</param>
        /// <param name="Stream">The named pipe IPC channel between the VS extension and this wrapper</param>
        public MessageHandler(Logger Logger, NamedPipeClientStream Stream)
        {
            this.Logger = Logger;
            this.Stream = Stream;
            Parser = new QSharpParser(Logger);
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

                // Process the message
                IMessage response = null;
                Logger.Debug($"Got a message of length {messageLength} bytes.");
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
                            response = new ErrorMessage
                            {
                                Message = $"Unknown message type ({message.Type})"
                            };
                            break;
                    }
                }
                catch(Exception ex)
                {
                    Logger.Error($"Error handling message: {ex.GetType().Name} - {ex.Message}.");
                    Logger.Trace(ex.StackTrace);

                    response = new ErrorMessage
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
                        // TODO: Pull this out into its own messaging library, it will get impossible to maintain like this
                        // if more messages get added
                        Message wrapperMessage = new Message
                        {
                            Type = (response is ErrorMessage ? MessageType.Error : MessageType.MethodSignatureResponse),
                            MessageBody = response.ToByteString()
                        };

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


        private IMessage HandleMethodSignatureRequest(MethodSignatureRequest Request)
        {
            return Parser.ParseMethodSignature(Request.MethodSignature);
        }

    }
}
