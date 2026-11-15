// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.CLI
{
    [AttributeUsage(AttributeTargets.Parameter)]
    internal class CommandOptionAttribute : Attribute
    {
        public string Description { get; set; }

        /// <summary>
        /// Optional short alias (ex. "-v")
        /// </summary>
        public string Alias { get; set; }


        public CommandOptionAttribute() { }

        public CommandOptionAttribute(string description, string alias = null)
        {
            Description = description;
            Alias = alias;
        }
    }
}
