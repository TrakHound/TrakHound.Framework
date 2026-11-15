// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Reflection;
using System.Text.RegularExpressions;

namespace TrakHound.CLI
{
    internal static class CommandParser
    {
        public const int SuccessExitCode = 0;
        public const int ErrorExitCode = 1;
        public const int UsageExitCode = 2;

        private const string _regexPattern = "(\"[^\"]*\"|[^\\s\"]+)";
        private static readonly Regex _regex = new Regex(_regexPattern);

        private static readonly string[] _helpArgs = new string[] { "/?", "-h", "--help" };


        /// <summary>
        /// Split a single command line string into args (used by the interactive console)
        /// </summary>
        public static string[] Tokenize(string command)
        {
            var args = new List<string>();
            if (!string.IsNullOrEmpty(command))
            {
                foreach (Match match in _regex.Matches(command))
                {
                    var value = match.Groups[1].Value;
                    if (value.Length > 1 && value.StartsWith('"') && value.EndsWith('"')) value = value.Substring(1, value.Length - 2);
                    args.Add(value);
                }
            }
            return args.ToArray();
        }

        public static async Task<int> Run(string[] args)
        {
            args ??= Array.Empty<string>();

            // Help ("help [command]", "[command] --help", "[command] /?")
            if (args.Length == 0)
            {
                PrintHelp();
                return SuccessExitCode;
            }
            if (args[0] == "help")
            {
                return Help(args.Skip(1).ToArray());
            }
            if (_helpArgs.Contains(args[args.Length - 1]))
            {
                return Help(args.Take(args.Length - 1).ToArray());
            }

            try
            {
                var match = FindCommand(args);
                if (match == null)
                {
                    throw new CommandException($"Unknown Command : {string.Join(' ', args)}");
                }

                var commandParameters = GetCommandParameters(match.Method, match.ArgIndex, args);

                return await Invoke(match.Method, commandParameters);
            }
            catch (CommandException ex)
            {
                Console.Error.WriteLine($"Error : {ex.Message}");
                Console.Error.WriteLine("Run 'th help' to list the available commands");
                return UsageExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error : {ex.Message}");
                return ErrorExitCode;
            }
        }

        private static int Help(string[] args)
        {
            if (args.Length == 0)
            {
                PrintHelp();
                return SuccessExitCode;
            }

            var methodMatch = FindCommand(args);
            if (methodMatch != null)
            {
                PrintHelp(methodMatch.Method);
                return SuccessExitCode;
            }

            foreach (var type in GetCommandGroupTypes())
            {
                if (CommandGroupMatch.Match(type, args) != null)
                {
                    PrintHelp(type);
                    return SuccessExitCode;
                }
            }

            Console.Error.WriteLine($"Error : Unknown Command : {string.Join(' ', args)}");
            PrintHelp();
            return UsageExitCode;
        }

        private static CommandMethodMatch FindCommand(string[] args)
        {
            CommandMethodMatch bestMatch = null;
            foreach (var type in GetCommandGroupTypes())
            {
                var match = CommandMethodMatch.Match(type, args);
                if (match != null && (bestMatch == null || match.ArgIndex > bestMatch.ArgIndex))
                {
                    bestMatch = match;
                }
            }
            return bestMatch;
        }

        private static IEnumerable<Type> GetCommandGroupTypes()
        {
            return Assembly.GetExecutingAssembly().GetTypes()
                .Where(o => o.GetCustomAttribute<CommandGroupAttribute>() != null)
                .OrderBy(o => o.GetCustomAttribute<CommandGroupAttribute>().Name);
        }

        private static async Task<int> Invoke(MethodInfo method, object[] parameters)
        {
            object result;
            try
            {
                result = method.Invoke(null, parameters);
                if (result is Task task)
                {
                    await task;
                    var resultProperty = task.GetType().GetProperty("Result");
                    result = method.ReturnType.IsGenericType ? resultProperty?.GetValue(task) : null;
                }
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }

            switch (result)
            {
                case bool success: return success ? SuccessExitCode : ErrorExitCode;
                case int exitCode: return exitCode;
                default: return SuccessExitCode;
            }
        }


        private static object[] GetCommandParameters(MethodInfo method, int argumentIndex, string[] args)
        {
            var parameters = method.GetParameters();
            var values = new object[parameters.Length];
            var isSet = new bool[parameters.Length];

            var positionalParameters = new Queue<int>();
            var options = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                values[i] = parameter.HasDefaultValue ? parameter.DefaultValue : null;

                if (parameter.GetCustomAttribute<CommandParameterAttribute>() != null)
                {
                    positionalParameters.Enqueue(i);
                }

                var optionAttribute = parameter.GetCustomAttribute<CommandOptionAttribute>();
                if (optionAttribute != null)
                {
                    // Accept --kebab-case (preferred) and --camelCase (legacy)
                    options[$"--{parameter.Name.ToKebabCase()}"] = i;
                    options[$"--{parameter.Name.ToCamelCase()}"] = i;
                    if (!string.IsNullOrEmpty(optionAttribute.Alias)) options[optionAttribute.Alias] = i;
                }
            }

            // Options and Parameters can be specified in any order
            var argIndex = argumentIndex;
            while (argIndex < args.Length)
            {
                var arg = args[argIndex++];

                if (arg.StartsWith('-') && arg.Length > 1 && !double.TryParse(arg, out _))
                {
                    // Support "--name=value"
                    string inlineValue = null;
                    var name = arg;
                    var equalsIndex = arg.IndexOf('=');
                    if (equalsIndex > 0)
                    {
                        name = arg.Substring(0, equalsIndex);
                        inlineValue = arg.Substring(equalsIndex + 1);
                    }

                    if (!options.TryGetValue(name, out var parameterIndex))
                    {
                        throw new CommandException($"Unknown Option : {name}");
                    }

                    var parameter = parameters[parameterIndex];
                    var parameterType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

                    string value;
                    if (inlineValue != null)
                    {
                        value = inlineValue;
                    }
                    else if (parameterType == typeof(bool))
                    {
                        // A bool option with no value is 'true', but an explicit 'true/false' can follow it
                        value = "true";
                        if (argIndex < args.Length && bool.TryParse(args[argIndex], out _)) value = args[argIndex++];
                    }
                    else
                    {
                        if (argIndex >= args.Length) throw new CommandException($"Missing value for Option : {name}");
                        value = args[argIndex++];
                    }

                    if (parameterType == typeof(string[]))
                    {
                        // A string[] option can be repeated (ex. --param a=1 --param b=2)
                        var existing = isSet[parameterIndex] ? (string[])values[parameterIndex] : Array.Empty<string>();
                        values[parameterIndex] = existing.Append(value).ToArray();
                    }
                    else
                    {
                        values[parameterIndex] = ConvertValue(value, parameter, name);
                    }
                    isSet[parameterIndex] = true;
                }
                else
                {
                    if (positionalParameters.Count == 0)
                    {
                        throw new CommandException($"Unexpected Argument : {arg}");
                    }

                    var parameterIndex = positionalParameters.Dequeue();
                    values[parameterIndex] = ConvertValue(arg, parameters[parameterIndex], parameters[parameterIndex].Name);
                    isSet[parameterIndex] = true;
                }
            }

            for (var i = 0; i < parameters.Length; i++)
            {
                if (!isSet[i] && !parameters[i].HasDefaultValue)
                {
                    throw new CommandException($"Missing required Parameter : {parameters[i].Name}");
                }
            }

            return values;
        }

        private static object ConvertValue(string value, ParameterInfo parameter, string name)
        {
            var targetType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

            try
            {
                if (targetType == typeof(string)) return value;
                if (targetType.IsEnum) return Enum.Parse(targetType, value, true);
                return Convert.ChangeType(value, targetType);
            }
            catch
            {
                throw new CommandException($"Invalid value for {name} : '{value}' (expected {targetType.Name})");
            }
        }


        private static void PrintHelp()
        {
            Console.WriteLine("Usage : th [group] [command] [parameters] [options]");
            Console.WriteLine();
            Console.WriteLine("Command Groups");
            Console.WriteLine("-------------------");

            foreach (var type in GetCommandGroupTypes())
            {
                var groupAttribute = type.GetCustomAttribute<CommandGroupAttribute>();
                Console.WriteLine(string.Format("  {0,-16} {1}", groupAttribute.Name, groupAttribute.Description));

                foreach (var method in CommandMethodMatch.GetCommandMethods(type))
                {
                    var commandAttribute = method.GetCustomAttribute<CommandAttribute>();
                    Console.WriteLine(string.Format("      {0,-16} {1}", commandAttribute.Name, commandAttribute.Description));
                }
                Console.WriteLine();
            }

            Console.WriteLine("Run 'th help [group] [command]' or 'th [group] [command] --help' for more information");
        }

        private static void PrintHelp(Type type)
        {
            var commandGroupAttribute = type.GetCustomAttribute<CommandGroupAttribute>();
            if (commandGroupAttribute != null)
            {
                Console.WriteLine($"Command Group : {commandGroupAttribute.Name}");
                if (!string.IsNullOrEmpty(commandGroupAttribute.Description)) Console.WriteLine(commandGroupAttribute.Description);
                Console.WriteLine();
                Console.WriteLine("Commands");
                Console.WriteLine("-------------------");
                Console.WriteLine();

                foreach (var method in CommandMethodMatch.GetCommandMethods(type))
                {
                    PrintHelp(method);
                    Console.WriteLine();
                }
            }
        }

        private static void PrintHelp(MethodInfo method)
        {
            var commandAttribute = method.GetCustomAttribute<CommandAttribute>();
            if (commandAttribute != null)
            {
                var groupName = method.DeclaringType.GetCustomAttribute<CommandGroupAttribute>()?.Name;
                var helpAttribute = method.GetCustomAttribute<CommandHelpAttribute>();
                var shift = "    ";

                Console.WriteLine($"Command : {commandAttribute.Name}");
                if (!string.IsNullOrEmpty(commandAttribute.Description)) Console.WriteLine(commandAttribute.Description);
                Console.WriteLine();

                var usage = !string.IsNullOrEmpty(helpAttribute?.Usage) ? helpAttribute.Usage : GetUsage(groupName, commandAttribute.Name, method);
                Console.WriteLine(shift + $"Usage : {usage}");
                if (!string.IsNullOrEmpty(helpAttribute?.Example)) Console.WriteLine(shift + $"Example : {helpAttribute.Example}");
                Console.WriteLine();

                var parameters = method.GetParameters();
                if (!parameters.IsNullOrEmpty())
                {
                    Console.WriteLine(shift + string.Format("{0,-30} |{1,10} |{2,10} |   {3}", "Name", "Type", "Required", "Description"));
                    Console.WriteLine(shift + string.Format("{0,-30} {1,10} {2,10} {3}", new string('-', 31), new string('-', 11), new string('-', 11), "---------------"));

                    foreach (var parameter in parameters)
                    {
                        string name = null;
                        string parameterType = null;
                        string parameterDescription = null;
                        var required = parameter.HasDefaultValue ? "Optional" : "Required";

                        var commandParameterAttribute = parameter.GetCustomAttribute<CommandParameterAttribute>();
                        if (commandParameterAttribute != null)
                        {
                            name = parameter.Name;
                            parameterType = "Parameter";
                            parameterDescription = commandParameterAttribute.Description;
                        }

                        var commandOptionAttribute = parameter.GetCustomAttribute<CommandOptionAttribute>();
                        if (commandOptionAttribute != null)
                        {
                            name = $"--{parameter.Name.ToKebabCase()}";
                            if (!string.IsNullOrEmpty(commandOptionAttribute.Alias)) name += $", {commandOptionAttribute.Alias}";
                            parameterType = "Option";
                            parameterDescription = commandOptionAttribute.Description;
                        }

                        if (parameterType != null)
                        {
                            if (parameter.HasDefaultValue && parameter.DefaultValue != null) parameterDescription += $" (default: {parameter.DefaultValue})";

                            Console.WriteLine(shift + string.Format("{0,-30} |{1,10} |{2,10} |   {3}", name, parameterType, required, parameterDescription));
                        }
                    }
                }
            }
        }

        private static string GetUsage(string groupName, string commandName, MethodInfo method)
        {
            var parts = new List<string> { "th" };
            if (!string.IsNullOrEmpty(groupName)) parts.Add(groupName);
            parts.Add(commandName);

            foreach (var parameter in method.GetParameters())
            {
                if (parameter.GetCustomAttribute<CommandParameterAttribute>() != null)
                {
                    parts.Add(parameter.HasDefaultValue ? $"[{parameter.Name}]" : $"<{parameter.Name}>");
                }
            }

            if (method.GetParameters().Any(o => o.GetCustomAttribute<CommandOptionAttribute>() != null))
            {
                parts.Add("[options]");
            }

            return string.Join(' ', parts);
        }
    }
}
