// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.CLI
{
    /// <summary>
    /// Thrown when the command line arguments are invalid (unknown command, missing parameter, invalid option, etc.)
    /// </summary>
    internal class CommandException : Exception
    {
        public CommandException(string message) : base(message) { }
    }
}
