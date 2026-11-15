// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Diagnostics;

namespace TrakHound
{
	/// <summary>
	/// Runs shell commands using cmd.exe
	/// </summary>
	public static class CommandProcess
	{
		/// <summary>
		/// Runs the command using "cmd /c" and blocks until it exits (returns false if the command is empty or the Process fails to start)
		/// </summary>
		public static bool Run(string command, string workingDirectory = null)
		{
			if (!string.IsNullOrEmpty(command))
			{
				try
				{
					// Set Process Start Info
					var startInfo = new ProcessStartInfo("cmd", "/c " + command);
					//startInfo.RedirectStandardOutput = true;
					//startInfo.RedirectStandardError = true;
					startInfo.UseShellExecute = false;
					startInfo.WorkingDirectory = workingDirectory;

					// Start the Process\
					var process = new Process();
					process.StartInfo = startInfo;
					process.Start();

					// Block until Process is completed
					process.WaitForExit();

					return true;
				}
				catch
				{

				}
			}

			return false;
		}
	}
}
