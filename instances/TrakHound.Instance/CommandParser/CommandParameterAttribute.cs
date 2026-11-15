// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance
{
    [AttributeUsage(AttributeTargets.Parameter)]
    internal class CommandParameterAttribute : Attribute
    {
        public string Description { get; set; }


        public CommandParameterAttribute() { }

        public CommandParameterAttribute(string description)
        {
            Description = description;
        }
    }
}
