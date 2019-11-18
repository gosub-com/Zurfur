using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Gosub.Zurfur.Compiler;

namespace Gosub.Zurfur.Ide
{
    /// <summary>
    /// Manage a group of Zurfur text editors.   Customizes their look
    /// and interacts with the compiler asking for symbolic info.
    /// </summary>
    class ZurfEditController
    {
        FormHoverMessage mHoverMessageForm;
        Token mHoverToken;
        DateTime mLastEditorChangedTime;
        TextEditor mActiveEditor;
        TextEditor mReparseEditor;

        static Pen sBoldConnectorOutlineColor = new Pen(Color.FromArgb(192, 192, 255));
        static Brush sBoldConnectorBackColor = new SolidBrush(Color.FromArgb(224, 224, 255));
        static Pen sConnectorOutlineColor = new Pen(Color.FromArgb(192, 192, 255));
        static Brush sConnectorBackColor = null;
        static WordSet sBoldHighlightConnectors = new WordSet("( ) [ ] { } < >");

        public ZurfEditController()
        {
            mHoverMessageForm = new FormHoverMessage();
        }

        public void AddEditor(TextEditor editor)
        {
            ParseText(editor);
            editor.MouseHoverTokenChanged += editor_MouseTokenChanged;
            editor.TextChanged2 += editor_TextChanged2;
            editor.MouseClick += editor_MouseClick;
            editor.MouseDown += editor_MouseDown;
        }

        public void RemoveEditor(TextEditor editor)
        {
            editor.MouseHoverTokenChanged -= editor_MouseTokenChanged;
            editor.TextChanged2 -= editor_TextChanged2;
            editor.MouseClick -= editor_MouseClick;
            editor.MouseDown -= editor_MouseDown;
        }

        /// <summary>
        /// Set the active editor, or null if none is active
        /// </summary>
        public void ActiveViewChanged(TextEditor editor)
        {
            // Reparse old one if necessary
            if (mReparseEditor != null)
                ParseText(mReparseEditor);
            mReparseEditor = null;

            mActiveEditor = editor;
            if (editor != null)
                editor_MouseTokenChanged(editor, null, null);
        }

        private void editor_TextChanged2(object sender, EventArgs e)
        {
            // Setup to re-parse some time after the user stops typing
            mLastEditorChangedTime = DateTime.Now;
            mReparseEditor = sender as TextEditor;
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

        /// <summary>
        /// Open web browser
        /// </summary>
        private void editor_MouseClick(object sender, MouseEventArgs e)
        {
            var token = ((TextEditor)sender).MouseHoverToken;
            if (token != null && (Control.ModifierKeys & Keys.Control) == Keys.Control
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
        /// Should be called periodically from the UI thread to
        /// </summary>
        public void Timer()
        {
            CheckForRecompile();
            DisplayHoverForm();
        }
        /// <summary>
        /// Called periodically from timer to recompile
        /// </summary>
        private void CheckForRecompile()
        {
            if (mReparseEditor != null
                && (DateTime.Now - mLastEditorChangedTime).TotalMilliseconds > 250)
            {
                if (System.Diagnostics.Debugger.IsAttached)
                {
                    // Reset the lexer, re-parse, and compile
                    ParseText(mReparseEditor);
                    mReparseEditor = null;
                }
                else
                {
                    try
                    {
                        // Reset the lexer, re-parse, and compile
                        ParseText(mReparseEditor);
                        mReparseEditor = null;
                    }
                    catch (Exception ex)
                    {
                        mReparseEditor = null;
                        if (mActiveEditor != null)
                            MessageBox.Show(mActiveEditor, "Error compiling: " + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Called periodically to display the hover form
        /// </summary>
        private void DisplayHoverForm()
        {
            if (mActiveEditor != null
                    && mHoverToken != null
                    && mHoverToken.Type != eTokenType.Comment
                    && (mHoverToken.GetInfoString() != "" || mHoverToken.GetInfo<Symbol>() != null)
                    && !mHoverMessageForm.Visible)
            {
                // Get message
                string message = "";
                var symbol = mHoverToken.GetInfo<Symbol>();
                if (symbol != null)
                {
                    message += symbol.ToString() + "\r\n";
                    if (symbol.Comments != "")
                        message += symbol.Comments + "\r\n";
                    message += "\r\n";
                }
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
        }

        private void ParseText(TextEditor editor)
        {
            // For the time being, we'll use the extension to decide
            // which parser to use.  
            // TBD: This will all be moved to a build manager
            var ext = System.IO.Path.GetExtension(editor.FilePath).ToLower();
            if (ext == ".zurf")
            {
                // Parse text
                var t1 = DateTime.Now;
                var parser = new ZurfParse(editor.Lexer);
                var program = parser.Parse();

                // Generate Sil
                if (!parser.ParseError)
                {
                    // TBD: This will all be moved to a bild manager
                    var sil = new SilGen(editor.FilePath, program);
                    sil.GenerateTypeDefinitions();
                    sil.MergeTypeDefinitions();
                    sil.GenerateHeader();
                    sil.GenerateCode();
                }
                var t2 = DateTime.Now;
                var parseTime = t2 - t1;


                // Show parser generated tokens
                var extraTokens = parser.ExtraTokens();
                editor.ExtraTokens = extraTokens;

            }
            else if (ext == ".json")
            {
                var parser = new JsonParse(editor.Lexer);
                parser.Parse();
            }
            else
            {
                return;
            }

            // Call this function after the tokens changed in a way that
            // causes the vertical marks or connectors to have changed
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
            foreach (var token in editor.ExtraTokens)
            {
                // ERRORS
                if (token.Error && token.Location.Y != lastMark)
                {
                    lastMark = token.Location.Y;
                    marks.Add(new VerticalMarkInfo { Color = Color.Red, Length = 1, Start = lastMark });
                }
            }
            editor.SetMarks(marks.ToArray());
            editor.Invalidate();
        }



    }
}
