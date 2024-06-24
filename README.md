# Krg-Terminal
A simple, easily extensible, Quake-esque developer terminal for Godot in C#

## Features 
- Autocomplete
- History recall & selection via arrow keys
- Binding commands to your keyboard; Like Source Engine
- Color coded output with basic text effects
- Can read text files containg series of commands & execute them via the "run" command. Place them in a folder "cfg" in your project root, give them a ".cfg" extension

## Install
1. Copy "krg-terminal-main" into the addons folder of your project & rename it to "krg-terminal"
2. Build & reload your project
3. Enable the addon in Project->ProjectSettings->Plugins
4. Create a "terminal_toggle" Action in your Input Map
5. Add an instance of "terminal.tscn" your scene

You should be ready

## Creating your own commands
1. Write a function that returns <code>TerminalReturn</code> & takes a <code>string</code> parameter. 
```
    TerminalReturn YourTFunction(string args)
    {
        return new TerminalReturn(true, "I am doing nothing right now!");
    }
```
2. Create a <code>TerminalCommand</code> struct instance, fill it out with either constructor or initializer 
```
    var yourCommand = new TerminalCommand()
    {
        Key      = "test",
        ArgCount = 1,
        Function = YourTFunction,
        HelpText = "Just testing"
    };

    _terminal.AddCommand(yourCommand.Key, yourCommand);
```
3. Call Terminal.AddCommand(), passing it your Terminal Command struct
```
    _terminal.AddCommand(yourCommand.Key, yourCommand);
```

Look at the built-in commands in Terminal.cs for reference

## Contributions
I'm open to contributions. If you have any questions/concerns whatnot you can reach me at jrich128@proton.me

