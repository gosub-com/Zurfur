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

        static readonly string EXE_DIR = Path.GetDirectoryName(Application.ExecutablePath);
        static readonly string EXAMPLE_FILE_NAME = Path.Combine(EXE_DIR, "Example.zurf");
        static readonly string LICENSE_FILE_NAME = Path.Combine(EXE_DIR, "License.txt");

        public FormMain()
        {
            InitializeComponent();

        }

        /// <summary>
        /// Initialize this form
        /// </summary>
        private void FormBit_Load(object sender, EventArgs e)
        {
            // Read the CPU from the embedded resource
            List<string> lines = new List<string>();
            Stream s = File.OpenRead(EXAMPLE_FILE_NAME);
            StreamReader sr = new StreamReader(s);
            while (!sr.EndOfStream)
                lines.Add(sr.ReadLine());
            sr.Close();
            string []file = lines.ToArray();
            
            editor1.Lexer.ScanLines(file);
            editor1_TextChanged2(null, null);
            Text += " - " + "V" + App.Version;


            mHoverMessageForm = new FormHoverMessage();
        }

        private void editor1_Load(object sender, EventArgs e)
        {
        }


        /// <summary>
        /// Setup to re-compile after the user stops typing for a short while
        /// </summary>
        private void editor1_TextChanged2(object sender, EventArgs e)
        {
            mLastEditorChangedTime = DateTime.Now;
        }

        private void editor1_BlockedByReadOnly(object sender, EventArgs e)
        {
            MessageBox.Show(this, "You can not modify text while the similation window is open.");
        }

        /// <summary>
        /// Setup to display the message for the hover token.
        /// Immediately show connected tokens.
        /// </summary>
        private void editor1_MouseTokenChanged(Token previousToken, Token newToken)
        {
            // Setup to display the hover token
            mHoverToken = newToken;
            mHoverMessageForm.Visible = false;

            // Update hover token colors
            editor1.TokenColorOverrides = null;
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
                editor1.TokenColorOverrides = overrides.ToArray();
            }
        }


        /// <summary>
        /// When the user click the editor, hide the message box until a
        /// new token is hovered over.
        /// </summary>
        private void editor1_MouseDown(object sender, MouseEventArgs e)
        {
            mHoverToken = null;
            mHoverMessageForm.Visible = false;
        }

        /// <summary>
        /// Keep track of mouse movement time for hover message display
        /// </summary>
        private void editor1_MouseMove(object sender, MouseEventArgs e)
        {
            mLastMouseMoveTime = DateTime.Now;
        }

        /// <summary>
        /// Recompile or display hover message when necessary
        /// </summary>
        private void timer1_Tick(object sender, EventArgs e)
        {
            // Recompile 250 milliseconds after the user stops typing
            if (mLastEditorChangedTime != new DateTime()
                && (DateTime.Now - mLastEditorChangedTime).TotalMilliseconds > 250)
            {
                try
                {
                    // Reset the lexer, re-parse, and compile
                    mLastEditorChangedTime = new DateTime();
                    ParseText();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Error compiling: " + ex.Message);
                }
            }

            // Display the hover message (after no mouse movement for 150 milliseconds)
            const int DELAY_TIME_MS = 0;
            if ( mHoverToken != null
                    && mHoverToken.Type != eTokenType.Comment
                    && mHoverToken.GetInfoString() != ""
                    && (DateTime.Now - mLastMouseMoveTime).TotalMilliseconds > DELAY_TIME_MS
                    && !mHoverMessageForm.Visible)
            {
                // Set form size, location, and text
                mHoverMessageForm.Message.Text = mHoverToken.GetInfoString();
                var s = mHoverMessageForm.Message.Size;
                mHoverMessageForm.ClientSize = new Size(s.Width + 8, s.Height + 8);
                var p = editor1.PointToScreen(editor1.LocationToken(mHoverToken.Location));
                p.Y -= s.Height + 32;
                mHoverMessageForm.Location = p;

                // Display the form
                mHoverMessageForm.Show(this);
            }
        }

        private void ParseText()
        {
            var parser = new Parser(editor1.Lexer);
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
            foreach (var token in editor1.Lexer)
            {
                if (token.Error && token.Location.Line != lastMark)
                {
                    lastMark = token.Location.Line;
                    marks.Add(new VerticalMarkInfo { Color = Color.Red, Length = 1, Start = lastMark });
                }
            }
            editor1.SetMarks(marks.ToArray());
            editor1.Invalidate();
        }

        private void menuFileOpen_Click(object sender, EventArgs e)
        {
            MessageBox.Show(this, "This function is not implemented");
        }

        private void menuFileSave_Click(object sender, EventArgs e)
        {
            MessageBox.Show(this, "This function is not implemented");
        }

        private void FormBit_KeyDown(object sender, KeyEventArgs e)
        {
            // Display search form
            if (e.Control && e.KeyCode == Keys.F)
                FormSearch.Show(this, editor1 );
            if (e.KeyCode == Keys.F3)
                FormSearch.FindNext(this, editor1);
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
            FormSearch.Show(this, editor1);
        }

        private void menuEditFindNext_Click(object sender, EventArgs e)
        {
            FormSearch.FindNext(this, editor1);
        }

        private void viewRTFToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FormHtml form = new FormHtml();
            form.ShowLexer(editor1.Lexer);
        }

    }
}
