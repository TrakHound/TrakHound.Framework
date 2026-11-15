// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Reflection;

namespace TrakHound.CLI
{
    class CommandGroupMatch
    {
        public Type Type { get; set; }
        public int ArgIndex { get; set; }


        public CommandGroupMatch(Type type, int argIndex)
        {
            Type = type;
            ArgIndex = argIndex;
        }


        public static CommandGroupMatch Match(Type type, string[] args)
        {
            if (type != null && args != null && args.Length > 0)
            {
                var commandGroupAttribute = type.GetCustomAttribute<CommandGroupAttribute>();
                if (commandGroupAttribute != null)
                {
                    var argIndex = MatchNames(commandGroupAttribute.Name, args, 0);
                    if (argIndex > 0) return new CommandGroupMatch(type, argIndex);
                }
            }

            return null;
        }

        /// <summary>
        /// Match a (space separated) command name against the args starting at startIndex.
        /// Returns the index of the next arg after the match, or -1 if not matched
        /// </summary>
        public static int MatchNames(string name, string[] args, int startIndex)
        {
            if (string.IsNullOrEmpty(name)) return -1;

            var argIndex = startIndex;
            foreach (var matchName in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (argIndex >= args.Length || args[argIndex] != matchName) return -1;
                argIndex++;
            }

            return argIndex;
        }
    }
}
