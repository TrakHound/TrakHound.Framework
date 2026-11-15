// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance
{
    /// <summary>
    /// Writes the Instance's startup output (header and parameters) to the Console in a consistent format
    /// </summary>
    internal static class ConsoleOutput
    {
        private const int _width = 80;
        private const int _propertyNameWidth = 24;
        private const string _indent = "  ";


        public static void WriteDivider()
        {
            WriteLine(new string('-', _width), ConsoleColor.DarkGray);
        }

        public static void WriteTitle(string title, string detail = null)
        {
            Write(_indent);
            Write(title, ConsoleColor.Cyan);
            if (!string.IsNullOrEmpty(detail)) Write($"  {detail}");
            Console.WriteLine();
        }

        public static void WriteText(string text)
        {
            WriteLine(_indent + text);
        }

        /// <summary>
        /// Write an aligned "Name   Value" line with an optional note (ex. the source of the Value)
        /// </summary>
        public static void WriteProperty(string name, string value, string note = null)
        {
            Write(_indent + (name ?? string.Empty).PadRight(_propertyNameWidth), ConsoleColor.Gray);
            Write(value ?? string.Empty, ConsoleColor.White);
            if (!string.IsNullOrEmpty(note)) Write($"  ({note})", ConsoleColor.DarkGray);
            Console.WriteLine();
        }


        private static void Write(string text, ConsoleColor? color = null)
        {
            if (color.HasValue)
            {
                Console.ForegroundColor = color.Value;
                Console.Write(text);
                Console.ResetColor();
            }
            else
            {
                Console.Write(text);
            }
        }

        private static void WriteLine(string text, ConsoleColor? color = null)
        {
            Write(text, color);
            Console.WriteLine();
        }
    }
}
