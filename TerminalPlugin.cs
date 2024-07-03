#if TOOLS
using Godot;
using System;

[Tool]
public partial class TerminalPlugin : EditorPlugin
{
	static string Dir            = "addons/krg-terminal";
	static string TerminalScript = "script/Terminal.cs";
	static string TerminalIcon   = "icon.png";
	static string Path(string fileName) => $"{Dir}/{fileName}";
	
	const string TypeName = "Terminal";


	public override void _EnterTree()
	{
		if(!DirAccess.DirExistsAbsolute(Dir)){
			GD.PrintErr($"Plugin 'terminal' failed to load.\nDirectory not found '{Dir}'");
			return;
        }
		
		string iconPath = Path(TerminalIcon);
		if(!FileAccess.FileExists(iconPath)){
			GD.PrintErr($"Plugin 'terminal' failed to load.\nFile not found '{iconPath}'");
			return;
		}

		string scriptPath = Path(TerminalScript);
		if(!FileAccess.FileExists(scriptPath)){
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
