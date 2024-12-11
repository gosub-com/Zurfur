using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;

using Avalonia;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;

using Gosub.Lex;
using Gosub.Avalonia.Controls;
using Zurfur;
using Zurfur.Compiler;
using Zurfur.Vm;
using AvaloniaEditor.Views;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Markup.Xaml.Templates;

namespace AvaloniaEditor;

/// <summary>
/// Manage a group of Zurfur text editors.   Customizes their look.
/// </summary>
class ZurfEditController
{
    static Pen s_BoldConnectorOutlineColor = new Pen(new Color(255, 192, 192, 255).ToUInt32());
    static Brush s_BoldConnectorBackColor = new SolidColorBrush(new Color(255, 224, 224, 255));
    static Pen s_ConnectorOutlineColor = new Pen(new Color(255, 192, 192, 255).ToUInt32());
    static Brush? s_ConnectorBackColor = null;
    static Brush s_LinkColorBrush = new SolidColorBrush(Colors.Blue);
    static WordSet s_BoldHighlightConnectors = new WordSet("( ) [ ] { } < >");

    HoverMessage _hoverMessageForm = new();

    // TBD: Port to Avalonia
    //ContextMenuStrip mContextMenuJson = new ContextMenuStrip()
    //    { AutoSize = false, Width = 100, ShowImageMargin = false }; // Autosizing didn't work, so hard code it here

    Token? _hoverToken;
    Editor? _activeEditor;
    DispatcherTimer _timer = new DispatcherTimer() { Interval = TimeSpan.FromMilliseconds(20) };
    bool _updateInfo;
    KeyModifiers _keyModifiers = KeyModifiers.None;

    public delegate void NavigateToSymbolDelegate(string path, int x, int y);

    public event NavigateToSymbolDelegate? OnNavigateToSymbol;

    public ZurfEditController()
    {
        _timer.Tick += _timer_Tick;
        //mContextMenuJson.Items.Add(
        //    new ToolStripLabel("Format Json", null, true, new EventHandler(FormatJson)));
    }

    /// <summary>
    /// Call this function once on each editor to direct events to this controller
    /// </summary>
    public void AttachEditor(Editor editor)
    {
        editor.LexerChanged += Editor_LexerChanged;
        editor.TextChanged += editor_TextChanged;
        editor.MouseHoverTokenChanged += editor_MouseTokenChanged;
        editor.PointerPressed += Editor_PointerPressed;
        editor.PointerMoved += Editor_PointerMoved;
        editor.PointerReleased += Editor_PointerReleased;
        editor.PointerExited += Editor_PointerExited;
        editor.KeyDown += Editor_KeyDown;
        editor.KeyUp += Editor_KeyUp;
        _timer.IsEnabled = true;
    }

    /// <summary>
    /// Set the active editor, or null if none is active
    /// </summary>
    public async void ActiveViewChanged(Editor editor)
    {
        _activeEditor = editor;
        if (editor != null)
        {
            UpdateScrollBars(editor);
            editor_MouseTokenChanged(editor, null, null);

            // Show Format Json context menu
            if (Path.GetExtension(editor.Lexer.Path).ToLower() == ".json"
                && editor.Lexer.LineCount == 1
                && editor.Lexer.GetLine(0).Length > 80)
            {
                // The control is invisible, so wait a short period for it to appear
                await System.Threading.Tasks.Task.Delay(50);
                //if (editor.IsVisible)
                //    mContextMenuJson.Show(editor, new Point(20, 30));
            }
        }
    }

    /// <summary>
    /// Call this once to set the control that should contain the hover message
    /// </summary>
    public void SetHoverMessageParent(Panel parent)
    {
        _hoverMessageForm.IsVisible = false;
        _hoverMessageForm.Width = 600;
        _hoverMessageForm.Height = 200;
        _hoverMessageForm.VerticalAlignment = VerticalAlignment.Top;
        _hoverMessageForm.HorizontalAlignment = HorizontalAlignment.Right;
        parent.Children.Add(_hoverMessageForm);
    }

    private void Editor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // When the user click the editor, hide the message box until a new token is hovered over.
        _hoverToken = null;
        _hoverMessageForm.IsVisible = false;

        if (e.KeyModifiers != _keyModifiers)
        {
            _keyModifiers = e.KeyModifiers;
            UpdateMouseHoverToken();
        }
    }
    private void Editor_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.KeyModifiers != _keyModifiers)
        {
            _keyModifiers = e.KeyModifiers;
            UpdateMouseHoverToken();
        }
    }

    private void Editor_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.KeyModifiers != _keyModifiers)
        {
            _keyModifiers = e.KeyModifiers;
            UpdateMouseHoverToken();
        }

        if (_activeEditor == null)
            return;

        var editor = _activeEditor;

        //  TBD: Convert json
        //if (e.Button == MouseButtons.Right
        //    && Path.GetExtension(editor.FilePath).ToLower() == ".json")
        //{
        //    mContextMenuJson.Show(editor, editor.PointToClient(Cursor.Position));
        //}


        var token = editor.MouseHoverToken;
        if (token != null
            && e.InitialPressMouseButton == MouseButton.Left
            && _keyModifiers.HasFlag(KeyModifiers.Control)
            && (token.Url != "" || token.GetInfo<Symbol>() != null))
        {
            if (token.Url.ToLower().StartsWith("http"))
            {
                TopLevel.GetTopLevel(_activeEditor)?.Launcher.LaunchUriAsync(new(token.Url));
            }
            else if (token.GetInfo<Symbol>() is Symbol sym)
            {
                if (OnNavigateToSymbol == null)
                    Debug.WriteLine("ShowEvent handler not installed");
                OnNavigateToSymbol?.Invoke(sym.Path, sym.Token.X, sym.Token.Y);
            }
            else
            {
                Debug.WriteLine("Event handler not installed", "Zurfur");
            }
        }

    }

    private void Editor_PointerExited(object? sender, PointerEventArgs e)
    {
        if (e.KeyModifiers != _keyModifiers)
        {
            _keyModifiers = e.KeyModifiers;
            UpdateMouseHoverToken();
        }
    }

    private void Editor_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != _keyModifiers)
        {
            _keyModifiers = e.KeyModifiers;
            UpdateMouseHoverToken();
        }
    }

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != _keyModifiers)
        {
            _keyModifiers = e.KeyModifiers;
            UpdateMouseHoverToken();
        }
    }

    void editor_TextChanged(object? sender, EventArgs e)
    {
        if (sender == _activeEditor)
            _updateInfo = true;
    }

    private void Editor_LexerChanged(object? sender, EventArgs e)
    {
        if (sender == _activeEditor)
            _updateInfo = true;
    }


    void _timer_Tick(object? sender, EventArgs e)
    {
        DisplayHoverForm();
        SetHoverFormLocation(false);
        if (_updateInfo)
        {
            _updateInfo = false;
            if (_activeEditor != null)
                UpdateScrollBars(_activeEditor);
        }
    }

    /// <summary>
    /// Setup to display the message for the hover token.
    /// Immediately show connected tokens.
    /// </summary>
    private void editor_MouseTokenChanged(object? sender, Token? prevToken, Token? newToken)
    {
        UpdateMouseHoverToken();
    }


    // Called whenever the mouse hover token changes or when a modifier keys changed
    void UpdateMouseHoverToken()
    {
        if (_activeEditor == null) 
            return;

        var editor = _activeEditor;
        var newToken = editor.MouseHoverToken;

        // Hide old form if hover token changed and setup to display the new token
        if (_hoverToken != newToken)
            _hoverMessageForm.IsVisible = false;
        _hoverToken = newToken;

        // Show meta when hovering over control character
        editor.ShowMetaTokens = newToken != null
            && newToken.Meta && (newToken == ";" || newToken == "{" || newToken == "}");


        // Show active link when CTRL is pressed
        var overrides = new List<TokenColorOverride>();
        if (_keyModifiers.HasFlag(KeyModifiers.Control)
            && newToken != null
            && (newToken.Url != "" || newToken.GetInfo<Symbol>() != null))
        {
            var ov = new TokenColorOverride(newToken);
            ov.ForeColor = s_LinkColorBrush;
            ov.Decorations = [new()]; // Underline
            overrides.Add(ov);
            editor.CursorOverride = new Cursor(StandardCursorType.Hand);
        }
        else
        {
            editor.CursorOverride = null;
        }

        // Update hover token colors
        editor.TokenColorOverrides = [];
        if (newToken == null)
            return;


        // Make a list of connecting tokens
        var connectors = newToken.GetInfo<Token[]>();
        if (connectors != null)
        {
            foreach (Token s in connectors)
            {
                if (s_BoldHighlightConnectors.Contains(s))
                    overrides.Add(new TokenColorOverride(s, s_BoldConnectorOutlineColor, s_BoldConnectorBackColor));
                else
                    overrides.Add(new TokenColorOverride(s, s_ConnectorOutlineColor, s_ConnectorBackColor));
            }
        }

        // Highlight all tokens on the screen with the same symbol info
        var hoverSymbols = newToken.GetInfos<Symbol>();
        foreach (var hoverSymbol in hoverSymbols)
        {
            var endLine = editor.TopVisibleLine + editor.LinesInWindow();
            foreach (var screenToken in editor.Lexer.GetEnumeratorStartAtLine(editor.TopVisibleLine))
            {
                if (screenToken.Y > endLine)
                    break;
                var screenSymbol = screenToken.GetInfo<Symbol>();
                if (screenSymbol == null || screenToken.Error)
                    continue;

                // Highlight symbols with the same name, and also
                // NOTE: Local symbols have the same name, so compare symbol objects, not FullName
                // specialized symbols with tokens matching location of definition
                if ((object)screenSymbol == (object)hoverSymbol
                        || hoverSymbol.HasToken && hoverSymbol.Token.Location == screenToken.Location)
                    overrides.Add(new TokenColorOverride(screenToken, s_BoldConnectorOutlineColor, s_BoldConnectorBackColor));
            }
        }

        // Highlight current location (if not already showing something from above)
        if (newToken.Type != TokenType.Comment
            || newToken.Subtype == TokenSubType.CodeInComment
            || newToken.Underline)
        {
            overrides.Add(new TokenColorOverride(newToken,
                newToken.Error ? new Pen(Colors.Red.ToUInt32()) : new Pen(Colors.LightBlue.ToUInt32())));
        }

        // Update editor to show them
        editor.TokenColorOverrides = overrides.ToArray();

    }

    private void FormatJson(object sender, EventArgs e)
    {
        try
        {
            var editor = _activeEditor;
            if (editor == null)
                return;

            var s = string.Join("", editor.Lexer.GetText(new TokenLoc(0, 0), new TokenLoc(1000000, 1000000)));
            var json = JsonSerializer.Deserialize<JsonDocument>(s);
            s = JsonSerializer.Serialize(json, new JsonSerializerOptions() { WriteIndented = true });
            editor.ReplaceText(s.Split('\n'), new TokenLoc(0, 0), new TokenLoc(1000000, 1000000));
        }
        catch (Exception ex)
        {
            // MessageBox.Show($"ERROR: {ex.Message}");
        }
    }



    /// <summary>
    /// Called from timer periodically to display the hover form
    /// </summary>
    void DisplayHoverForm()
    {
        var showForm = _activeEditor != null
                && _hoverToken != null
                && _hoverToken.Type != TokenType.Comment
                && (_hoverToken.GetInfo<string>() != null
                        || _hoverToken.GetInfo<ParseInfo>() != null
                        || _hoverToken.GetInfo<Symbol>() != null
                        || _hoverToken.GetInfo<TokenError>() != null
                        || _hoverToken.GetInfo<TokenWarn>() != null)
                && !_hoverMessageForm.IsVisible;
        if (!showForm)
            return;

        if (_hoverToken == null)
            return;

        // Show errors and warnings
        var message = "";
        foreach (var error in _hoverToken.GetInfos<TokenError>())
        {
            var errorType = "";
            if (error is ParseError)
                errorType = " (syntax)";
            else if (error is ZilCompileError)
                errorType = " (compile)";
            else if (error is VerifyError)
                errorType = " (verify)";
            message += $"ERROR{errorType}: {error}\r\n";
        }

        foreach (var error in _hoverToken.GetInfos<TokenWarn>())
            message += $"WARNING: {error}\r\n";

        // Show symbol info
        if (message != "")
            message += "\r\n";
        message += GetSymbolInfo();

        // Show parse info and strings
        foreach (var s in _hoverToken.GetInfos<ParseInfo>())
            message += s + "\r\n\r\n";
        foreach (var s in _hoverToken.GetInfos<string>())
            message += s + "\r\n\r\n";

        _hoverMessageForm.Message = message.Trim();

        // Show form with proper size and location
        //var size = mHoverMessageForm.Message.Size;
        //mHoverMessageForm.ClientSize = new Size(size.Width + 8, size.Height + 8);
        SetHoverFormLocation(true);
        //mHoverMessageForm.Show(mActiveEditor.ParentForm);
        _hoverMessageForm.IsVisible = true; // TBD: Port
    }

    private void SetHoverFormLocation(bool setX)
    {
        /*
        if (mActiveEditor == null || mHoverToken == null) 
            return;

        if (setX)
            mHoverMessageForm.Left = Form.MousePosition.X;

        var top = mActiveEditor.PointToScreen(mActiveEditor.LocationToken(mHoverToken.Location)).Y;
        var fontSize = mActiveEditor.FontSize;
        if (Form.MousePosition.Y > top + fontSize.Height / 2)
            top += 2*fontSize.Height;
        else
            top -= mHoverMessageForm.Height + fontSize.Height;
        mHoverMessageForm.Top = top;
        */
    }

    private string GetSymbolInfo()
    {
        if (_hoverToken == null)
            return "";
        var message = "";
        var symbols = _hoverToken.GetInfos<Symbol>();
        if (symbols.Length == 0)
            return "";

        // When a token has multiple symbols or an error, display a summary.
        if (symbols.Length > 1 || symbols.Length == 1 && _hoverToken.Error)
        {
            message += "POSSIBLE SYMBOLS:\r\n";
            message += string.Join("\r\n", symbols.Select(sym =>
                $"    [{getQualifiers(sym)}] {sym.FullName}"));
            return message + "\r\n\r\n";
        }

        // Friendly names
        var symbol = symbols[0];
        message += $"[{getQualifiers(symbol)}] {symbol.FriendlyName()}\r\n";
        if (symbol.Type != null && !symbol.IsFun && !symbol.IsLambda)
            message += $"Type: {symbol.Type.FriendlyName()}\r\n";
        message += "\r\n";

        // Raw symbol info
        message += $"Full Name: {symbol.FullName}\r\n\r\n";
        if (symbol.IsSpecialized)
            message += $"Non-specialized: {symbol.Concrete.FullName}\r\n\r\n"; 
        if (symbol.Type != null && !symbol.IsFun && !symbol.IsLambda)
            message += $"Type Name: {symbol.Type.FullName}\r\n\r\n";

        // Comments
        if ((symbol.Concrete.Comments ?? "").Trim() != "")
            message += $"// {symbol.Concrete.Comments}\r\n\r\n";

        if (symbol.IsFun)
        {
            message += "PARAMS: \r\n";
            foreach (var child in symbol.Concrete.Children)
            {
                if (child.IsFunParam)
                    message += $"    {child.SimpleName}: [{getQualifiers(child)}] {child.TypeName}\r\n";
                else if (child.IsTypeParam)
                    message += $"    {child.SimpleName}: Type parameter\r\n";
                else
                    message += $"    {child.SimpleName}: COMPILER ERROR\r\n";
            }
            message += "\r\n";
        }


        return message;
    }

    string getQualifiers(Symbol symbol)
        => string.Join(", ", symbol.QualifiersStr().Split(' '));

    void UpdateScrollBars(Editor editor)
    {
        // Warnings on text
        var marks = new List<VerticalMarkInfo>();
        int lastMark = -1;
        foreach (var token in editor.Lexer)
        {
            // WARNINGS
            if (token.Warn && token.Location.Y != lastMark)
            {
                lastMark = token.Location.Y;
                marks.Add(new VerticalMarkInfo { Color = Colors.Gold, Length = 1, Start = lastMark });
            }
        }
        // Errors on text
        lastMark = -1;
        foreach (var token in editor.Lexer)
        {
            // ERRORS
            if (token.Error && token.Location.Y != lastMark)
            {
                lastMark = token.Location.Y;
                marks.Add(new VerticalMarkInfo { Color = Colors.Red, Length = 1, Start = lastMark });
            }
        }
        // Errors on meta tokens
        foreach (var token in editor.Lexer.MetaTokens)
        {
            // ERRORS
            if (token.Error && token.Location.Y != lastMark)
            {
                lastMark = token.Location.Y;
                marks.Add(new VerticalMarkInfo { Color = Colors.Red, Length = 1, Start = lastMark });
            }
        }
        editor.SetMarks(marks.ToArray());
    }
}
