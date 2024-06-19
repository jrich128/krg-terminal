Copy the folder into the addons of your project & rename it to "terminal"

Add an instance of "terminal.tscn" your scene, use the '`' key by default to toggle it.

Adding you own commands all you need is a reference to the terminal node to call the AddCommand method. In your script, you will need to write a function that returns 
TerminalReturn & takes a string argument to give to the TerminalCommand struct when you call AddCommand. For reference, look at the bottom of Terminal.cs to see how to write these functions
