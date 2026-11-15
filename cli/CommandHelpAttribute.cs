// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.CLI
{
    [AttributeUsage(AttributeTargets.Method)]
    internal class CommandHelpAttribute : Attribute
    {
        public string Usage { get; set; }

        public string Example { get; set; }


        public CommandHelpAttribute(string usage, string example = null)
        {
            Usage = usage;
            Example = example;
        }
    }
}
