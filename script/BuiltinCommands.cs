using Godot;
using System;
using KrgTerminal;
using System.IO;
using FileAccess = Godot.FileAccess;


public partial class Terminal : Control
{
	CommandReturn ClearBinds(string args)
    {
        //TODO:InputMap.EraseAction
        throw new NotImplementedException();
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
        if(argsSplit.Length < 3){
            return new CommandReturn(false, "Syntax is: bind <key> <command>");
        }
        
        string keyName     = argsSplit[1];
        string commandName = argsSplit[2];
        string command = args.Remove(0, argsSplit[0].Length + argsSplit[1].Length + 2);//       argsSplit[2];
 
        // Check command is valid
        if(!_commands.TryGetValue(commandName, out _)){
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
        Print(args.Remove(0,4));
        
        return new CommandReturn(true, null);
    }

    CommandReturn ShowLog(string args)
    {
        string output = "";
        foreach(var command in _commandLog)
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
            foreach(var shit in _commands)
            {
                output += shit.Key + '\n'; 
            }
            output += _commands["help"].HelpText + '\n';
            return new CommandReturn(true, output);
        }
        
        TerminalCommand command;
        bool isValidCommand = _commands.TryGetValue(argSplit[1], out command);
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
