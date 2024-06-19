#if TOOLS
using Godot;
using System;

[Tool]
public partial class TerminalPlugin : EditorPlugin
{
	const string DIR             = "addons/terminal";
	const string TERMINAL_SCRIPT = "Terminal.cs";
	const string TERMINAL_ICON   = "icon.png";
	static string Path(string fileName) => $"{DIR}/{fileName}";
	
	const string TYPE_NAME = "Terminal";
		

	public override void _EnterTree()
	{
		if(!Godot.DirAccess.DirExistsAbsolute(DIR)){
			GD.PrintErr($"Plugin 'terminal' failed to load.\nDirectory not found '{DIR}'");
			return;
        }
		
		string iconPath = Path(TERMINAL_ICON);
		if(!Godot.FileAccess.FileExists(iconPath)){
			GD.PrintErr($"Plugin 'terminal' failed to load.\nFile not found '{iconPath}'");
			return;
		}

		string scriptPath = Path(TERMINAL_SCRIPT);
		if(!Godot.FileAccess.FileExists(scriptPath)){
			GD.PrintErr($"Plugin 'terminal' failed to load.\nFile not found '{scriptPath}'");
			return;
		}

		var icon   = ResourceLoader.Load<Texture2D>(iconPath);
		var script = ResourceLoader.Load<Script>(scriptPath);
		
		AddCustomType(TYPE_NAME, "Control", script, icon);
	}

	public override void _ExitTree()
	{
		RemoveCustomType(TYPE_NAME);
	}
}
#endif
