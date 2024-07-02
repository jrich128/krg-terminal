using Godot;
using System;

namespace KrgTerminal
{
	/// <summary>
    /// Data returned from a <c>CommandFunction</c> 
    /// </summary>
    public struct CommandReturn
    {
        public bool Success;
        public string Details;

        public CommandReturn(bool success, string details)
        {
            Success = success; Details = details;
        }
    }

	/// <summary>
	/// Signature for all functions tied to a command
	/// </summary>
	public delegate CommandReturn CommandFunction(string args);

	/// <summary>
	/// Container for all data of a terminal command
	/// </summary>
	public struct TerminalCommand
	{
		public string Key = "";

		public int ArgCount = 0; // 0 = Varible amount
		public string[][] ArgAutocomplete = null; // For autocomplete; Unused if null

		public CommandFunction Function;
		public string HelpText = ""; 
		
		public TerminalCommand(string key, int argCount, CommandFunction function, string helpText)
		{
			Key = key; ArgCount = argCount; Function = function; HelpText = helpText;
		}
	}
}