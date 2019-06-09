/* ========================================================================
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

using Google.Protobuf;
using System;
using System.Collections.Generic;

namespace QSharpTripleSlash.Common
{
    /// <summary>
    /// This class is used to manage messages that are sent between the QSharpTripleSlash
    /// extension and the parsing wrapper.
    /// </summary>
    public class MessageManager
    {
        /// <summary>
        /// A map of message types to their values in the MessageType enum
        /// </summary>
        private static readonly Dictionary<Type, MessageType> MessageTypeLookup;


        /// <summary>
        /// A logger for recording event information
        /// </summary>
        private readonly Logger Logger;


        /// <summary>
        /// Initializes the message type map
        /// </summary>
        static MessageManager()
        {
            MessageTypeLookup = new Dictionary<Type, MessageType>();
        }


        /// <summary>
        /// Creates a new MessageManager instance.
        /// </summary>
        /// <param name="Logger">A logger for recording event information</param>
        public MessageManager(Logger Logger)
        {
            this.Logger = Logger;

            // Check to see if the map has already been created
            if(MessageTypeLookup.Count > 0)
            {
                return;
            }

            // Create the message type map
            foreach(Type exportedType in typeof(MessageManager).Assembly.GetExportedTypes())
            {
                // Look through every public type in this assembly and check if it's a protobuf message 
                if(typeof(IMessage).IsAssignableFrom(exportedType))
                {
                    // If it is, try to match it to the entry in MessageType with the same name
                    if(Enum.TryParse(exportedType.Name, out MessageType messageType))
                    {
                        MessageTypeLookup.Add(exportedType, messageType);
                    }
                    else
                    {
                        Logger.Warn($"Warning: tried to add the message type {exportedType.Name} to " +
                            $"message wrapping support, but it isn't included in the {nameof(MessageType)} enum.");
                    }
                }
            }
        }


        /// <summary>
        /// Wraps a protobuf message in the general Message class, so it can be properly identified on
        /// the remote end.
        /// </summary>
        /// <param name="Message">The message to wrap</param>
        /// <returns>A <see cref="Message"/> object that wraps the provided message and specifies what
        /// type it is</returns>
        public static Message WrapMessage(IMessage Message)
        {
            if(!MessageTypeLookup.TryGetValue(Message.GetType(), out MessageType messageType))
            {
                throw new Exception($"Can't wrap {Message.GetType()} because it hasn't been mapped to the {nameof(MessageType)} enum.");
            }

            Message wrapper = new Message
            {
                Type = messageType,
                MessageBody = Message.ToByteString()
            };

            return wrapper;
        }

    }
}
