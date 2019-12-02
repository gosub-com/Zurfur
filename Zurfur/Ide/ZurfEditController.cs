using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using Gosub.Zurfur.Lex;
using Gosub.Zurfur.Compiler;
using Gosub.Zurfur.Build;

namespace Gosub.Zurfur.Ide
{
    /// <summary>
    /// Manage a group of Zurfur text editors.   Customizes their look
    /// and interacts with the compiler asking it to build and
    /// retrieve symbolic info.
    /// </summary>
    class ZurfEditController
    {
        static Pen sBoldConnectorOutlineColor = new Pen(Color.FromArgb(192, 192, 255));
        static Brush sBoldConnectorBackColor = new SolidBrush(Color.FromArgb(224, 224, 255));
        static Pen sConnectorOutlineColor = new Pen(Color.FromArgb(192, 192, 255));
        static Brush sConnectorBackColor = null;
        static WordSet sBoldHighlightConnectors = new WordSet("( ) [ ] { } < >");

        FormHoverMessage mHoverMessageForm;
        Token mHoverToken;
        TextEditor mActiveEditor;

        Dictionary<string, TextEditor> mEditors = new Dictionary<string, TextEditor>();

        /// <summary>
        /// For the time being, the editor is in charge of sending files to the build.
        /// This will be changed so there is a looser assocition.
        /// </summary>
        BuildManager mBuildPackage;

        public ZurfEditController(BuildManager buildPackage)
        {
            mBuildPackage = buildPackage;
            mHoverMessageForm = new FormHoverMessage();
        }

        public void AddEditor(TextEditor editor)
        {
            if (mEditors.ContainsKey(editor.FilePath))
                return;

            mEditors[editor.FilePath] = editor;

            if (mEditors.Count == 1)
                MonitorBuildPackage();

            editor.MouseHoverTokenChanged += editor_MouseTokenChanged;
            editor.TextChanged2 += editor_TextChanged2;
            editor.MouseClick += editor_MouseClick;
            editor.MouseDown += editor_MouseDown;

            SendRecompileMessage(editor);
        }

        public void RemoveEditor(TextEditor editor)
        {
            editor.MouseHoverTokenChanged -= editor_MouseTokenChanged;
            editor.TextChanged2 -= editor_TextChanged2;
            editor.MouseClick -= editor_MouseClick;
            editor.MouseDown -= editor_MouseDown;

            mEditors.Remove(editor.FilePath);
            mBuildPackage.CloseFile(editor.FilePath);

            // So the awaiter exits if this is the last editor
            mBuildPackage.ForceNotifyBuildChanged();

        }

        /// <summary>
        /// Set the active editor, or null if none is active
        /// </summary>
        public void ActiveViewChanged(TextEditor editor)
        {
            mActiveEditor = editor;
            if (editor != null)
                editor_MouseTokenChanged(editor, null, null);
        }

        private void editor_TextChanged2(object sender, EventArgs e)
        {
            // Send message to build build manager to recompile
            var editor = sender as TextEditor;
            SendRecompileMessage(editor);
        }

        private void SendRecompileMessage(TextEditor editor)
        {
            if (editor != null)
            {
                var buildFile = mBuildPackage.GetFile(editor.FilePath);
                if (buildFile != null)
                    buildFile.Lexer = editor.Lexer;
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
        /// Called periodically from the UI thread to show various info
        /// </summary>
        public void Timer()
        {
            DisplayHoverForm();
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

        async void MonitorBuildPackage()
        {
            while (mEditors.Count != 0)
            {
                await mBuildPackage.AwaitBuildChanged();

                foreach (var kv in mEditors)
                    UpdateBuildInfo(kv.Value);
            }
        }

        private void UpdateBuildInfo(TextEditor editor)
        {
            // TBD: This could be sped up by tracking `FileBuildVersion`
            //      but is probably not really necessary
            var buildFile = mBuildPackage.GetFile(editor.FilePath);
            if (buildFile == null)
            {
                if (System.Diagnostics.Debugger.IsAttached)
                    throw new Exception("Build file shouldn't be null here");
                return;
            }

            // Update the editor with new build info
            editor.ExtraTokens = buildFile.ExtraTokens;
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
