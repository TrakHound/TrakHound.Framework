// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Reflection;

namespace TrakHound.CLI
{
    class CommandMethodMatch
    {
        public MethodInfo Method { get; set; }
        public int ArgIndex { get; set; }


        public CommandMethodMatch(MethodInfo method, int argIndex)
        {
            Method = method;
            ArgIndex = argIndex;
        }


        public static CommandMethodMatch Match(Type type, string[] args)
        {
            var groupMatch = CommandGroupMatch.Match(type, args);
            if (groupMatch != null)
            {
                CommandMethodMatch bestMatch = null;

                // Use the longest matching Command Name (ex. "set-variable" vs "set")
                foreach (var method in GetCommandMethods(type))
                {
                    var commandAttribute = method.GetCustomAttribute<CommandAttribute>();
                    var argIndex = CommandGroupMatch.MatchNames(commandAttribute.Name, args, groupMatch.ArgIndex);
                    if (argIndex > 0 && (bestMatch == null || argIndex > bestMatch.ArgIndex))
                    {
                        bestMatch = new CommandMethodMatch(method, argIndex);
                    }
                }

                return bestMatch;
            }

            return null;
        }

        public static IEnumerable<MethodInfo> GetCommandMethods(Type type)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(o => o.GetCustomAttribute<CommandAttribute>() != null);
        }
    }
}
