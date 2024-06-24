#if TOOLS
using Godot;
using System;

[Tool]
public partial class TerminalPlugin : EditorPlugin
{
	// I made a bigggg fuck here: DONT USE CONST!
	// Godot will cache them if it feels like it & they will 
	// refuse to update unless you change the varible name.
	// GOOD LUCK TRACKING DOWN THAT ERROR! I had a fun time -_-
	const string Dir             = "addons/krg-terminal";
	const string TerminalScript = "Terminal.cs";
	const string TerminalIcon   = "icon.png";
	static string Path(string fileName) => $"{Dir}/{fileName}";
	
	const string TypeName = "Terminal";


	public override void _EnterTree()
	{
		if(!Godot.DirAccess.DirExistsAbsolute(Dir)){
			GD.PrintErr($"Plugin 'terminal' failed to load.\nDirectory not found '{Dir}'");
			return;
        }
		
		string iconPath = Path(TerminalIcon);
		if(!Godot.FileAccess.FileExists(iconPath)){
			GD.PrintErr($"Plugin 'terminal' failed to load.\nFile not found '{iconPath}'");
			return;
		}

		string scriptPath = Path(TerminalScript);
		if(!Godot.FileAccess.FileExists(scriptPath)){
			GD.PrintErr($"Plugin 'terminal' failed to load.\nFile not found '{scriptPath}'");
			return;
		}

		var icon   = ResourceLoader.Load<Texture2D>(iconPath);
		var script = ResourceLoader.Load<Script>(scriptPath);
		
		AddCustomType(TypeName, "Control", script, icon);
	}

	public override void _ExitTree()
	{
		RemoveCustomType(TypeName);
	}
}
#endif
