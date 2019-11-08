using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Gosub.Zurfur.Compiler;

namespace Gosub.Zurfur
{
    /// <summary>
    /// Search an editor for a string.  Call Show(Form, Editor) to show
    /// the search box.  This form hides when deleted so it keeps the
    /// previously searched text.  Call FindNext to find the previous
    /// search text without displaying this form.
    /// </summary>
    public partial class FormSearch:Form
    {
        // The form
        static FormSearch		sFormSearch;
    
        /// <summary>
        /// This must be set to the editor before showing the form
        /// </summary>
        public TextEditor	mEditor;
        TokenLoc		mStartSearchLoc;
        TokenLoc		mPreviousSearchLoc = new TokenLoc(-1, -1);

        /// <summary>
        /// Initialize form
        /// </summary>
        public FormSearch()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Returns the search string
        /// </summary>
        public string SearchText { get { return textSearch.Text; } }


        /// <summary>
        /// Call this function to show the search form
        /// </summary>
        public static void Show(Form owner, TextEditor editor)
        {
            // Create the form
            if (sFormSearch == null || sFormSearch.IsDisposed)
                sFormSearch = new FormSearch();

            // Display the form (possibly with a new owner)
            sFormSearch.mEditor = editor;
            if (owner != sFormSearch.Owner)
                sFormSearch.Visible = false;
            if (!sFormSearch.Visible)
                sFormSearch.Show(owner);

            // Take selected text for search box (if any)
            TokenLoc selStart = editor.SelStart;
            TokenLoc selEnd = editor.SelEnd;
            if (selStart != selEnd && selStart.Y == selEnd.Y)
            {
                string []search = editor.Lexer.GetText(selStart, selEnd);
                if (search.Length == 1)
                    sFormSearch.textSearch.Text = search[0];
            }

            // Set search box focus
            sFormSearch.textSearch.Focus();
            sFormSearch.textSearch.SelectAll();
        }

        /// <summary>
        /// Call this function to "FindNext"
        /// </summary>
        public static void FindNext(Form owner, TextEditor editor)
        {
            if (sFormSearch == null || sFormSearch.SearchText.Trim() == "")
            {
                Show(owner, editor);
                sFormSearch.mEditor = editor;
            }
            else
            {
                if (sFormSearch.Owner != owner)
                    Show(owner, editor);
                sFormSearch.mEditor = editor;
                sFormSearch.FindNext(owner);
            }
        }

        /// <summary>
        /// Returns TRUE if the character is a word separator
        /// </summary>
        bool IsWordSeparator(char ch)
        {
            if (ch >= 'a' && ch <= 'z'
                    || ch >= 'A' && ch <= 'Z'
                    || ch == '-')
                return false;
            return true;
        }

        /// <summary>
        /// Check to see if 'find' matches the text in 'text'.
        /// Returns TRUE if they match, FALSE if not.
        /// Does not change the current scan locations
        /// </summary>
        bool Match(Scanner text, Scanner find, ref TokenLoc matchEnd)
        {
            // Save original locations
            TokenLoc textLoc = text.Location;
            TokenLoc findLoc = find.Location;
            matchEnd = textLoc;

            bool found = MatchInternal(text, find, ref matchEnd);

            // Restore text locations, return result
            if (found)
                matchEnd = text.Location;
            text.Location = textLoc;
            find.Location = findLoc;
            return found;
        }

        /// <summary>
        /// Check to see if 'find' matches the text in 'text'.
        /// Returns TRUE if they match, FALSE if not.
        /// Changes the current scan locations, does not always set matchEnd
        /// </summary>
        bool MatchInternal(Scanner text, Scanner find, ref TokenLoc matchEnd)
        {
            // Verify we are at the start of a word (if requested)
            if (checkMatchWholeWord.Checked)
            {
                text.Location.X--;
                if (!IsWordSeparator(text.Peek()))
                    return false;
                text.Location.X++;
            }

            // Scan until we find a match or find the end
            bool matchCase = checkMatchCase.Checked;
            bool found = true;
            while (!find.AtEnd())
            {
                char findPeek = find.Peek();
                char textPeek = text.Peek();
                if (!matchCase)
                {
                    findPeek = char.ToUpper(findPeek);
                    textPeek = char.ToUpper(textPeek);
                }
                if (findPeek != textPeek)
                {
                    found = false;
                    break;
                }
                find.Inc();
                text.Inc();
            }

            // Verify we are at the end of a word (if requested)
            if (checkMatchWholeWord.Checked && found)
                return IsWordSeparator(text.Peek());

            return found;
        }		   

        /// <summary>
        /// Find Next
        /// </summary>
        void FindNext(Form messageBoxParent)
        {
            // Find start location (cursor or beginning of selected text)
            TokenLoc start = mEditor.CursorLoc;
            if (mEditor.HasSel())
                start = mEditor.SelStart;

            // Start new search?
            if (mPreviousSearchLoc != start)
            {
                mPreviousSearchLoc = new TokenLoc(-1, -1);
                mStartSearchLoc = start;
            }

            // Create text scanner
            Scanner text = new Scanner(mEditor.Lexer.GetText());
            text.Location = start;

            // Create search string scanner
            List<string> findList = new List<string>();
            findList.Add(textSearch.Text);
            Scanner find = new Scanner(findList.ToArray());

            // Skip first char to move past previous search
            if (mEditor.HasSel())
                text.Inc();

            // Scan for a match
            TokenLoc matchEnd = new TokenLoc();
            bool firstChar = true;
            bool pastEnd = false;
            while (true)
            {
                // Past end of previous search?
                if (!firstChar && text.Location == start)
                {
                    MessageBox.Show(messageBoxParent,
                        "The search text was not found", Text);
                    return;
                }

                if (!firstChar && text.Location == mStartSearchLoc)
                    pastEnd = true;

                // Found match?
                if (Match(text, find, ref matchEnd))
                {
                    mEditor.SelSet(text.Location, matchEnd);
                    mEditor.CursorLoc = text.Location;
                    mPreviousSearchLoc = text.Location;

                    if (pastEnd)
                        MessageBox.Show(messageBoxParent,
                            "Find reached the starting point of the search", Text);

        
                    return;
                }
                text.Inc();
                firstChar = false;
            }
        }

        private void FormSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.F3)
                FindNext(this);
            if (e.KeyCode == Keys.Escape)
            {
                // For whatever reason, if we don't do this
                // the main window can go under every other window
                // in the system
                Owner.BringToFront();
                Owner.Focus();
                Hide();
            }
        }

        private void FormSearch_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == 27)
                e.Handled = true;
        }

        private void FormSearch_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                // For whatever reason, if we don't do this
                // the main window can go under every other window
                // in the system
                Owner.BringToFront();
                Owner.Focus();
                Hide();
            }
        }

        private void buttonFindNext_Click(object sender, EventArgs e)
        {
            FindNext(this);
        }

        /// <summary>
        /// Start a new search whenever the search text changes
        /// </summary>
        private void textSearch_TextChanged(object sender, EventArgs e)
        {
            mPreviousSearchLoc = new TokenLoc(-1, -1);
        }
        /// <summary>
        /// Helper class to scan text for matches
        /// </summary>
        class Scanner
        {
            public TokenLoc Location;
            string			[]mLines;

            public Scanner(string[] lines)
            {
                mLines = lines;
            }

            /// <summary>
            /// Returns the character at the token location.
            /// Converts TAB, CR, LF, etc. to a space.
            /// </summary>
            public char Peek()
            {
                if (Location.X < 0 
                    || Location.Y < 0
                    || Location.Y >= mLines.Length
                    || Location.X >= mLines[Location.Y].Length)
                    return ' ';
                char ch = mLines[Location.Y][Location.X];
                if (ch <= ' ')
                    return ' ';
                return ch;
            }

            /// <summary>
            /// Returns TRUE if we are at the end
            /// </summary>
            public bool AtEnd()
            {
                if (Location.X < 0)
                    return false;
                if (Location.Y >= mLines.Length)
                    return true; // Past end of lines
                if (Location.Y != mLines.Length-1)
                    return false; // Must be on last line
                return Location.X >= mLines[Location.Y].Length;
            }

            /// <summary>
            /// Moves to the next char location.
            /// </summary>
            public void Inc()
            {
                if (Location.Y >= mLines.Length)
                {
                    Location = new TokenLoc();
                    return;
                }
                Location.X++;
                if (Location.X > mLines[Location.Y].Length)
                {
                    Location.X = 0;
                    Location.Y++;
                }
            }
        }

    }
}
