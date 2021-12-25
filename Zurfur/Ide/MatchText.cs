using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur
{
    class MatchText
    {
        TokenLoc mStartSearchLoc;
        TokenLoc mPreviousSearchLoc = new TokenLoc(-1, -1);

        public bool MatchWholeWord;
        public bool MatchCase;

        public void ResetLocation()
        {
            mPreviousSearchLoc = new TokenLoc(-1, -1);
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
            if (MatchWholeWord)
            {
                text.Location.X--;
                if (!IsWordSeparator(text.Peek()))
                    return false;
                text.Location.X++;
            }

            // Scan until we find a match or find the end
            bool matchCase = MatchCase;
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
            if (MatchWholeWord && found)
                return IsWordSeparator(text.Peek());

            return found;
        }

        public int CountMatches(string []text, string search)
        {
            if (search == "")
                return 0;

            var findScan = new Scanner(new string[] {search});
            var textScan = new Scanner(text);
            var count = 0;
            var tokenLoc = new TokenLoc();
            do {
                if (Match(textScan, findScan, ref tokenLoc))
                {
                    count++;
                    textScan.Location = tokenLoc;
                }
                else
                {
                    textScan.Inc();
                }
            } while (textScan.Location != new TokenLoc());
            return count;
        }

        /// <summary>
        /// Find next, returns (found, pastEnd)
        /// </summary>
        public (bool, bool) FindNextAndSelect(TextEditor editor, string search, bool skipSelectedText)
        {
            // Find start location (cursor or beginning of selected text)
            TokenLoc start = editor.CursorLoc;
            if (editor.HasSel())
                start = editor.SelStart;

            // Start new search?
            if (mPreviousSearchLoc != start)
            {
                mPreviousSearchLoc = new TokenLoc(-1, -1);
                mStartSearchLoc = start;
            }

            // Create scanners
            var find = new Scanner(new string[] { search });
            var text = new Scanner(editor.Lexer.GetText());
            text.Location = start;

            // Skip first char to move past previous search
            if (editor.HasSel() && skipSelectedText)
                text.Inc();

            // Scan for a match
            var matchEnd = new TokenLoc();
            var firstChar = true;
            var pastEnd = false;
            while (true)
            {
                // Past end of previous search?
                if (!firstChar && text.Location == start)
                {
                    return (false, pastEnd);
                }

                if (!firstChar && text.Location == mStartSearchLoc)
                    pastEnd = true;

                // Found match?
                if (Match(text, find, ref matchEnd))
                {
                    editor.SelSet(text.Location, matchEnd);
                    editor.CursorLoc = matchEnd;
                    mPreviousSearchLoc = text.Location;
                    return (true, pastEnd);
                }
                text.Inc();
                firstChar = false;
            }
        }

        /// <summary>
        /// Helper class to scan text for matches
        /// </summary>
        class Scanner
        {
            public TokenLoc Location;
            string[] mLines;

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
                if (Location.Y != mLines.Length - 1)
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
