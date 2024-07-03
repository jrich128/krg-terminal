using Godot;
using System;
using System.Linq;


namespace KrgTerminal
{
	public struct InputState
	{
		public bool HasInput;
		public string[] Words; // Non-empty strings only
		public string[] Split; // Raw output of string.Split
		public TerminalCommand? Command;

		public int EndSpaceCount() => Split.Length - Words.Length;
		public int ArgIndex() => Mathf.Max(Words.Count() - 2 + EndSpaceCount(), 0);
		
		// This needs to be re-written
		public int LastWordStartIndex()
		{
			int argIndex = ArgIndex();
			int charCount = 0;

			

			if(EndSpaceCount() > 0)
			{
				for(int i = 0; i < argIndex + 1; i++)
				{
					charCount += Words[i].Length;
					charCount += 1;
				}

				return charCount;
			}
			else
			{
				for(int i = 0; i < Words.Length - 1; i++)
				{
					charCount += Words[i].Length;
					charCount += 1; // space
				}
				return charCount;
			}
		} 
	} 
}