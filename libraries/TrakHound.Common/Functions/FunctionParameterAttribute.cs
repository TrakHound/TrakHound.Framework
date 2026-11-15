// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Functions
{
    /// <summary>
    /// Describes an Input Parameter of a Function (add one per Parameter to the Function class). The Admin interface fills in its Run and Job
    /// fields from them, a Run gets the DefaultValue when the Parameter isn't passed, and a Run without a Required Parameter is refused (400).
    /// The Function still reads Parameters with GetParameter, and can be passed Parameters that aren't described
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public class FunctionParameterAttribute : Attribute
    {
        /// <summary>
        /// The name the Function reads the Parameter by
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// What the Parameter is for (shown with its field and to AI agents)
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The kind of value it takes (String by default)
        /// </summary>
        public TrakHoundFunctionParameterType Type { get; set; }

        /// <summary>
        /// A Run without a value for the Parameter is refused
        /// </summary>
        public bool Required { get; set; }

        /// <summary>
        /// The value used when the Parameter isn't passed
        /// </summary>
        public string DefaultValue { get; set; }

        /// <summary>
        /// The values it can take (shown as a list to choose from). Any value is allowed when empty
        /// </summary>
        public string[] Options { get; set; }

        /// <summary>
        /// The value is a secret (ex. a password or key): it is masked when entered and isn't saved with a Run's Input
        /// </summary>
        public bool Secret { get; set; }


        /// <summary>
        /// Describes the Function Input Parameter with the specified name
        /// </summary>
        public FunctionParameterAttribute(string name)
        {
            Name = name;
        }
    }
}
