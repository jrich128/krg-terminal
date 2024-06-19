# Install
Copy the folder into the addons of your project & rename it to "terminal"
Build & reload your project
Enable the addon 
Create a "terminal_toggle" Action in your Input Map
Add an instance of "terminal.tscn" your scene
You should be ready to rock

# Creating your own commands
All you need to do is call AddCommand() & pass it a filled out TerminalCommand struct
You will need to give TerminalCommand a function that returns a TerminalReturn struct, and takes a string argument
Look at the built-in commands in Terminal.cs for reference

# Features 
- Autocomplete
- History recall & selection via arrow keys
- Binding commands to your keyboard; Like Source Engine
- Color coded output with basic text effects
- Can read text files containg series of commands & execute them via the "run" command. Place them in a folder "cfg" in your project root, give them a ".cfg" extension

This is still a WIP. I've already found it really useful so I thought I'd put it up on here for others

I know there's bout a hundred other terminal addons already, but I had fatal issues with all of them I tried. Took some fukitol & wrote my own for exactly what I needed it for
