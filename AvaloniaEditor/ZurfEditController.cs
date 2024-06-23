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
    static Pen sBoldConnectorOutlineColor = new Pen(new Color(255, 192, 192, 255).ToUInt32());
    static Brush sBoldConnectorBackColor = new SolidColorBrush(new Color(255, 224, 224, 255));
    static Pen sConnectorOutlineColor = new Pen(new Color(255, 192, 192, 255).ToUInt32());
    static Brush? sConnectorBackColor = null;
    static WordSet sBoldHighlightConnectors = new WordSet("( ) [ ] { } < >");

    HoverMessage mHoverMessageForm = new();
    //ContextMenuStrip mContextMenuJson = new ContextMenuStrip()
    //    { AutoSize = false, Width = 100, ShowImageMargin = false }; // Autosizing didn't work, so hard code it here

    Token? mHoverToken;
    Editor? mActiveEditor;
    DispatcherTimer _timer = new DispatcherTimer() { Interval = TimeSpan.FromMilliseconds(20) };
    bool mUpdateInfo;

    Dictionary<string, Editor> mEditors = new();

    public delegate void NavigateToSymbolDelegate(string path, int x, int y);

    public event NavigateToSymbolDelegate? OnNavigateToSymbol;

    public ZurfEditController()
    {
        _timer.Tick += _timer_Tick;
        //mContextMenuJson.Items.Add(
        //    new ToolStripLabel("Format Json", null, true, new EventHandler(FormatJson)));
    }

    public void SetHoverMessageParent(Panel parent)
    {
        mHoverMessageForm.IsVisible = false;
        mHoverMessageForm.Width = 600;
        mHoverMessageForm.Height = 200;
        mHoverMessageForm.VerticalAlignment = VerticalAlignment.Top;
        mHoverMessageForm.HorizontalAlignment = HorizontalAlignment.Right;
        parent.Children.Add(mHoverMessageForm);
    }

    public void AddEditor(Editor editor)
    {
        if (mEditors.ContainsKey(editor.Lexer.Path))
            return;

        mEditors[editor.Lexer.Path] = editor;
        editor.LexerChanged += Editor_LexerChanged;
        editor.TextChanged += editor_TextChanged;
        editor.MouseHoverTokenChanged += editor_MouseTokenChanged;
        //editor.MouseClick += editor_MouseClick;
        //editor.MouseDown += editor_MouseDown;
        _timer.IsEnabled = true;
    }

    public void RemoveEditor(Editor editor)
    {
        editor.LexerChanged += Editor_LexerChanged;
        editor.TextChanged -= editor_TextChanged;
        editor.MouseHoverTokenChanged -= editor_MouseTokenChanged;
        //editor.MouseClick -= editor_MouseClick;
        //editor.MouseDown -= editor_MouseDown;
        mEditors.Remove(editor.Lexer.Path);
        if (mEditors.Count == 0)
            _timer.IsEnabled = false;
    }

    /// <summary>
    /// Set the active editor, or null if none is active
    /// </summary>
    public async void ActiveViewChanged(Editor editor)
    {
        mActiveEditor = editor;
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

    void editor_TextChanged(object? sender, EventArgs e)
    {
        if (sender == mActiveEditor)
            mUpdateInfo = true;
    }

    private void Editor_LexerChanged(object? sender, EventArgs e)
    {
        if (sender == mActiveEditor)
            mUpdateInfo = true;
    }


    void _timer_Tick(object? sender, EventArgs e)
    {
        DisplayHoverForm();
        SetHoverFormLocation(false);
        if (mUpdateInfo)
        {
            mUpdateInfo = false;
            if (mActiveEditor != null)
                UpdateScrollBars(mActiveEditor);
        }
    }

    /// <summary>
    /// Setup to display the message for the hover token.
    /// Immediately show connected tokens.
    /// </summary>
    private void editor_MouseTokenChanged(object? sender, Token? prevToken, Token? newToken)
    {
        var editor = sender as Editor;
        if (editor == null)
            return;

        // Setup to display the hover token
        mHoverToken = newToken;
        mHoverMessageForm.IsVisible = false;

        // TBD: Remove after port to Avalonia
        //mHoverMessageForm.IsVisible = newToken != null;
        //if (newToken != null)
        //    mHoverMessageForm.Message = newToken.ToString();

        // Show meta when hovering over control character
        editor.Lexer.ShowMetaTokens = newToken != null
            && newToken.Meta && (newToken == ";" || newToken == "{" || newToken == "}");

        // Update hover token colors
        editor.TokenColorOverrides = [];
        if (newToken == null)
            return;

        // Show active link when CTRL is pressed
        var overrides = new List<TokenColorOverride>();
        /*
        if ((Control.ModifierKeys & Keys.Control) == Keys.Control
            && (newToken.Url != "" || newToken.GetInfo<Symbol>() != null))
        {
            var ov = new TokenColorOverride(newToken);
            ov.Font = new Font(editor.Font, FontStyle.Underline);
            ov.ForeColor = Brushes.Blue;
            overrides.Add(ov);
            editor.Cursor = Cursors.Hand;
        }
        else
        {
            editor.Cursor = Cursors.IBeam;
        }
        */

        // Make a list of connecting tokens
        var connectors = newToken.GetInfo<Token[]>();
        if (connectors != null)
        {
            foreach (Token s in connectors)
            {
                if (sBoldHighlightConnectors.Contains(s))
                    overrides.Add(new TokenColorOverride(s, sBoldConnectorOutlineColor, sBoldConnectorBackColor));
                else
                    overrides.Add(new TokenColorOverride(s, sConnectorOutlineColor, sConnectorBackColor));
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
                if ( (object)screenSymbol == (object)hoverSymbol
                        || hoverSymbol.HasToken && hoverSymbol.Token.Location == screenToken.Location)
                    overrides.Add(new TokenColorOverride(screenToken, sBoldConnectorOutlineColor, sBoldConnectorBackColor));
            }
        }

        // Highlight current location (if not already showing something from above)
        if (newToken.Type != eTokenType.Comment
            || newToken.Subtype == eTokenSubtype.CodeInComment
            || newToken.Underline)
        {
            overrides.Add(new TokenColorOverride(newToken, 
                newToken.Error ? new Pen(Colors.Red.ToUInt32()) : new Pen(Colors.LightBlue.ToUInt32())));
        }

        // Update editor to show them
        editor.TokenColorOverrides = overrides.ToArray();
    }

    /// <summary>
    /// When the user click the editor, hide the message box until a
    /// new token is hovered over.
    /// </summary>
    //private void editor_MouseDown(object sender, MouseEventArgs e)
    //{
    //    mHoverToken = null;
    //    mHoverMessageForm.Visible = false;
    //}

    private void FormatJson(object sender, EventArgs e)
    {
        try
        {
            var editor = mActiveEditor;
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
    /// Open web browser
    /// </summary>
    /*
    private void editor_MouseClick(object sender, MouseEventArgs e)
    {
        var editor = (TextEditor)sender;
        if (e.Button == MouseButtons.Right
            && Path.GetExtension(editor.FilePath).ToLower() == ".json")
        {
            mContextMenuJson.Show(editor, editor.PointToClient(Cursor.Position));
        }


        var token = editor.MouseHoverToken;
        if (token != null 
            && e.Button == MouseButtons.Left
            && (Control.ModifierKeys & Keys.Control) == Keys.Control
            && (token.Url != "" || token.GetInfo<Symbol>() != null))
        {
            if (token.Url.ToLower().StartsWith("http"))
            {
                System.Diagnostics.Process.Start(token.Url);
            }
            else if (token.GetInfo<Symbol>() != null)
            {
                var sym = token.GetInfo<Symbol>();
                if (OnNavigateToSymbol == null)
                    MessageBox.Show("Event handler not installed", "Zurfur");
                else
                    OnNavigateToSymbol?.Invoke(sym.Token.Path, sym.Token.X, sym.Token.Y);
            }
            else
            {
                MessageBox.Show("TBD: Still working on this:)", "Zurfur");
            }
        }
    }
    */

    /// <summary>
    /// Called from timer periodically to display the hover form
    /// </summary>
    void DisplayHoverForm()
    {
        var showForm = mActiveEditor != null
                && mHoverToken != null
                && mHoverToken.Type != eTokenType.Comment
                && (mHoverToken.GetInfo<string>() != null
                        || mHoverToken.GetInfo<ParseInfo>() != null
                        || mHoverToken.GetInfo<Symbol>() != null
                        || mHoverToken.GetInfo<TokenError>() != null
                        || mHoverToken.GetInfo<TokenWarn>() != null)
                && !mHoverMessageForm.IsVisible;
        if (!showForm)
            return;

        if (mHoverToken == null)
            return;

        // Show errors and warnings
        var message = "";
        foreach (var error in mHoverToken.GetInfos<TokenError>())
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

        foreach (var error in mHoverToken.GetInfos<TokenWarn>())
            message += $"WARNING: {error}\r\n";

        // Show symbol info
        if (message != "")
            message += "\r\n";
        message += GetSymbolInfo();

        // Show parse info and strings
        foreach (var s in mHoverToken.GetInfos<ParseInfo>())
            message += s + "\r\n\r\n";
        foreach (var s in mHoverToken.GetInfos<string>())
            message += s + "\r\n\r\n";

        mHoverMessageForm.Message = message.Trim();

        // Show form with proper size and location
        //var size = mHoverMessageForm.Message.Size;
        //mHoverMessageForm.ClientSize = new Size(size.Width + 8, size.Height + 8);
        SetHoverFormLocation(true);
        //mHoverMessageForm.Show(mActiveEditor.ParentForm);
        mHoverMessageForm.IsVisible = true; // TBD: Port
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
        if (mHoverToken == null)
            return "";
        var message = "";
        var symbols = mHoverToken.GetInfos<Symbol>();
        if (symbols.Length == 0)
            return "";

        // When a token has multiple symbols or an error, display a summary.
        if (symbols.Length > 1 || symbols.Length == 1 && mHoverToken.Error)
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
        /*
        // Warnings on text
        var marks = new List<VerticalMarkInfo>();
        int lastMark = -1;
        foreach (var token in editor.Lexer)
        {
            // WARNINGS
            if (token.Warn && token.Location.Y != lastMark)
            {
                lastMark = token.Location.Y;
                marks.Add(new VerticalMarkInfo { Color = Color.Gold, Length = 1, Start = lastMark });
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
                marks.Add(new VerticalMarkInfo { Color = Color.Red, Length = 1, Start = lastMark });
            }
        }
        // Errors on meta tokens
        foreach (var token in editor.Lexer.MetaTokens)
        {
            // ERRORS
            if (token.Error && token.Location.Y != lastMark)
            {
                lastMark = token.Location.Y;
                marks.Add(new VerticalMarkInfo { Color = Color.Red, Length = 1, Start = lastMark });
            }
        }
        editor.SetMarks(marks.ToArray());
        editor.InvalidateAll();
        */
    }
}
