#define DBPRINT
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using FileAccess = Godot.FileAccess; // We want to use Godot's not C#'s


// Next commit: Fleshed out autocomplete. Removed extension method to a local function
// Next push: Overhauled input validation; Added autocomplete for command args
/*
    TODO:
        - Add valid key names to bind's autocomplete 
        - Load autocomplete data from text file for big ones

        We need to make some kind of system to either give precidence
        or not let already occupied keys be bound

        - Ability to give commands their own autocomplete dictionaries
        for their args

        - unbind & clearbinds commands
        - Fix key name confusion - Why are key names listed with the 
        "key_" prefix, but that's invalid for OS.FindKeycodeFromString()????????
        and keys with two word names use whitespace instead of underscores too? Ffs
*/

/// <summary>
/// Key bind to execute a terminal command
/// </summary>
struct Bind
{
    public string BindCommand; // Command that created the bind; Used to serialize in binds.cfg
    public string ActionName;  // Name in Godot's InputMap
    public string Command;     // Terminal Command to be Executed on ActionName pressed

    public Bind(string bindCommand, string actionName, string command)
    {
        (BindCommand, ActionName, Command) = (bindCommand, actionName, command);
    }
}

/// <summary>
/// Data returned from a <c>TerminalFunction</c> 
/// </summary>
public struct TerminalReturn
{
    public bool Success;
    public string Details;

    public TerminalReturn(bool success, string details)
    {
        Success = success; Details = details;
    }
}

/// <summary>
/// Signature for all functions tied to a terminal command
/// </summary>
public delegate TerminalReturn TerminalFunction(string args);

public struct TerminalCommand
{
    public string Key = "";

    public int ArgCount = 0; // 0 = Varible amount
    public string[][] ValidArgs = null; // For autocomplete; Unused if null

    public TerminalFunction Function;
    public string HelpText = ""; 
    
    public TerminalCommand(string key, int argCount, TerminalFunction function, string helpText)
    {
        Key = key; ArgCount = argCount; Function = function; HelpText = helpText;
    }
}

/// <summary>
/// BBCode text style effects; Not all implemented yet!
/// </summary>
[Flags] public enum TStyleFlag
{
    None      = 0,
    Italic    = 1 << 0,
    Bold      = 1 << 1,
    Underline = 1 << 2,
    Error     = Bold
}

/// <summary>
/// Terminal text colors 
/// </summary>
public enum TColor
{
    Default,
    White  ,
    Black  ,
    Red    , 
    Green  ,
    Blue   ,

    Error = Red
}


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


public partial class Terminal : Control
{
    [Signal] public delegate void OpenedEventHandler(bool state);

    const string CFG_DIR      = "cfg";
    const string BINDS_PATH   = $"{CFG_DIR}/binds.cfg";
    const string AUTORUN_PATH = $"{CFG_DIR}/autorun.cfg";
    static string CfgPath(string name) => $"{CFG_DIR}/{name}.cfg";
    
    Dictionary<string, TerminalCommand> _commands = new Dictionary<string, TerminalCommand>();

    // A dictionary for each string arg type Ex. command, mapname 
    Dictionary<string, string[]> _autoCompleteDics = new Dictionary<string, string[]>();

    static Dictionary<TColor, Color> _color = new Dictionary<TColor, Color>()
    {
        // This mess allows Print to have a default for color.
        // Using Godot's built-in colors is too much of pain
        {TColor.White, Colors.White},
        {TColor.Black, new Color(0.1f , 0.1f, 0.1f )},
        {TColor.Red  , new Color(0.65f, 0.15f,0.15f)},
        {TColor.Green, new Color(0.57f, 0.7f, 0.23f)},
        {TColor.Blue,  new Color(0.0f , 0.0f, 0.51f)}
    };

    List<Bind> _binds = new List<Bind>();

    // Log of all input submitted
    List<string> _commandLog = new List<string>();
    int _logCursor = 0;

    
    const int SuggestionCount = 6;
    Button[] _suggs = new Button[SuggestionCount];
    int _logBorderWidth = 2; // Width of left border when dislaying _commandLog as suggestions
    Node _suggParent; 
    
    RichTextLabel _output;
    LineEdit      _input;
    bool HaveInput      => _input.Text != "";
    bool SuggsHaveFocus => _suggs.Any(sug => sug.HasFocus());

    AnimationPlayer _animPlayer;
    Viewport _viewport;

    InputState inputState;
    public InputState GetInputState() => inputState;


    public override void _Ready()
    {
        // Create cfg directory if none
        if(!DirAccess.DirExistsAbsolute(CFG_DIR)){
            Directory.CreateDirectory(CFG_DIR);
        }
        // Create AutoRun.cfg if none 
        if(!FileAccess.FileExists(AUTORUN_PATH)){
            using(var file = Godot.FileAccess.Open(AUTORUN_PATH, Godot.FileAccess.ModeFlags.Write)){
            }
        }

        _output = GetNode<RichTextLabel>("margin/vbox/output");
        _input  = GetNode<LineEdit>("margin/vbox/input");
        _input.Connect("text_submitted", new Callable(this, "SubmitInput"));
        _input.Connect("focus_entered" , new Callable(this, "OnInputFocusEntered"));
        _input.Connect("text_changed"  , new Callable(this, "OnInputChanged"));

        _animPlayer = GetNode<AnimationPlayer>("animation_player");
        
        _suggParent = GetNode("margin/vbox/input/sug_buttons");
        for(int i = 0; i < SuggestionCount; i++){
            _suggs[i] = _suggParent.GetNode<Button>($"sug_{i}");
            _suggs[i].Connect("pressed", new Callable(this, "SuggestionClicked"));
            _suggs[i].Text = "NULL";
        }

        _viewport = GetViewport(); 
        if(_viewport == null){
            GD.PrintErr($"Terminal: Could not get viewport.");
            return;
        }

        // Check that key binds exist
        if(!InputMap.HasAction("terminal_toggle")){
            GD.PrintErr("Terminal: " + "Must create Action 'terminal_toggle' in Input Map!");
            Print("Must create Action 'terminal_toggle' in Input Map!");
        }

        AddCommand( 
        new TerminalCommand("help", 0, Help, "'help <command>' Display info on command"));

        AddCommand( 
        new TerminalCommand("clear", 0, Clear, "'clear' Clear output log"));

        AddCommand( 
        new TerminalCommand("run", 1, RunCfg, "'run <filename>' Reads a text file from 'cfg/' directory with commands in it & executes them"));

        AddCommand( 
        new TerminalCommand("bind", 2, CreateBind, "'bind <key> <command>' Binds a key to execute a command"));
        
        AddCommand( 
        new TerminalCommand("echo", 1, Echo, "'echo <text>' Prints text to the output log"));

        AddCommand( 
        new TerminalCommand("quit", 0, Quit, "'quit' Quits game"));

        AddCommand( 
        new TerminalCommand("log", 0, ShowLog, "'log' Prints command history to output log"));




        TerminalCommand tComm = new TerminalCommand()
        {
            Key = "test",
            ArgCount = 1,
            ValidArgs = new string[][]
            { 
                new string[]{"bass", "baps0", "banmss1", "ass2"}, 
            },
            Function = ShowLog,
            HelpText = "test"
        };

        AddCommand(tComm);


        TerminalCommand tComm0 = new TerminalCommand()
        {
            Key = "testj",
            ArgCount = 1,
            ValidArgs = new string[][]
            { 
                new string[]{"bass", "bacs0", "ass1", "ass2"}, 
            },
            Function = ShowLog,
            HelpText = "test"
        };

        AddCommand(tComm0);

        TerminalCommand tComm1 = new TerminalCommand()
        {
            Key = "tectj",
            ArgCount = 1,
            ValidArgs = new string[][]
            {
                new string[]{"bass", "bacsy", "bass0", "ass1", "ass2"}, 
                new string[]{"ii.txt"},
                new string[]{"bguuuy", "bfuj", "ass2"}
            },
            Function = ShowLog,
            HelpText = "test"
        };

        AddCommand(tComm1);

        Execute("run autorun");

        LoadBinds();

        GetFileAsLines("s");

        base._Ready();
    }  

    string[] GetFileAsLines(string name)
    {
        string file = FileAccess.GetFileAsString("addons/krg-terminal/keycodes.txt");
        Error error = FileAccess.GetOpenError();
        if(error != Error.Ok){
            GD.PrintErr("Terminal couldnt load ignes");
        }
        string[] fileLines = file.Split('\n');

        return fileLines;
    }

    public override void _Input(InputEvent @event)
    {
        // Execute bound commands on Action pressed
        for(int i = 0; i < _binds.Count; i++)
        {
            Bind bind = _binds[i];

            if(@event.IsActionPressed(bind.ActionName)){
                Execute(bind.Command);
                //GD.Print($"Bind: {bind.Command}, Action: {bind.ActionName}");
                //_viewport.SetInputAsHandled();
                return;
            }
        }

        // Open & Close, default '`'
        if(@event.IsActionPressed("terminal_toggle"))
        {
            if(Visible){
                Close();
            }
            else{
                Open();
            }
            
            _viewport.SetInputAsHandled();
            return;
        }
        // Clear input with delete
        if(@event.IsActionPressed("ui_text_delete"))
        {
            _input.Text = "";
            _viewport.SetInputAsHandled(); 
            return;
        }
        // Show command history in suggestions
        if(@event.IsActionPressed("ui_up"))
        {
            if(SuggsHaveFocus){
                return;
            }
            if(SuggestionShowHist()){
                _suggs[0].GrabFocus();
                _viewport.SetInputAsHandled(); 
                return;
            }
        }
        // Give focus to suggestions
        if(@event.IsActionPressed("ui_down"))
        { 
            if(SuggsHaveFocus){
                return;
            }
            _suggs[0].GrabFocus();
            _viewport.SetInputAsHandled();
            return;
        }
        // Tab to bring focus back to input text edit
        if(@event.IsActionPressed("ui_focus_next"))
        {
            _input.GrabFocus();
            _viewport.SetInputAsHandled();
            return;
        }
        // Esc to clear suggestions & give focus back to input
        if(@event.IsActionPressed("ui_cancel"))
        {
            _input.GrabFocus();
            SuggestionClear();
            _viewport.SetInputAsHandled();
            return;
        }

        base._Input(@event);
    }

    bool Execute(string input)
    {
        // Convert to lower case only 
        string inputLowercase = input.ToLower();
        // Convert into string array
        string[] splitInput = inputLowercase.Split(' ');
        string commandName  = splitInput[0];

        // TODO: Replace with StringToCommand
        // Check if first string in command is valid command, execute it if so 
        TerminalCommand command;
        bool isValidCommand = _commands.TryGetValue(commandName, out command);
        if(isValidCommand == false){
            return false;
        }

        // Check for correct num of args
        if(command.ArgCount != 0){
            if(splitInput.Length < 1 + command.ArgCount){
                Print($"{command.Key} Incorect num of args! \nHelp:{command.HelpText}", true, TColor.Red, TStyleFlag.Error);
                return false;
            }
        }

        // Execute function
        TerminalReturn funcReturn = command.Function(input);
        if(funcReturn.Details != null){
            Print(
                funcReturn.Details, true, 
                funcReturn.Success? TColor.Default : TColor.Red,
                funcReturn.Success? TStyleFlag.None : TStyleFlag.Error);
        }

        return true;
    }

    public void SubmitInput(string text)
    {
        Print("===============================================", true, TColor.Black, TStyleFlag.Underline | TStyleFlag.Bold);

        _commandLog.Add(text);
        _logCursor = _commandLog.Count - 1; // reset history cursor 

        // Log input in output 
        Print(">" + text, true, TColor.Green);

        // Delete input from line edit if it came from there
        if(_input.Text == text){
            _input.Text = "";
        }

        // See if Input text is a valid command
        if(Execute(text) == false)
        {
            Print("Unknown command", true, TColor.Error, TStyleFlag.Error);
        }

        SuggestionClear();
    }

    public void AddCommand(TerminalCommand command)
    {
        _commands.Add(command.Key, command);
    }

    public void Print(string text, bool newLine = true, TColor color = TColor.Default, TStyleFlag flags = TStyleFlag.None)
    {
        if(text == null || text.Length < 1){
            return;
        }

        // BBCode formatting 
        if(color != TColor.Default)             _output.PushColor(_color[color]);
        if(flags.HasFlag(TStyleFlag.Italic))    _output.PushItalics();
        if(flags.HasFlag(TStyleFlag.Bold))      _output.PushBold();
        if(flags.HasFlag(TStyleFlag.Underline)) _output.PushUnderline();

        _output.AppendText(text + (newLine? '\n':""));

        _output.PopAll();

        // It scrolls to top when text added, this scrolls back to bottom
        _output.ScrollToLine(_output.GetLineCount()); 
    }

    TerminalCommand? StringToCommand(string input)
    {
        TerminalCommand result;

        // Check first word against command dictionary
        string commandName = input.Split(' ').First();
        bool validCommand = _commands.TryGetValue(commandName, out result);
        if(validCommand == false){
            return null;
        }

        return result;
    }

    void SuggestionClicked()
    {
        // Grab text from focused suggestion button
        string suggestion = _suggs.Single(sugg => sugg.HasFocus()).Text;

        // Insert arg to existing command 
        if(inputState.Command.HasValue){
            _input.DeleteText(inputState.LastWordStartIndex(), _input.Text.Length);
            _input.InsertTextAtCaret(suggestion);
        }
        // Insert command 
        else
        {
            _input.Text = suggestion;
        }

        // Move caret to end of input & select input line edit
        _input.CaretColumn = _input.Text.Length; 
        _input.GrabFocus();

        SuggestionClear();                                     
    }

    public void OnInputChanged(string newText)
    {
        inputState = new InputState()
        {   
            HasInput = HaveInput,
            Split = newText.Split(' '), 
            Words = newText.Split(' ').Where(word => word != "").ToArray(),
            Command = StringToCommand(newText)
        };     

        SuggestionShowMatches();
    }

    void SuggestionShowMatches()
    {   
        /// <summary>
        /// Sequential similarity from 0.0f to 1.0f 
        /// </summary>
        static float Similarity(string a, string b)
        {
            // We loop checking the char's until we hit a non-match
            float matchingCharCount = 0; 
            for(int i = 0; i < Mathf.Min(a.Length, b.Length); i++)
            {   
                if(a[i] != b[i]){
                    return 0.0f;
                }
                matchingCharCount += 1.0f;
            }
            // Return percentage of characters that match
            return (matchingCharCount / (a.Length+b.Length)) * 2.0f;
        }

        static string[] Matches(string input, string[] strings)
        {   
            GD.Print("\n\nInput: " + input);

            // Pair strings & their similarity into an array of tuples
            (string text, float sim)[] potMatches = new (string text, float sim)[strings.Length];
            for(int i = 0; i < strings.Length; i++)
            {
                potMatches[i] = (strings[i], Similarity(strings[i], input));
            }

            GD.Print("Matches:_____________");
            // Find all matches i.e. any strings with a sim > 0 && != 1 as we don't want to suggest an already complete word
            var matches = potMatches.Where(e => e.sim > 0.0f && e.sim != 1.0f);
            foreach(var match in matches)
            {
                GD.Print(match);
            }

            GD.Print("Matches sorted by complement of perecentage similarity:_____________");
            // Sort based on complement of perectage similarity
            matches = matches.OrderBy(e => (1.0f - e.sim));
            foreach(var match in matches)
            {
                GD.Print(match);
            }            

            // Create string[] with just the text from the sorted tuple array
            string[] output = new string[matches.Count()];
            for(int i = 0; i < output.Length; i++)
            {
                output[i] = matches.ElementAt(i).text;
            }

            GD.Print("Output Len: " + output.Length);
            
            return output;
        }

        void SetSuggestions(string[] text)
        {
            // Offset sugg box to start of word to be autocompleted. 13 pulled directly from my ass
            ((Control)_suggParent).OffsetLeft = 13 * inputState.LastWordStartIndex();

            //SuggestionClear();
            for(int i = 0; i < text.Length && i < SuggestionCount; i++){
                _suggs[i].Text = text[i];
                _suggs[i].Visible = true;
            }
        }

        SuggestionClear();

        // Nothing to suggest if no input
        if(!inputState.HasInput){
            return;
        }

        string compare        = null;
        string[] validStrings = null;
        string[] matches      = null;
        
        // Autocomplete for commands 
        if(!inputState.Command.HasValue){
            compare = inputState.Words[0];            // Find command at first index
            validStrings = _commands.Keys.ToArray();  // Get string[] of all potential commands
            matches = Matches(compare, validStrings); // Find all that match
            SetSuggestions(matches);
            return;
        }

        // Autocomplete for args _______________________________ 

        TerminalCommand command = inputState.Command.Value;
        
        // Command has args?
        if(command.ArgCount == 0){
            return;
        }

        // Command has AutoComplete data?
        if(command.ValidArgs == null){
            return;
        }   

        int argIndex = inputState.ArgIndex();

        // Are more args in input than the command takes?
        if(argIndex > command.ArgCount){
            return;
        }

        // Does arg have autocomplete data?
        if(command.ValidArgs[argIndex] == null){
            return;
        } 

        // Show all valid args if no arg input yet
        if(inputState.EndSpaceCount() == 1){
            validStrings = command.ValidArgs[argIndex];
            SetSuggestions(validStrings);
            return;
        }

        // Get arg input
        compare = inputState.Words.Last();
        validStrings = command.ValidArgs[argIndex];
        matches = Matches(compare, validStrings);
        SetSuggestions(matches);
    }

    void SuggestionClear()
    {   
        _suggs.All(sugg => {
            sugg.Text = "";  
            sugg.Visible = false;  
            ((StyleBoxFlat)sugg.GetThemeStylebox("normal")).BorderWidthLeft = 0;
            ((StyleBoxFlat)sugg.GetThemeStylebox("focus")).BorderWidthLeft = 0;
            ((StyleBoxFlat)sugg.GetThemeStylebox("hover")).BorderWidthLeft = 0;
            return true;
            });
    }

    bool SuggestionShowHist()
    {
        SuggestionClear();
        if(_commandLog.Count == 0){
            return false;
        }

        // Reverse to show most recent at top
        var histReverse = _commandLog.ToArray();
        Array.Reverse(histReverse);

        for(int i = 0; i < _commandLog.Count && i < SuggestionCount; i++)
        {   
            // Give suggestion buttons a mark on the left side for color coding
            ((StyleBoxFlat)_suggs[i].GetThemeStylebox("normal")).BorderWidthLeft = _logBorderWidth;
            ((StyleBoxFlat)_suggs[i].GetThemeStylebox("focus")).BorderWidthLeft  = _logBorderWidth;
            ((StyleBoxFlat)_suggs[i].GetThemeStylebox("hover")).BorderWidthLeft  = _logBorderWidth;
            
            _suggs[i].Text = histReverse[i];
            _suggs[i].Visible = true;
        }

        return true;
    }

    void SaveBinds()
    {
        using(var file = Godot.FileAccess.Open(BINDS_PATH, FileAccess.ModeFlags.WriteRead))
        {
            string[] lines = file.GetAsText(true).Split('\n');

            for(int i = 0; i < _binds.Count; i++){
                if(lines.Contains(_binds[i].BindCommand)){
                    continue;
                }
                file.StoreLine(_binds[i].BindCommand);
            }
        }
    }

    void LoadBinds()
    {
        if(!Godot.FileAccess.FileExists(BINDS_PATH)){
            return;
        }

        using(var file = Godot.FileAccess.Open(BINDS_PATH, FileAccess.ModeFlags.Read))
        {
            string[] lines = file.GetAsText(true).Split('\n');

            for(int i = 0; i < lines.Length; i++){
                CreateBind(lines[_binds.Count]);
            }
        }
    }

    public void Close()
    {
        _animPlayer.PlayBackwards("open");
        _animPlayer.Connect("animation_finished", new Callable(this, "CloseAnimFinish"));
    }
    void CloseAnimFinish(string animName)
    {
        _animPlayer.Disconnect("animation_finished", new Callable(this, "CloseAnimFinish"));
        Visible = false;
        EmitSignal(SignalName.Opened, Visible);
    }
    
    public void Open()
    {
        _input.GrabFocus();
        Visible = true;
        _animPlayer.Play("open");
        EmitSignal(SignalName.Opened, Visible);
    }

    public override void _Notification(int what)
    {
        // Is game closing?
        if(what == NotificationWMCloseRequest){
            SaveBinds();
        }
    }

    /* All bellow are our built-in Terminal Commands */
    
    TerminalReturn ClearBinds(string args)
    {
        //TODO:InputMap.EraseAction
        throw new NotImplementedException();
    }

    TerminalReturn Quit(string args)
    {
        GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest);
        GetTree().Quit();
        return new TerminalReturn(true, null);
    }

    TerminalReturn CreateBind(string args)
    {
        // bind <key> <command>
        string[] argsSplit = args.Split(' ');
        if(argsSplit.Length < 3){
            return new TerminalReturn(false, "Syntax is: bind <key> <command>");
        }
        
        string keyName     = argsSplit[1];
        string commandName = argsSplit[2];
        string command = args.Remove(0, argsSplit[0].Length + argsSplit[1].Length + 2);//       argsSplit[2];
 
        // Check command is valid
        if(!_commands.TryGetValue(commandName, out _)){
            return new TerminalReturn(false, $"'{commandName}' Unknown command.");
        }
        // Check if keycode string is valid
        Key key = OS.FindKeycodeFromString(keyName.Replace('_', ' ')); // Whitespace? FFS
        if(key == Key.None){
            return new TerminalReturn(false, $"Unrecognized key '{keyName}', check 'bind_keycode_ref.txt' for valid names.");
        }

        Bind bind = new Bind();
        bind.BindCommand = args;
        bind.ActionName  = $"bind_{_binds.Count}";
        bind.Command     = command;

        InputMap.AddAction(bind.ActionName);
        InputEventKey inputEvent = new InputEventKey() {Keycode = key}; 
        InputMap.ActionAddEvent(bind.ActionName, inputEvent);
        
        _binds.Add(bind);

        return new TerminalReturn(true, $"'{command}' bound to '{keyName}'");
    }

    TerminalReturn RunCfg(string args)
    {
        string[] argsSplit = args.Split(' ');
        if(argsSplit.Length != 2){
            return new TerminalReturn(false, $"Invalid command: {args}, incorrect num of arguments!");
        }

        // Make path from name
        string cfgName = argsSplit[1];
        string cfgPath = CfgPath(argsSplit[1]);
       
        // Is file real?
        if(!FileAccess.FileExists(cfgPath)){
            return new TerminalReturn(false, $"File '{cfgPath}' doesn't exist!");
        }
        
        // TODO: Replace with Godot's FileAccess
        // Load file & execute commands within
        string[] commands = File.ReadAllLines(cfgPath);
        for(int i = 0; i < commands.Length; i++)
        {
            // Return out if we hit an invalid command 
            if(!Execute(commands[i])){
                return new TerminalReturn(false, $"Bad command: {commands[i]}, cfg execution stopped.");
            }
        }
        //Print(ParseCfg(cfgPath));

        return new TerminalReturn(true, $"'{cfgName}.cfg' ran");
    }

    TerminalReturn Echo(string args)
    {
        Print(args.Remove(0,4));
        
        return new TerminalReturn(true, null);
    }

    TerminalReturn ShowLog(string args)
    {
        string output = "";
        foreach(var command in _commandLog)
        {
            output += command + '\n';
        }
        return new TerminalReturn(true, output);
    }

    TerminalReturn Help(string args)
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
            return new TerminalReturn(true, output);
        }
        
        TerminalCommand command;
        bool isValidCommand = _commands.TryGetValue(argSplit[1], out command);
        if(isValidCommand == false){
            return new TerminalReturn(false, "Invalid Command");
        }
        return new TerminalReturn(true, command.HelpText);
    }

    TerminalReturn Clear(string args)
    {
        _output.Clear();
        return new TerminalReturn(true, null);
    } 
}