using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Gosub.Zurfur.Lex;
using Gosub.Zurfur.Compiler;
using System.IO;
using Newtonsoft.Json;

namespace Gosub.Zurfur.Ide
{
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
        ContextMenu mContextMenuJson = new ContextMenu();

        Token mHoverToken;
        TextEditor mActiveEditor;
        Timer mTimer = new Timer() { Interval = 20 };
        bool mUpdateInfo;

        Dictionary<string, TextEditor> mEditors = new Dictionary<string, TextEditor>();

        public ZurfEditController()
        {
            mHoverMessageForm = new FormHoverMessage();
            mTimer.Tick += mTimer_Tick;
            mContextMenuJson.MenuItems.Add("Format Json", new EventHandler(FormatJson));
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
        private void editor_MouseTokenChanged(TextEditor editor, Token previousToken, Token newToken)
        {
            // Setup to display the hover token
            mHoverToken = newToken;
            mHoverMessageForm.Visible = false;

            // Update hover token colors
            editor.TokenColorOverrides = null;
            if (newToken == null)
                return;

            // Show active link when CTRL is pressed
            List<TokenColorOverride> overrides = new List<TokenColorOverride>();
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
            var symbol = newToken.GetInfo<Symbol>();
            if (symbol != null)
            {
                var endLine = editor.TopVisibleLine + editor.LinesInWindow();
                foreach (var token in editor.Lexer.GetEnumeratorStartAtLine(editor.TopVisibleLine))
                {
                    if (token.Y > endLine)
                        break;
                    if (token.GetInfo<Symbol>() == symbol && !token.Error)
                        overrides.Add(new TokenColorOverride(token, sBoldConnectorOutlineColor, sBoldConnectorBackColor));

                }
            }

            // Highlight current location (if not already showing something from above)
            if (newToken.Type != eTokenType.Comment
                && newToken.Type != eTokenType.PublicComment
                || newToken.Subtype == eTokenSubtype.CodeInComment
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
                var json = JsonConvert.DeserializeObject(s);
                s = JsonConvert.SerializeObject(json, Formatting.Indented);
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
                    MessageBox.Show("TBD: Still working on goto symbol", "Zurfur");
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
                    && mHoverToken.Type != eTokenType.Comment
                    && (mHoverToken.GetInfoString() != ""
                            || mHoverToken.GetInfo<Symbol>() != null
                            || mHoverToken.GetInfo<TokenError>() != null
                            || mHoverToken.GetInfo<TokenWarn>() != null)
                    && !mHoverMessageForm.Visible;
            if (!showForm)
                return;

            // Show symbol info
            var message = GetSymbolInfo();

            foreach (var error in mHoverToken.GetInfos<TokenError>())
            {
                var errorType = "";
                if (error is ParseZurf.ParseError)
                    errorType = " (parse)";
                else if (error is ZilHeaderError)
                    errorType = " (gen header)";
                else if (error is VerifyHeaderError)
                    errorType = " (verify header)";
                message += $"ERROR{errorType}: {error}\r\n";
            }

            foreach (var error in mHoverToken.GetInfos<TokenWarn>())
                message += $"WARNING: {error}\r\n";

            message += mHoverToken.GetInfoString();
            mHoverMessageForm.Message.Text = message.Trim();

            // Show form with proper size and location
            var size = mHoverMessageForm.Message.Size;
            mHoverMessageForm.ClientSize = new Size(size.Width + 8, size.Height + 8);
            var location = mActiveEditor.PointToScreen(mActiveEditor.LocationToken(mHoverToken.Location));
            location.Y += mActiveEditor.FontSize.Height + 8;
            location.X = Form.MousePosition.X;
            mHoverMessageForm.Location = location;
            mHoverMessageForm.Show(mActiveEditor.ParentForm);
        }

        private string GetSymbolInfo()
        {
            var message = "";
            var symbols = mHoverToken.GetInfos<Symbol>();
            if (symbols.Length == 0)
                return "";

            var symbol = symbols[0];
            message += "[" + string.Join(", ", symbol.QualifiersStr().Split(' ')) + "]\r\n";

            if (symbol.IsField || symbol.IsMethodParam)
            {
                message += symbol.KindName.ToUpper() + ": " + symbol.ToString() + "\r\n";
                message += "TYPE: " + symbol.TypeName + "\r\n";
            }
            else if (symbol.IsMethod)
            {
                message += symbol.KindName.ToUpper() + ": " + symbol.FullName + "\r\n";
                message += "PARAMS: \r\n";
                foreach (var child in symbol.Children)
                {
                    if (child.Value.IsMethodParam)
                        message += "    " + child.Key + ": " + child.Value.TypeName + "\r\n";
                    else if (child.Value.IsTypeParam)
                        message += "    " + child.Key + ": Type parameter\r\n";
                    else
                        message += "    " + child.Key + ": COMPILER ERROR\r\n";
                }
            }
            else
            {
                message += symbol.KindName.ToUpper() + ": " + symbol.ToString() + "\r\n";
            }

            // Comments
            if (symbol.Comments != "")
            {
                message += "COMMENTS:\r\n" + symbol.Comments + "\r\n";
            }
            message += "\r\n";

            // Only show one symbol, but if there are duplicates let us know about them
            if (symbols.Length != 1)
            {
                // We get this when there are parse errors
                message += "DUPLICATE SYMBOL ERROR: There are " + symbols.Length + " symbols\r\n";
            }
            return message;
        }

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
}
