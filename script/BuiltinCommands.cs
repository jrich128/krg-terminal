using Godot;
using System;
using KrgTerminal;
using System.IO;
using FileAccess = Godot.FileAccess;
using System.Reflection;



public struct TVar
{
	public object Obj;
	public Variant.Type VarType;

	public MemberInfo Member;


	public Variant? Get()
	{
		object value = null;
		
		switch(Member.MemberType)
		{
			case MemberTypes.Field:
			value = ((FieldInfo)Member).GetValue(Obj);
			break;

			case MemberTypes.Property:
			value = ((PropertyInfo)Member).GetValue(Obj);
			break;

			default:
			GD.PrintErr($"Member type: {Member.MemberType} not valid");
			break;
		}

		switch(value)
		{
			case int v:
			return v;

			case string v:
			return v;

			default:
			GD.PrintErr($"{value.GetType()} not compatible");
			break;
		}

		return null;
	}

	// This needs to be re-written
	public void Set(Variant value)
	{
		if(VarType != value.VariantType){
			GD.Print("WRONG TYPE");
			return;
		}

		switch(Member.MemberType)
		{
			case MemberTypes.Field:
				switch(VarType)
				{
					case Variant.Type.Int:
					((FieldInfo)Member).SetValue(Obj, value.As<int>());
					break;
					case Variant.Type.String:
					((FieldInfo)Member).SetValue(Obj, value.As<string>());
					break;
				}
			break;

			case MemberTypes.Property:
			switch(VarType)
				{
					case Variant.Type.Int:
					((PropertyInfo)Member).SetValue(Obj, value.As<int>());
					break;
					case Variant.Type.String:
					((PropertyInfo)Member).SetValue(Obj, value.As<string>());
					break;
				}
			break;
		}
	}
}

public partial class Terminal : Control
{	

	CommandReturn ClearBinds(string args)
    {
        //TODO:InputMap.EraseAction
        throw new NotImplementedException();
    }

	CommandReturn GetTVar(string args)
	{
		var split    = args.Split(' ');
		var tvarKey  = split[1];

		if(!_tvars.ContainsKey(tvarKey)){
			return new CommandReturn()
			{
				Success = false,
				Details = $"No TVar named '{tvarKey}' exists!"
			};
		}

		TVar var = _tvars[tvarKey];
		return new CommandReturn()
		{
			Success = true,
			Details = $"{tvarKey} == {var.Get()}"
		};
	}

	CommandReturn SetTVar(string args)
	{
		var split    = args.Split(' ');
		var tvarKey  = split[1];
		var newValue = split[2];

		if(!_tvars.ContainsKey(tvarKey)){
			return new CommandReturn()
			{
				Success = false,
				Details = $"No TVar named '{tvarKey}' exists!"
			};
		}

		TVar var = _tvars[tvarKey];

		// Try to parse string input to correct type
		switch (var.VarType)
		{
			case Variant.Type.String:
			var.Set(newValue);
			break;

			case Variant.Type.Int:
			int value = 0;
			if(!int.TryParse(newValue, out value)){
				return new CommandReturn(false, $"'{newValue}' cannot parse to int!");
			}
			var.Set(value);
			break;

			default:
			return new CommandReturn(false, $"'{var.VarType}' not supported yet");
		}

		 
		var a = var.Get();
		if(a.HasValue){
			return new CommandReturn(true, $"Tester value {a.Value}");
		}
		return new CommandReturn(true, $"Tester value NULL");
	}

    CommandReturn Quit(string args)
    {
		// Tell all the nodes we quiting
        GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
        
		GetTree().Quit();
        return new CommandReturn(success: true, details: null);
    }

    CommandReturn CreateBind(string args)
    {
        // bind <key> <command>
        string[] argsSplit = args.Split(' ');   
        
        string keyName     = argsSplit[1];
        string commandName = argsSplit[2];
        string command = args.Remove(0, argsSplit[0].Length + argsSplit[1].Length + 2);//       argsSplit[2];
 
        // Check command is valid
        if(!Commands.TryGetValue(commandName, out _)){
            return new CommandReturn(false, $"'{commandName}' Unknown command.");
        }
        // Check if keycode string is valid
        Key key = OS.FindKeycodeFromString(keyName.Replace('_', ' ')); // Whitespace? FFS
        if(key == Key.None){
            return new CommandReturn(false, $"Unrecognized key '{keyName}', check 'bind_keycode_ref.txt' for valid names.");
        }

        Bind bind = new Bind();
        bind.BindCommand = args;
        bind.ActionName  = $"bind_{_binds.Count}";
        bind.Command     = command;

        InputMap.AddAction(bind.ActionName);
        InputEventKey inputEvent = new InputEventKey() {Keycode = key}; 
        InputMap.ActionAddEvent(bind.ActionName, inputEvent);
        
        _binds.Add(bind);

        return new CommandReturn(true, $"'{command}' bound to '{keyName}'");
    }

    CommandReturn RunCfg(string args)
    {
        string[] argsSplit = args.Split(' ');
        if(argsSplit.Length != 2){
            return new CommandReturn(false, $"Invalid command: {args}, incorrect num of arguments!");
        }

        // Make path from name
        string cfgName = argsSplit[1];
        string cfgPath = CfgPath(argsSplit[1]);
       
        // Is file real?
        if(!FileAccess.FileExists(cfgPath)){
            return new CommandReturn(false, $"File '{cfgPath}' doesn't exist!");
        }
        
        // TODO: Replace with Godot's FileAccess
        // Load file & execute commands within
        string[] commands = File.ReadAllLines(cfgPath);
        for(int i = 0; i < commands.Length; i++)
        {
            // Return out if we hit an invalid command 
            if(!Execute(commands[i])){
                return new CommandReturn(false, $"Bad command: {commands[i]}, cfg execution stopped.");
            }
        }
        //Print(ParseCfg(cfgPath));

        return new CommandReturn(true, $"'{cfgName}.cfg' ran");
    }

    CommandReturn Echo(string args)
    {
		// Remove "echo ", print the rest
        Print(args.Remove(0,4));
        
        return new CommandReturn(true, null);
    }

    CommandReturn ShowLog(string args)
    {
        string output = "";
        foreach(var command in CommandLog)
        {
            output += command + '\n';
        }
        return new CommandReturn(true, output);
    }

    CommandReturn Help(string args)
    {
        string output = "";
        string[] argSplit = args.Split(' ');
        // If no command arg, print help's help text along with list of all commands
        if(argSplit.Length == 1){
            foreach(var shit in Commands)
            {
                output += shit.Key + '\n'; 
            }
            output += Commands["help"].HelpText + '\n';
            return new CommandReturn(true, output);
        }
        
        TerminalCommand command;
        bool isValidCommand = Commands.TryGetValue(argSplit[1], out command);
        if(isValidCommand == false){
            return new CommandReturn(false, "Invalid Command");
        }
        return new CommandReturn(true, command.HelpText);
    }

    CommandReturn Clear(string args)
    {
        _output.Clear();
        return new CommandReturn(true, null);
    } 
}
