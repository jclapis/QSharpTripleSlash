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
using Microsoft.Quantum.QsCompiler.SyntaxTokens;
using Microsoft.Quantum.QsCompiler.TextProcessing;
using QSharpTripleSlash.Common;
using System;
using System.Collections.Generic;

namespace QSharpTripleSlash.Parser
{
    /// <summary>
    /// This class wraps the Q# Text Processing library, which is used to parse Q# code in the form of
    /// a string. It currently supports being able to pull details from functions and operations, for
    /// documentation purposes.
    /// </summary>
    internal class QSharpParser
    {
        /// <summary>
        /// A logger for recording event information
        /// </summary>
        private readonly Logger Logger;


        /// <summary>
        /// Creates a new QSharpParser instance.
        /// </summary>
        /// <param name="Logger">A logger for recording event information</param>
        public QSharpParser(Logger Logger)
        {
            this.Logger = Logger;
        }


        /// <summary>
        /// Parses a raw Q# method signature and extracts the important details for documentation purposes.
        /// </summary>
        /// <param name="MethodSignature">The signature of the method, in plaintext form</param>
        /// <returns>A <see cref="MethodSignatureResponse"/> with the relevant information extracted from
        /// the raw signature, or an <see cref="ErrorMessage"/> if parsing failed.</returns>
        public IMessage ParseMethodSignature(string MethodSignature)
        {
            try
            {
                string name = null;
                List<string> parameterNames = null;
                bool hasReturnType = false;

                // Parse the details out of the signature text
                QsFragment[] fragments = Parsing.ProcessCodeFragment(MethodSignature);
                if (fragments[0].Kind is QsFragmentKind.OperationDeclaration operationDeclaration)
                {
                    name = GetSymbolName(operationDeclaration.Item1);
                    parameterNames = GetParameterNames(operationDeclaration.Item2);
                    hasReturnType = CheckForReturnType(operationDeclaration.Item2);
                }
                else if(fragments[0].Kind is QsFragmentKind.FunctionDeclaration functionDeclaration)
                {
                    name = GetSymbolName(functionDeclaration.Item1);
                    parameterNames = GetParameterNames(functionDeclaration.Item2);
                    hasReturnType = CheckForReturnType(functionDeclaration.Item2);
                }
                else
                {
                    string warning = $"Warning: tried to parse a method signature, but it wasn't a function or operation. Contents: {MethodSignature}";
                    Logger.Warn(warning);
                    return new ErrorMessage
                    {
                        Message = warning
                    };
                }

                // Log and return them
                Logger.Debug($"Method name: {name ?? string.Empty}");
                Logger.Debug("Parameter names:");
                foreach(string parameter in parameterNames)
                {
                    Logger.Debug($"\t{parameter}");
                }
                Logger.Debug($"Returns something: {hasReturnType}");

                MethodSignatureResponse response = new MethodSignatureResponse
                {
                    Name = name,
                    HasReturnType = hasReturnType
                };
                response.ParameterNames.AddRange(parameterNames);
                return response;
            }
            catch(Exception ex)
            {
                Logger.Error($"Error processing method signature: {ex.GetType().Name} - {ex.Message}.");
                Logger.Trace(ex.StackTrace);

                return new ErrorMessage
                {
                    ErrorType = ex.GetType().Name,
                    Message = ex.Message,
                    StackTrace = ex.StackTrace
                };
            }
        }

        /// <summary>
        /// Gets the name for a Q# symbol.
        /// </summary>
        /// <param name="Symbol">The symbol to get the name of</param>
        /// <returns>The name of the symbol.</returns>
        private string GetSymbolName(QsSymbol Symbol)
        {
            QsSymbolKind<QsSymbol>.Symbol nameSymbol = (QsSymbolKind<QsSymbol>.Symbol)Symbol.Symbol;
            return nameSymbol.Item.Value;
        }


        /// <summary>
        /// Gets a list of names for each of the parameters in a method.
        /// </summary>
        /// <param name="MethodSignature">The signature of the method to process</param>
        /// <returns>A list of all of the method's parameter names.</returns>
        private List<string> GetParameterNames(CallableSignature MethodSignature)
        {
            List<string> parameterNames = new List<string>();

            // The method's parameters will all be encapsulated into one large tuple, so it can be 
            // processed the same way as a nested tuple parameter.
            QsTuple<Tuple<QsSymbol, QsType>>.QsTuple parameterTuple = 
                (QsTuple<Tuple<QsSymbol, QsType>>.QsTuple)MethodSignature.Argument;
            ProcessTupleParameter(parameterTuple, parameterNames);

            return parameterNames;
        }


        /// <summary>
        /// Parses a parameter that is represented as a tuple, and adds its constituents to the provided
        /// list of named parameters.
        /// </summary>
        /// <param name="TupleParameter">The tuple parameter to parse</param>
        /// <param name="ParameterNames">The collection of parameter names discovered so far</param>
        private void ProcessTupleParameter(QsTuple<Tuple<QsSymbol, QsType>>.QsTuple TupleParameter, List<string> ParameterNames)
        {
            // Go through the tuple and look at all of its elements
            foreach (QsTuple<Tuple<QsSymbol, QsType>> parameter in TupleParameter.Item)
            {
                // If this element is a basic parameter, record its name
                if (parameter is QsTuple<Tuple<QsSymbol, QsType>>.QsTupleItem basicParameter)
                {
                    string parameterName = GetSymbolName(basicParameter.Item.Item1);
                    ParameterNames.Add(parameterName);
                }

                // If this is a nested tuple, process it recursively
                else if (parameter is QsTuple<Tuple<QsSymbol, QsType>>.QsTuple tupleParameter)
                {
                    ProcessTupleParameter(tupleParameter, ParameterNames);
                }
            }
        }


        /// <summary>
        /// Determins whether or not the method returns something based on its signature.
        /// </summary>
        /// <param name="MethodSignature">The method's signature</param>
        /// <returns>True if it returns something, false if the return type is Unit.</returns>
        private bool CheckForReturnType(CallableSignature MethodSignature)
        {
            return MethodSignature.ReturnType.Type != QsTypeKind<QsType, QsSymbol, QsSymbol, Affiliation>.UnitType;
        }

    }
}
