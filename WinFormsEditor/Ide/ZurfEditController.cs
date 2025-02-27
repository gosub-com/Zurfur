﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;

using Gosub.Lex;
using Zurfur.Compiler;
using Zurfur.Vm;

namespace Zurfur.Ide;

/// <summary>
/// Manage a group of Zurfur text editors.   Customizes their look.
/// </summary>
class ZurfEditController
{
    static Pen sBoldConnectorOutlineColor = new Pen(Color.FromArgb(192, 192, 255));
    static Brush sBoldConnectorBackColor = new SolidBrush(Color.FromArgb(224, 224, 255));
    static Pen sConnectorOutlineColor = new Pen(Color.FromArgb(192, 192, 255));
    static Brush sConnectorBackColor = null;
    static WordSet sBoldHighlightConnectors = new WordSet("( ) [ ] { } < >");

    FormHoverMessage mHoverMessageForm;
    ContextMenuStrip mContextMenuJson = new ContextMenuStrip()
        { AutoSize = false, Width = 100, ShowImageMargin = false }; // Autosizing didn't work, so hard code it here

    Token mHoverToken;
    TextEditor mActiveEditor;
    Timer mTimer = new Timer() { Interval = 20 };
    bool mUpdateInfo;

    Dictionary<string, TextEditor> mEditors = new Dictionary<string, TextEditor>();

    public delegate void NavigateToSymbolDelegate(string path, int x, int y);

    public event NavigateToSymbolDelegate OnNavigateToSymbol;

    public ZurfEditController()
    {
        mHoverMessageForm = new FormHoverMessage();
        mTimer.Tick += mTimer_Tick;
        mContextMenuJson.Items.Add(
            new ToolStripLabel("Format Json", null, true, new EventHandler(FormatJson)));
    }

    public void AddEditor(TextEditor editor)
    {
        if (mEditors.ContainsKey(editor.FilePath))
            return;

        mEditors[editor.FilePath] = editor;
        editor.LexerChanged += Editor_LexerChanged;
        editor.TextChanged2 += editor_TextChanged2;
        editor.MouseHoverTokenChanged += editor_MouseTokenChanged;
        editor.MouseClick += editor_MouseClick;
        editor.MouseDown += editor_MouseDown;
        mTimer.Enabled = true;
    }

    public void RemoveEditor(TextEditor editor)
    {
        editor.LexerChanged += Editor_LexerChanged;
        editor.TextChanged2 -= editor_TextChanged2;
        editor.MouseHoverTokenChanged -= editor_MouseTokenChanged;
        editor.MouseClick -= editor_MouseClick;
        editor.MouseDown -= editor_MouseDown;
        mEditors.Remove(editor.FilePath);
        if (mEditors.Count == 0)
            mTimer.Enabled = false;
    }

    /// <summary>
    /// Set the active editor, or null if none is active
    /// </summary>
    public async void ActiveViewChanged(TextEditor editor)
    {
        mActiveEditor = editor;
        if (editor != null)
        {
            UpdateScrollBars(editor);
            editor_MouseTokenChanged(editor, null, null);

            // Show Format Json context menu
            if (Path.GetExtension(editor.FilePath).ToLower() == ".json"
                && editor.Lexer.LineCount == 1
                && editor.Lexer.GetLine(0).Length > 80)
            {
                // The control is invisible, so wait a short period for it to appear
                await System.Threading.Tasks.Task.Delay(50);
                if (editor.Visible)
                    mContextMenuJson.Show(editor, new Point(20, 30));
            }
        }
    }

    void editor_TextChanged2(object sender, EventArgs e)
    {
        if (sender == mActiveEditor)
            mUpdateInfo = true;
    }

    private void Editor_LexerChanged(object sender, EventArgs e)
    {
        if (sender == mActiveEditor)
            mUpdateInfo = true;
    }


    void mTimer_Tick(object sender, EventArgs e)
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
    private void editor_MouseTokenChanged(TextEditor editor, Token prevToken, Token newToken)
    {
        // Setup to display the hover token
        mHoverToken = newToken;
        mHoverMessageForm.Visible = false;

        // Show meta when hovering over control character
        editor.ShowMetaTokens = newToken != null
            && newToken.Meta && (newToken == ";" || newToken == "{" || newToken == "}");

        // Update hover token colors
        editor.TokenColorOverrides = null;
        if (newToken == null)
            return;

        // Show active link when CTRL is pressed
        var overrides = new List<TokenColorOverride>();
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

        // Make a list of connecting tokens
        Token[] connectors = newToken.GetInfo<Token[]>();
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
        if (newToken.Type != TokenType.Comment
            || newToken.Subtype == TokenSubType.CodeInComment
            || newToken.Underline)
        {
            overrides.Add(new TokenColorOverride(newToken, newToken.Error ? Pens.Red : Pens.LightBlue));
        }

        // Update editor to show them
        editor.TokenColorOverrides = overrides.ToArray();
    }

    /// <summary>
    /// When the user click the editor, hide the message box until a
    /// new token is hovered over.
    /// </summary>
    private void editor_MouseDown(object sender, MouseEventArgs e)
    {
        mHoverToken = null;
        mHoverMessageForm.Visible = false;
    }

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
            MessageBox.Show($"ERROR: {ex.Message}");
        }
    }


    /// <summary>
    /// Open web browser
    /// </summary>
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
                    OnNavigateToSymbol?.Invoke(sym.Path, sym.Token.X, sym.Token.Y);
            }
            else
            {
                MessageBox.Show("TBD: Still working on this:)", "Zurfur");
            }
        }

    }

    /// <summary>
    /// Called periodically to display the hover form
    /// </summary>
    void DisplayHoverForm()
    {
        var showForm = mActiveEditor != null
                && mHoverToken != null
                && mHoverToken.Type != TokenType.Comment
                && (mHoverToken.GetInfo<string>() != null
                        || mHoverToken.GetInfo<ParseInfo>() != null
                        || mHoverToken.GetInfo<Symbol>() != null
                        || mHoverToken.GetInfo<TokenError>() != null
                        || mHoverToken.GetInfo<TokenWarn>() != null)
                && !mHoverMessageForm.Visible;
        if (!showForm)
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

        foreach (var s in mHoverToken.GetInfos<ParseInfo>())
            message += s + "\r\n\r\n";
        foreach (var s in mHoverToken.GetInfos<string>())
            message += s + "\r\n\r\n";

        mHoverMessageForm.Message.Text = message.Trim();

        // Show form with proper size and location
        var size = mHoverMessageForm.Message.Size;
        mHoverMessageForm.ClientSize = new Size(size.Width + 8, size.Height + 8);
        SetHoverFormLocation(true);
        mHoverMessageForm.Show(mActiveEditor.ParentForm);
    }

    private void SetHoverFormLocation(bool setX)
    {
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
    }

    private string GetSymbolInfo()
    {
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
        message +=$"[{getQualifiers(symbol)}] {symbol.FriendlyName()}\r\n";
        if (symbol.Type != null && !symbol.IsFun && !symbol.IsLambda)
            message += $"Type: {symbol.Type.FriendlyName()}\r\n";

        if ( (symbol.Concrete.Comments ?? "") .Trim() != "")
            message += $"// {symbol.Concrete.Comments}\r\n";

        // Raw symbol info
        message += $"\r\nFull Name: {symbol.FullName}\r\n";
        if (symbol.IsSpecialized)
            message += $"Non-specialized: {symbol.Concrete.FullName}\r\n"; 
        if (symbol.Type != null && !symbol.IsFun && !symbol.IsLambda)
            message += $"Type Name: {symbol.Type.FullName}\r\n";

        if (symbol.IsFun)
        {
            message += "\r\nPARAMS: \r\n";
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

    void UpdateScrollBars(TextEditor editor)
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
    }
}
