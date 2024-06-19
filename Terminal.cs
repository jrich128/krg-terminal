using Godot;
using System;
using System.IO;
using System.Collections.Generic;
using FileAccess = Godot.FileAccess;
using System.Linq;

public static class Extension
{
    /// <summary>
    /// Sequential similarity from 0.0f to 1.0f
    /// </summary>
    public static float Similarity(this string a, string b)
    {
        float sim = 0;
        for(int i = 0; i < Mathf.Min(a.Length, b.Length); i++)
        {   
            if(a[i] != b[i]){
                break;
            }
            sim += 1.0f;
        }

        return (sim / (a.Length+b.Length)) * 2.0f;
    }
}

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

public struct TerminalVar
{
    GodotObject _obj;
    Variant.Type _type; 
    string _name;

    public bool Set(Variant value)
    {
        if(value.VariantType != _type){
            return false;
        }
        _obj.Set(_name, value);
        return true;
    }
    public Variant? Value()
    {
        Variant value = _obj.Get(_name);
        return value;
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
    public TerminalFunction Function;
    public string HelpText = ""; 
    
    public TerminalCommand(string key, int argCount, TerminalFunction function, string helpText)
    {
        Key = key; ArgCount = argCount; Function = function; HelpText = helpText;
    }
}

/// <summary>
/// BBCode text style effects 
/// </summary>
[Flags] public enum TStyleFlag
{
    None      = 0,
    Italic    = 1 << 0,
    Bold      = 1 << 1,
    Underline = 1 << 2,
    Error     = Italic | Bold | Underline
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
    Blue
}

/* TODO:
    - alias command alias.cfg
    - Clear binds command
*/

public partial class Terminal : Control
{
    [Signal] public delegate void OpenedEventHandler(bool state);

    const string CFG_DIR      = "cfg";
    const string BINDS_PATH   = $"{CFG_DIR}/binds.cfg";
    const string AUTORUN_PATH = $"{CFG_DIR}/autorun.cfg";
    static string CfgPath(string name) => $"{CFG_DIR}/{name}.cfg";

    Dictionary<string, TerminalVar>     _vars     = new Dictionary<string, TerminalVar>();
    Dictionary<string, TerminalCommand> _commands = new Dictionary<string, TerminalCommand>();
    static Dictionary<TColor, Color>    _color    = new Dictionary<TColor, Color>()
    {
        // This mess allows PrintF to have a default for color.
        // Using Godot's built-in colors is too much of pain
        {TColor.White, Colors.White},
        {TColor.Black, new Color(0.1f, 0.1f, 0.1f)  },
        {TColor.Red  , new Color(0.5f, 0.0f, 0.0f)  },
        {TColor.Green, new Color(0.47f, 0.6f, 0.13f)},
        {TColor.Blue,  new Color(0.0f, 0.0f, 0.51f) }
    };

    List<Bind> _binds = new List<Bind>();

    // Log of all input submitted
    List<string> _commandLog = new List<string>();
    int _logCursor = 0;

    const int SuggestionCount = 6;
    Button[] _suggs = new Button[SuggestionCount];
    int _logBorderWidth = 2; // Width of left border when dislaying _commandLog as suggestions
    Control _suggParent; 
    
    RichTextLabel _output;
    LineEdit      _input;
    bool HaveInput      => _input.Text != "";
    bool SuggsHaveFocus => _suggs.Any(sug => sug.HasFocus());

    AnimationPlayer _animPlayer;
    Viewport _viewport;


    public override void _Ready()
    {
        // Create cfg directory if none
        if(!Godot.DirAccess.DirExistsAbsolute(CFG_DIR)){
            Directory.CreateDirectory(CFG_DIR);
        }
        // Create AutoRun.cfg if none 
        if(!Godot.FileAccess.FileExists(AUTORUN_PATH)){
            using(var file = Godot.FileAccess.Open(AUTORUN_PATH, Godot.FileAccess.ModeFlags.Write)){
            }
        }

        _output = GetNode<RichTextLabel>("margin/vbox/output");
        _input  = GetNode<LineEdit>("margin/vbox/input");
        _input.Connect("text_submitted", new Callable(this, "InputSubmit"));
        _input.Connect("focus_entered" , new Callable(this, "OnInputFocusEntered"));
        _input.Connect("text_changed"  , new Callable(this, "OnInputChanged"));

        _animPlayer = GetNode<AnimationPlayer>("animation_player");

        _suggParent = GetNodeOrNull<Control>("margin/vbox/input/sug_buttons");
        if(_suggParent == null){
            GD.PrintErr($"Terminal: Could not get suggParent");
            return;
        }

        for(int i = 0; i < SuggestionCount; i++)
        {
            _suggs[i] = GetNode<Button>($"margin/vbox/input/sug_buttons/sug_{i}");
            _suggs[i].Connect("pressed", new Callable(this, "SuggestionClicked"));
            _suggs[i].Text = "NULL";
        }

        _viewport = GetViewport(); 
        if(_viewport == null){
            GD.PrintErr($"Terminal: Could not get viewport.");
            return;
        }

        AddCommand("help" , 
        new TerminalCommand("help", 0, Help, "'help <command>' Display info on command"));

        AddCommand("clear", 
        new TerminalCommand("clear", 0, Clear, "'clear' Clear output log"));

        AddCommand("run"  , 
        new TerminalCommand("run", 1, RunCfg, "'run <filename>' Reads a text file from 'cfg/' directory with commands in it & executes them"));

        AddCommand("bind" , 
        new TerminalCommand("bind", 2, CreateBind, "'bind <key> <command>' Binds a key to execute a command. Currently only supports alphanumeric keys"));
        
        AddCommand("echo" , 
        new TerminalCommand("echo", 1, Echo, "'echo <text>' Prints text to the output log"));

        AddCommand("quit" , 
        new TerminalCommand("quit", 0, Quit, "'quit' Quits game"));

        AddCommand("log" , 
        new TerminalCommand("log", 0, ShowLog, "'log' Prints command history to output log"));

        AddVar("cling", new TerminalVar(){});

        Execute("run autorun");

        LoadBinds();

        base._Ready();
    }  

    public override void _Input(InputEvent @event)
    {
        // Execute bound commands on Action pressed
        for(int i = 0; i < _binds.Count; i++)
        {
            Bind bind = _binds[i];

            if(@event.IsActionPressed(bind.ActionName)){
                Execute(bind.Command);
                GD.Print($"Bind: {bind.Command}, Action: {bind.ActionName}");
                _viewport.SetInputAsHandled();
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

    public override void _Notification(int what)
    {
        // Is game closing?
        if(what == NotificationWMCloseRequest){
            SaveBinds();
        }
    }

    void SaveBinds()
    {
        using(var file = Godot.FileAccess.Open(BINDS_PATH, FileAccess.ModeFlags.ReadWrite))
        {
            string[] lines = file.GetAsText(true).Split('\n');

            for(int i = 0; i < _binds.Count; i++){
                if(lines.Contains(_binds[i].BindCommand)){
                    continue;
                }
                file.StoreLine(_binds[i].BindCommand);
            }
        }

        GD.Print("Binds saved.");
    }

    void LoadBinds()
    {
        using(var file = Godot.FileAccess.Open(BINDS_PATH, FileAccess.ModeFlags.Read))
        {
            string[] lines = file.GetAsText(true).Split('\n');

            for(int i = 0; i < lines.Length; i++){
                CreateBind(lines[_binds.Count]);
            }
        }
    }

    public void AddVar(string key, TerminalVar var)
    {
        _vars.Add(key, var);
    }

    public void AddCommand(string key, TerminalCommand command)
    {
        _commands.Add(key, command);
    }

    bool Execute(string input)
    {
        // Convert to lower case only 
        string inputLowercase = input.ToLower();
        // Convert into string array
        string[] splitInput = inputLowercase.Split(' ');

        // Check if first string in command is valid command, execute it if so 
        TerminalCommand command;
        bool isValidCommand = _commands.TryGetValue(splitInput[0], out command);
        if(isValidCommand == false){
            return false;
        }

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

    void InputSubmit(string text)
    {
        Print("==================================", true, TColor.Black, TStyleFlag.Underline | TStyleFlag.Bold);

        _commandLog.Add(text);
        //GD.Print(_commandHistory.Count);
        _logCursor = _commandLog.Count - 1; // reset history cursor 

        // Log command in Ouput 
        Print(">" + text, true, TColor.Green, TStyleFlag.Bold);

        // Delete from Input box
        _input.Text = "";

        // See if Input text is a valid command
        if(Execute(text) == false){
            Print("Unknown command", true, TColor.Red, TStyleFlag.Bold | TStyleFlag.Italic | TStyleFlag.Underline);
        }

        SuggestionClear();
    }

    void SuggestionClicked()
    {
        foreach(var suggestion in _suggs){
            if(suggestion.HasFocus()){
                _input.Text = suggestion.Text;           // Put suggestion into input
                _input.CaretColumn = _input.Text.Length; // Move caret to end of input
                _input.GrabFocus();                      // Select input line edit                               
                SuggestionClear();
                return;
            }
        }
    }

    void SuggestionShowMatches()
    {
        string[] Matches(string input, string[] strings)
        {   
            float[] simValues  = new float[strings.Length];
            int matches = 0;
            for(int i = 0; i < strings.Length; i++)
            {
                simValues[i] = input.Similarity(strings[i]);
                if(simValues[i] > 0.0f){
                    matches++;
                    //GD.Print($"{input}, {strings[i]}, {simValues[i]}");
                }
            }

            Array.Sort(simValues, strings);
            Array.Reverse(strings);
            string[] matchBuff = new string[matches];
            for(int i = 0; i < matches; i++){
                //GD.Print(strings[i]);
                matchBuff[i] = strings[i];
            }

            return matchBuff;
        }

        string[] commandMatches = Matches(_input.Text, _commands.Keys.ToArray());
        //string[] varMatches     = PossibleMatches(_input.Text, _vars.Keys.ToArray());
        string[] finalSet = commandMatches;//.Concat(varMatches).ToArray();

        // Show matches in suggestion buttons under input line edit
        for(int i = 0; i < commandMatches.Length && i < SuggestionCount; i++)
        {
            _suggs[i].Text = finalSet[i];
            _suggs[i].Visible = true;
        }
    }

    bool SuggestionShowHist()
    {
        SuggestionClear();
        if(_commandLog.Count == 0){
            return false;
        }

        var histReverse = _commandLog.ToArray();
        Array.Reverse(histReverse);

        for(int i = 0; i < _commandLog.Count && i < SuggestionCount; i++)
        {   
            ((StyleBoxFlat)_suggs[i].GetThemeStylebox("normal")).BorderWidthLeft = _logBorderWidth;
            ((StyleBoxFlat)_suggs[i].GetThemeStylebox("focus")).BorderWidthLeft  = _logBorderWidth;
            ((StyleBoxFlat)_suggs[i].GetThemeStylebox("hover")).BorderWidthLeft  = _logBorderWidth;

            _suggs[i].Text = histReverse[i];
            _suggs[i].Visible = true;
        }

        return true;
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

    public void OnInputChanged(string newText)
    {
        if(HaveInput){
            SuggestionShowMatches();
        }
        else{
            SuggestionClear();
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

    /* All bellow are our built-in Terminal Commands */
    
    TerminalReturn ClearBinds(string args)
    {
        throw new NotImplementedException();
    }

    TerminalReturn Alias(string args)
    {
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
        
        string key       = argsSplit[1];
        string command   = argsSplit[2];
        //string serialize 
 
        // Check command is valid
        if(!_commands.TryGetValue(command, out _)){
            return new TerminalReturn(false, $"'{command}' Unknown command.");
        }

        Bind bind = new Bind();
        bind.BindCommand = args;
        bind.ActionName = $"bind_{_binds.Count}";
        InputMap.AddAction(bind.ActionName);

        // TODO: Support for non-alphabet keys 
        InputEventKey bindInputEvent = new InputEventKey(); 
        bindInputEvent.Keycode = (Godot.Key)((argsSplit[1][0]) - 32); // Subtract 32 to go from lower case to uppercase ASCII, or is this Unicode? Fuck if I know
        //GD.Print(bindInputEvent.AsTextKeycode());
        InputMap.ActionAddEvent(bind.ActionName, bindInputEvent);
        
        // TODO:InputMap.EraseAction
        //GD.Print(command);
        bind.Command = command;
        
        _binds.Add(bind);
        //SaveBinds();

        return new TerminalReturn(true, $"'{command}' bound to '{argsSplit[1][0]}'");
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