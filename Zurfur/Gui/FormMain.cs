using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Reflection;
using Gosub.Zurfur;

namespace Gosub.Zurfur
{
    public partial class FormMain:Form
    {
        FormHoverMessage mHoverMessageForm;
        Token			mHoverToken;
        DateTime		mLastMouseMoveTime;
        DateTime		mLastEditorChangedTime;
        Editor          mReparseEditor;
        bool            mInActivatedEvent;


        static readonly string EXE_DIR = Path.GetDirectoryName(Application.ExecutablePath);
        static readonly string LICENSE_FILE_NAME = Path.Combine(EXE_DIR, "License.txt");
        static readonly string EXAMPLE_FILE = Path.Combine(EXE_DIR, "ZurfurLib\\Example.zurf");

        public FormMain()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialize this form
        /// </summary>
        private void FormMain_Load(object sender, EventArgs e)
        {
            Text += " - " + "V" + App.Version;
            mHoverMessageForm = new FormHoverMessage();
        }

        private async void FormMain_Shown(object sender, EventArgs e)
        {
            try
            {
                // This will be removed
                await mvEditors.LoadFile(EXAMPLE_FILE);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error loading example: " + ex.Message, App.Name);
            }
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            foreach (var editor in mvEditors.Editors)
            {
                if (!CanClose(editor))
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        /// <summary>
        /// Can we close this editor?  Give user a chance to save first.
        /// </summary>
        bool CanClose(Editor editor)
        {
            if (!editor.Modified)
                return true;
            mvEditors.EditorViewActive = editor;
            var dialogResult = MessageBox.Show(editor.FileTitle + " has unsaved changes.  \r\n\r\n"
                + "Do you want to save this file?", App.Name, MessageBoxButtons.YesNoCancel);
            if (dialogResult == DialogResult.No)
                return true;
            if (dialogResult == DialogResult.Cancel)
                return false;
            if (!SaveFile(editor, false))
                return false;
            return true;
        }

        private void mvEditors_EditorCanClose(Editor editor, ref bool doNotClose)
        {
            if (!CanClose(editor))
            {
                doNotClose = true;
            }
        }

        private void mvEditors_EditorAdded(Editor editor)
        {
            ParseText(editor);
            editor.MouseTokenChanged += editor_MouseTokenChanged;
            editor.TextChanged2 += editor_TextChanged2;
            editor.MouseDown += editor_MouseDown;
        }

        private void mvEditors_EditorRemoved(Editor editor)
        {
            editor.MouseTokenChanged -= editor_MouseTokenChanged;
            editor.TextChanged2 -= editor_TextChanged2;
            editor.MouseDown -= editor_MouseDown;
        }

        private void mvEditors_EditorViewChanged(Editor editor)
        {
            // Reparse old one if necessary
            if (mReparseEditor != null)
                ParseText(mReparseEditor);
            mReparseEditor = null;

            if (editor != null)
                editor_MouseTokenChanged(editor, null, null);
        }

        private void editor_TextChanged2(object sender, EventArgs e)
        {
            // Setup to re-parse some time after the user stops typing
            mLastEditorChangedTime = DateTime.Now;
            mReparseEditor = (Editor)sender;
        }

        /// <summary>
        /// Setup to display the message for the hover token.
        /// Immediately show connected tokens.
        /// </summary>
        private void editor_MouseTokenChanged(Editor editor, Token previousToken, Token newToken)
        {
            // Setup to display the hover token
            mHoverToken = newToken;
            mHoverMessageForm.Visible = false;

            // Update hover token colors
            editor.TokenColorOverrides = null;
            if (newToken != null && newToken.Type != eTokenType.Comment)
            {
                // Make a list of connecting tokens
                List<TokenColorOverride> overrides = new List<TokenColorOverride>();
                overrides.Add(new TokenColorOverride(newToken, Brushes.LightGray));
                Token []connectors = newToken.GetInfo<Token[]>();
                if (connectors != null)
                    foreach (Token s in connectors)
                        overrides.Add(new TokenColorOverride(s, Brushes.LightGray));

                // Update editor to show them
                editor.TokenColorOverrides = overrides.ToArray();
            }
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

        async void menuFileOpen_Click(object sender, EventArgs e)
        {
            var dialogResult = openFileDialog1.ShowDialog(this);
            if (dialogResult != DialogResult.OK || openFileDialog1.FileName == "")
                return;

            try
            {
                await mvEditors.LoadFile(openFileDialog1.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error loading file: " + ex.Message, App.Name);
            }
        }

        private void menuFileSave_Click(object sender, EventArgs e)
        {
            if (mvEditors.EditorViewActive == null)
                return;
            SaveFile(mvEditors.EditorViewActive, false);
        }

        private void menuFileSaveAll_Click(object sender, EventArgs e)
        {
            foreach (var editor in mvEditors.Editors)
                SaveFile(editor, false);
        }

        private void menuFileSaveAs_Click(object sender, EventArgs e)
        {
            if (mvEditors.EditorViewActive == null)
                return;
            SaveFile(mvEditors.EditorViewActive, true);
        }

        /// <summary>
        /// Returns true if the file was saved
        /// </summary>
        bool SaveFile(Editor editor, bool forceSaveAs)
        {
            var filePath = editor.FilePath;
            if (filePath == "" || forceSaveAs)
            {
                var dialogResult = saveFileDialog1.ShowDialog(this);
                if (dialogResult != DialogResult.OK || saveFileDialog1.FileName == "")
                    return false;
                filePath = saveFileDialog1.FileName;
            }
            try
            {
                mvEditors.Save(editor, filePath);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error saving file: " + ex.Message, App.Name);
            }
            return false;
        }

        /// <summary>
        /// Recompile or display hover message when necessary
        /// </summary>
        private void timer1_Tick(object sender, EventArgs e)
        {
            // Recompile 250 milliseconds after the user stops typing
            if (mReparseEditor != null
                && (DateTime.Now - mLastEditorChangedTime).TotalMilliseconds > 250)
            {
                try
                {
                    // Reset the lexer, re-parse, and compile
                    ParseText(mReparseEditor);
                    mReparseEditor = null;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Error compiling: " + ex.Message);
                }
            }

            // Display the hover message (after no mouse movement for 150 milliseconds)
            const int DELAY_TIME_MS = 0;
            var activeEditor = mvEditors.EditorViewActive;
            if (mHoverToken != null && activeEditor != null
                    && mHoverToken.Type != eTokenType.Comment
                    && (DateTime.Now - mLastMouseMoveTime).TotalMilliseconds > DELAY_TIME_MS
                    && mHoverToken.GetInfoString() != ""
                    && !mHoverMessageForm.Visible)
            {
                // Set form size, location, and text
                mHoverMessageForm.Message.Text = mHoverToken.GetInfoString();
                var s = mHoverMessageForm.Message.Size;
                mHoverMessageForm.ClientSize = new Size(s.Width + 8, s.Height + 8);
                var p = activeEditor.PointToScreen(activeEditor.LocationToken(mHoverToken.Location));
                p.Y -= s.Height + 32;
                mHoverMessageForm.Location = p;

                // Display the form
                mHoverMessageForm.Show(this);
            }
        }

        private void ParseText(Editor editor)
        {
            var parser = new Parser(editor.Lexer);
            var t1 = DateTime.Now;
            var program = parser.Parse();
            var t2 = DateTime.Now;

            // TBD: Analyze, code generation, etc
            var t3 = DateTime.Now;

            // Debug times
            var parseTime = t2 - t1;
            var genTime = t3 - t2;

            // Call this function after the tokens changed in a way that
            // causes the vertical marks or connectors to have changed
            var marks = new List<VerticalMarkInfo>();
            int lastMark = -1;
            foreach (var token in editor.Lexer)
            {
                if (token.Error && token.Location.Line != lastMark)
                {
                    lastMark = token.Location.Line;
                    marks.Add(new VerticalMarkInfo { Color = Color.Red, Length = 1, Start = lastMark });
                }
            }
            editor.SetMarks(marks.ToArray());
            editor.Invalidate();
        }

        private void FormMain_KeyDown(object sender, KeyEventArgs e)
        {
            var activeEditor = mvEditors.EditorViewActive;
            if (activeEditor == null)
                return;

            // Display search form
            if (e.Control && e.KeyCode == Keys.F)
                FormSearch.Show(this, activeEditor );
            if (e.KeyCode == Keys.F3)
                FormSearch.FindNext(this, activeEditor);
        }

        private void menuHelpAbout_Click(object sender, EventArgs e)
        {
            MessageBox.Show(this, App.Name + " version " + App.Version, App.Name);
        }

        private void menuHelpLicense_Click(object sender, EventArgs e)
        {
            // Read license from resource
            List<string> lines = new List<string>();
            Stream s = File.OpenRead(LICENSE_FILE_NAME);
            StreamReader sr = new StreamReader(s);
            while (!sr.EndOfStream)
                lines.Add(sr.ReadLine());
            sr.Close();

            // Display the license
            FormHtml form = new FormHtml();
            form.ShowText(lines.ToArray());
        }

        private void menuEditFind_Click(object sender, EventArgs e)
        {
            var activeEditor = mvEditors.EditorViewActive;
            if (activeEditor != null)
                FormSearch.Show(this, activeEditor);
        }

        private void menuEditFindNext_Click(object sender, EventArgs e)
        {
            var activeEditor = mvEditors.EditorViewActive;
            if (activeEditor != null)
                FormSearch.FindNext(this, activeEditor);
        }

        private void viewRTFToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var activeEditor = mvEditors.EditorViewActive;
            if (activeEditor == null)
                return;
            FormHtml form = new FormHtml();
            form.ShowLexer(activeEditor.Lexer);
        }

        private void FormMain_Activated(object sender, EventArgs e)
        {
            // Prevent from getting called again when user answers a question
            if (mInActivatedEvent)
                return;
            mInActivatedEvent = true;

            try
            {
                foreach (var editor in mvEditors.Editors)
                {
                    if (editor.FileInfo == null || editor.FilePath == "")
                        continue;
                    var newFileInfo = new FileInfo(editor.FilePath);
                    newFileInfo.Refresh();
                    if (newFileInfo.LastWriteTimeUtc == editor.FileInfo.LastWriteTimeUtc)
                        continue;

                    mvEditors.EditorViewActive = editor;
                    var message = editor.Modified ?
                        "This file has UNSAVED CHANGES inside this editor and "
                        + "has been changed externlly.\r\n\r\n Do you want to "
                        + "reload it and lose the changes made in the editor?"
                        : "This file has been changed externally, and has no "
                        + "unsaved changes inside the editor.\r\n\r\n"
                        + "Do you want to reload it?";
                    if (MessageBox.Show(this, editor.FilePath + "\r\n\r\n" + message,
                        App.Name, MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        editor.Lexer.ScanLines(File.ReadAllLines(editor.FilePath));
                        editor.Modified = false;
                    }
                    editor.FileInfo = new FileInfo(editor.FilePath);
                    editor.FileInfo.Refresh();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error: " + ex.Message, App.Name);
            }
            finally
            {
                mInActivatedEvent = false;
            }
        }

        private void menuFile_DropDownOpening(object sender, EventArgs e)
        {
            menuFileSave.Enabled = mvEditors.EditorViewActive != null;
            menuFileSaveAll.Enabled = mvEditors.EditorViewActive != null;
            menuFileSaveAs.Enabled = mvEditors.EditorViewActive != null;
        }

        private void menuEdit_DropDownOpening(object sender, EventArgs e)
        {
            menuEditFind.Enabled = mvEditors.EditorViewActive != null;
            menuEditFindNext.Enabled = mvEditors.EditorViewActive != null;
            viewRTFToolStripMenuItem.Enabled = mvEditors.EditorViewActive != null;
        }

    }
}
