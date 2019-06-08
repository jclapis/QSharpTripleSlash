using System;
using System.IO.Pipes;
using System.Text;

namespace QSharpParsingWrapper
{
    class Program
    {
        /// <summary>
        /// The main entry point of the wrapper.
        /// </summary>
        /// <param name="Args">This must contain only one argument, which is the name of the named pipe that
        /// the VS extension created for IPC.</param>
        static void Main(string[] Args)
        {
            Logger logger = new Logger();

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
