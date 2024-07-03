using Godot;
using System;
using System.Linq;
using KrgTerminal;


public partial class SuggestionBox : Control
{
	Terminal _terminal;
	InputState _inputState;

	const int SuggestionCount = 6;
    Button[] _suggs = new Button[SuggestionCount];
    int _logBorderWidth = 2; // Width of left border when dislaying _commandLog as suggestions
    Node _suggParent; 
	public new bool HasFocus => _suggs.Any(sug => sug.HasFocus());

	bool _histMode = false;


	public override void _Ready()
	{
		_terminal = GetOwnerOrNull<Terminal>();
		if(_terminal == null){
			GD.PrintErr(this.Name + ": Owner not Terminal");
			return;
		}

        for(int i = 0; i < SuggestionCount; i++){
            _suggs[i] = GetNode<Button>($"sug_{i}");
            _suggs[i].Connect("pressed", new Callable(this, "SuggestionClicked"));
            _suggs[i].Text = "NULL";
        }
	}

	public void SelectFirst()
	{
		_suggs[0].GrabFocus();
	}

	public void Clear()
	{
		// Reset box position
		OffsetLeft = 0;
		
		foreach(var sugg in _suggs)
		{
			sugg.Text = "";  
            sugg.Visible = false;  
            ((StyleBoxFlat)sugg.GetThemeStylebox("normal")).BorderWidthLeft = 0;
            ((StyleBoxFlat)sugg.GetThemeStylebox("focus")).BorderWidthLeft = 0;
            ((StyleBoxFlat)sugg.GetThemeStylebox("hover")).BorderWidthLeft = 0;
		}
	}

	void SuggestionClicked()
	{
		// Grab text from focused suggestion button
        string suggestion = _suggs.Single(sugg => sugg.HasFocus()).Text;

		
        // Insert arg to existing command 
		if(_inputState.Command.HasValue && !_histMode){
			_terminal.Input.DeleteText(_inputState.LastWordStartIndex(), _terminal.Input.Text.Length);
			_terminal.Input.InsertTextAtCaret(suggestion);
		}
		else{
			_terminal.Input.Text = suggestion;
		}	

        // Move caret to end of input & select input line edit
        _terminal.Input.CaretColumn = _terminal.Input.Text.Length; 
        _terminal.Input.GrabFocus();

        Clear();
	}

	public bool ShowHistory()
	{
		_histMode = true;

		Clear();
        if(_terminal.CommandLog.Count == 0){
            return false;
        }

        // Reverse to show most recent at top
        var histReverse = _terminal.CommandLog.ToArray();
        Array.Reverse(histReverse);

        for(int i = 0; i < _terminal.CommandLog.Count && i < SuggestionCount; i++)
        {   
            // Give suggestion buttons a mark on the left side for color coding
            ((StyleBoxFlat)_suggs[i].GetThemeStylebox("normal")).BorderWidthLeft = _logBorderWidth;
            ((StyleBoxFlat)_suggs[i].GetThemeStylebox("focus")).BorderWidthLeft  = _logBorderWidth;
            ((StyleBoxFlat)_suggs[i].GetThemeStylebox("hover")).BorderWidthLeft  = _logBorderWidth;
            
            _suggs[i].Text = histReverse[i];
            _suggs[i].Visible = true;
        }
		// Select first 
		_suggs[0].GrabFocus();
		return true;
	}

	void ShowMatches()
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
                if(b.Length > a.Length){
                    return 0.0f;
                }
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
            OffsetLeft = 13 * _inputState.LastWordStartIndex();

            //SuggestionClear();
            for(int i = 0; i < text.Length && i < SuggestionCount; i++){
                _suggs[i].Text = text[i];
                _suggs[i].Visible = true;
            }
        }

		_histMode = false;
        Clear();

        // Nothing to suggest if no input
        if(!_inputState.HasInput){
            return;
        }

        string compare        = null;
        string[] validStrings = null;
        string[] matches      = null;
        
        // Autocomplete for commands 
        if(!_inputState.Command.HasValue){
            compare = _inputState.Words[0];            // Find command at first index
            validStrings = _terminal.Commands.Keys.ToArray();  // Get string[] of all potential commands
            matches = Matches(compare, validStrings); // Find all that match
            SetSuggestions(matches);
            return;
        }

        // Autocomplete for args _______________________________ 
        TerminalCommand command = _inputState.Command.Value;
        
        // Command has args?
        if(command.ArgCount == 0){
            return;
        }
        // Command has AutoComplete data?
        if(command.ArgAutocomplete == null){
            return;
        }   

        int argIndex = _inputState.ArgIndex();

        // Are more args in input than the command takes?
        if(argIndex + 1 > command.ArgCount){
            return;
        }
        // Does arg have autocomplete data?
        if(command.ArgAutocomplete[argIndex] == null){
            return;
        } 
        // Show all valid args if no arg input yet
        if(_inputState.EndSpaceCount() == 1){
            validStrings = command.ArgAutocomplete[argIndex];
            SetSuggestions(validStrings);
            return;
        }

        // Get arg input
        compare = _inputState.Words.Last();
        validStrings = command.ArgAutocomplete[argIndex];
        matches = Matches(compare, validStrings);
        SetSuggestions(matches);
	}

	public void Update(InputState state)
	{
		_inputState = state;
		ShowMatches();
	}
}
