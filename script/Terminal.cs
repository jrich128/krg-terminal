#define DBPRINT
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

using FileAccess = Godot.FileAccess; // We want to use Godot's not C#'s
using KrgTerminal;


/* 
    TODO:
        - Fix keycode.txt names that are supposed to have underscores
        - unbind & clearbinds commands
*/

namespace KrgTerminal
{
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
    /// BBCode text style effects; Not all implemented yet!
    /// </summary>
    [Flags] public enum StyleFlag
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
    public enum Color
    {
        Default,
        White  ,
        Black  ,
        Red    , 
        Green  ,
        Blue   ,

        Error = Red
    }
}


[GlobalClass]
public partial class Terminal : Control
{
    [Signal] public delegate void OpenedEventHandler(bool state);

    const string CFG_DIR      = "cfg";
    const string BINDS_PATH   = $"{CFG_DIR}/binds.cfg";
    const string AUTORUN_PATH = $"{CFG_DIR}/autorun.cfg";
    static string CfgPath(string name) => $"{CFG_DIR}/{name}.cfg";
    
    public Dictionary<string, TerminalCommand> Commands = new Dictionary<string, TerminalCommand>();
    Dictionary<string, TVar> _tvars = new Dictionary<string, TVar>();


    static Dictionary<KrgTerminal.Color, Godot.Color> _color = new Dictionary<KrgTerminal.Color, Godot.Color>()
    {
        // This mess allows Print to have a default for color.
        // Using Godot's built-in colors is too much of pain
        { KrgTerminal.Color.White, Colors.White},
        { KrgTerminal.Color.Black, new Godot.Color(0.1f , 0.1f, 0.1f )},
        { KrgTerminal.Color.Red  , new Godot.Color(0.65f, 0.15f,0.15f)},
        { KrgTerminal.Color.Green, new Godot.Color(0.57f, 0.7f, 0.23f)},
        { KrgTerminal.Color.Blue,  new Godot.Color(0.0f , 0.0f, 0.51f)}
    };

    List<Bind> _binds = new List<Bind>();

    // Log of all input submitted
    public List<string> CommandLog = new List<string>();
    int _logCursor = 0;

    RichTextLabel _output;
    public LineEdit Input;

    AnimationPlayer _animPlayer;
    Viewport _viewport;

    InputState inputState;

    SuggestionBox _suggBox;


    public int tester = 1;
    
    
    void CreateTVars()
    {
        Node[] GetTvarNodes()
        {
            List<Node> nodes = new List<Node>();

            void FindTVarNodes(Node node)
            {
                GD.Print(node.Name + " " + node.GetType());
                // Is object marked with [Tvar]?
                var attribs = Attribute.GetCustomAttributes(node.GetType());
                bool hasTvarAttrib = attribs.Any(attrib => attrib.GetType() == typeof(TVarAttribute));
                if(hasTvarAttrib){
                    nodes.Add(node);
                }

                var children = node.GetChildren();
                foreach(var child in children)
                {
                    FindTVarNodes(child);
                }
            }

            FindTVarNodes(GetNode("/root"));

            return nodes.ToArray();
        }
      
		void MakeTVars()
        {
            var nodes = GetTvarNodes();
            foreach(var node in nodes)
            {
                Type type = node.GetType();

                var fields = type.GetRuntimeFields();
		        fields = fields.Where(field => field.CustomAttributes.Any(attrib => attrib.AttributeType == typeof(TVarAttribute)));

                foreach(var field in fields)
                {   
                    string tVarKey =$"{node.Name}.{field.Name}"; 
                    _tvars.Add(tVarKey, 
                    new TVar()
                    {   
                        Obj = node, VarType = Variant.Type.Int,// FUCK 
                        Member = field
                    });

                    GD.Print($"Tvar: {tVarKey} added with value of {_tvars[tVarKey].Get().Value}");
                }
                
                var properties = type.GetRuntimeProperties();
		        properties = properties.Where(prop => prop.CustomAttributes.Any(attrib => attrib.AttributeType == typeof(TVarAttribute)));

            }
        }

		
        MakeTVars();
    }   

    void Init()
    {
        CreateTVars();

        // Create cfg directory if none
        if(!DirAccess.DirExistsAbsolute(CFG_DIR)){
            Directory.CreateDirectory(CFG_DIR);
        }
        // Create AutoRun.cfg if none 
        if(!FileAccess.FileExists(AUTORUN_PATH)){
            using(var file = Godot.FileAccess.Open(AUTORUN_PATH, FileAccess.ModeFlags.Write)){
            }
        }

        _output = GetNode<RichTextLabel>("margin/vbox/output");
        Input  = GetNode<LineEdit>("margin/vbox/input");
        Input.Connect("text_submitted", new Callable(this, "SubmitInput"));
        Input.Connect("focus_entered" , new Callable(this, "OnInputFocusEntered"));
        Input.Connect("text_changed"  , new Callable(this, "OnInputChanged"));

        _animPlayer = GetNode<AnimationPlayer>("animation_player");
        _suggBox = GetNode<SuggestionBox>("margin/vbox/input/sug_buttons");

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
        new TerminalCommand("echo", 1, Echo, "'echo <text>' Prints text to the output log"));

        AddCommand( 
        new TerminalCommand("quit", 0, Quit, "'quit' Quits game"));

        AddCommand( 
        new TerminalCommand("log", 0, ShowLog, "'log' Prints command history to output log"));


        TerminalCommand tComm = new TerminalCommand()
        {
            Key = "test",
            ArgCount = 1,
            ArgAutocomplete = new string[][]
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
            ArgAutocomplete = new string[][]
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
            ArgAutocomplete = new string[][]
            {
                new string[]{"bass", "bacsy", "bass0", "ass1", "ass2"}, 
                GetFileAsLines("res://addons/krg-terminal/keycodes.txt"),
                new string[]{"bguuuy", "bfuj", "ass2"}
            },
            Function = ShowLog,
            HelpText = "test"
        };

        AddCommand( 
        new TerminalCommand()
        {
            Key      = "bind",
            ArgCount = 2,
            ArgAutocomplete = new string[][]
            {
                GetFileAsLines("res://addons/krg-terminal/keycodes.txt"),
                Commands.Keys.ToArray()
            },
            Function = CreateBind,
            HelpText = "'bind <key> <command>' Binds a key to execute a command"
        });

        _tvars.Add("ass", 
        new TVar()
        {
            Obj = this, VarType = Variant.Type.Int,
            Member = typeof(Terminal).GetField("tester")
        });

        _tvars.Add("grass", 
        new TVar()
        {
            Obj = this, VarType = Variant.Type.String, 
            Member = typeof(Terminal).GetField("testerStr")
        });

        AddCommand(
            new TerminalCommand()
            {
                Key = "set",
                ArgCount = 2,
                Function = SetTVar,
                HelpText = "NULL",
                ArgAutocomplete = new string[][]
                {
                    _tvars.Keys.ToArray(),
                    null
                }
            }
        );

        AddCommand(
            new TerminalCommand()
            {
                Key = "get",
                ArgCount = 1,
                Function = GetTVar,
                HelpText = "NULL",
                ArgAutocomplete = new string[][]
                {
                    _tvars.Keys.ToArray()
                }
            }
        );

        AddCommand(tComm1);

        Execute("run autorun");

        LoadBinds();

        //GetFileAsLines("s");
    }

    public override void _Ready()
    {   
        GetNode("/root").Connect(SignalName.Ready, new Callable(this, "Init")); 

        var f = ((InputEventKey)InputMap.ActionGetEvents("forward")[0]).PhysicalKeycode;
        GD.Print(f);

        base._Ready();
    }  

    public string testerStr = "fs";

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
            Input.Text = "";
            _viewport.SetInputAsHandled(); 
            return;
        }
        // Show command history in suggestions
        if(@event.IsActionPressed("ui_up"))
        {
            if(_suggBox.HasFocus){
                return;
            }
            if(_suggBox.ShowHistory()){
                _viewport.SetInputAsHandled(); 
                return;
            }
        }
        // Give focus to suggestions
        if(@event.IsActionPressed("ui_down"))
        { 
            if(_suggBox.HasFocus){
                return;
            }
            _suggBox.SelectFirst();
            _viewport.SetInputAsHandled();
            return;
        }
        // Tab to bring focus back to input text edit
        if(@event.IsActionPressed("ui_focus_next"))
        {
            Input.GrabFocus();
            _viewport.SetInputAsHandled();
            return;
        }
        // Esc to clear suggestions & give focus back to input
        if(@event.IsActionPressed("ui_cancel"))
        {
            Input.GrabFocus();
            _suggBox?.Clear();
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
        bool isValidCommand = Commands.TryGetValue(commandName, out command);
        if(isValidCommand == false){
            return false;
        }

        // Check for correct num of args
        if(command.ArgCount != 0){
            if(splitInput.Length < 1 + command.ArgCount){
                Print($"{command.Key} Incorect num of args! \nHelp:{command.HelpText}", true, KrgTerminal.Color.Red, StyleFlag.Error);
                return false;
            }
        }

        // Execute function
        CommandReturn funcReturn = command.Function(input);
        if(funcReturn.Details != null){
            Print(
                funcReturn.Details, true, 
                funcReturn.Success? KrgTerminal.Color.Default : KrgTerminal.Color.Red,
                funcReturn.Success? StyleFlag.None : StyleFlag.Error);
        }

        return true;
    }

    public void SubmitInput(string text)
    {
        Print("===============================================", true, KrgTerminal.Color.Black, StyleFlag.Underline | StyleFlag.Bold);

        CommandLog.Add(text);
        _logCursor = CommandLog.Count - 1; // reset history cursor 

        // Log input in output 
        Print(">" + text, true, KrgTerminal.Color.Green);

        // Delete input from line edit if it came from there
        if(Input.Text == text){
            Input.Text = "";
        }

        // See if Input text is a valid command
        if(Execute(text) == false)
        {
            Print("Unknown command", true, KrgTerminal.Color.Error, StyleFlag.Error);
        }

        _suggBox.Clear();
    }

    public void AddCommand(TerminalCommand command)
    {
        Commands.Add(command.Key, command);
    }

    public void Print(string text, bool newLine = true, KrgTerminal.Color color = KrgTerminal.Color.Default, StyleFlag flags = StyleFlag.None)
    {
        if(text == null || text.Length < 1){
            return;
        }

        // BBCode formatting 
        if(color != KrgTerminal.Color.Default) _output.PushColor(_color[color]);
        if(flags.HasFlag(StyleFlag.Italic))    _output.PushItalics();
        if(flags.HasFlag(StyleFlag.Bold))      _output.PushBold();
        if(flags.HasFlag(StyleFlag.Underline)) _output.PushUnderline();

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
        bool validCommand = Commands.TryGetValue(commandName, out result);
        if(validCommand == false){
            return null;
        }

        return result;
    }

    public void OnInputChanged(string newText)
    {
        inputState = new InputState()
        {   
            HasInput = Input.Text != "",
            Split = newText.Split(' '), 
            Words = newText.Split(' ').Where(word => word != "").ToArray(),
            Command = StringToCommand(newText)
        };     

        _suggBox?.Update(inputState);
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
        if(!FileAccess.FileExists(BINDS_PATH)){
            return;
        }

        using(var file = Godot.FileAccess.Open(BINDS_PATH, FileAccess.ModeFlags.Read))
        {
            string[] lines = file.GetAsText(true).Split('\n');

            for(int i = 0; i < lines.Length; i++)
            {
                if(lines[_binds.Count] == ""){
                    continue;
                } 

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
        Input.GrabFocus();
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
}