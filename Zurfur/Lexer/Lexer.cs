using System;
using System.Collections.Generic;

namespace Gosub.Zurfur.Lex
{
    /// <summary>
    /// Lexical analyzer - scan and separate tokens in a file.
    /// Provide services for the editor to modify the text and re-tokenize
    /// whenever it changes.  Tokens never cross line boundaries.
    /// </summary>
    abstract public class Lexer
    {
        // Strings and tokens buffer
        List<string> mLines = new List<string>();
        List<Token[]> mTokens = new List<Token[]>();

        /// <summary>
        /// Logic to tokenize text based on the language
        /// </summary>
        protected abstract Token[] ScanLine(string line, int lineIndex);

        /// <summary>
        /// This must be overridden by the inheriting class to create the same
        /// type of lexer and clone anything that is mutable.
        /// </summary>
        protected abstract Lexer CloneInternal();

        /// <summary>
        /// Create an empty lexer
        /// </summary>
        public Lexer()
        {
            ScanLines(new string[] { "" });
        }

        /// <summary>
        /// Clone the lexer, but not the tokens are shared.  Since the
        /// mutable parts are cloned, the copies should be thread safe.
        /// </summary>
        public Lexer Clone()
        {
            var lex = CloneInternal();
            lex.mLines = new List<string>(mLines);
            lex.mTokens = new List<Token[]>(mTokens);
            return lex;
        }


        public static bool IsAsciiDigit(char ch)
        {
            return ch >= '0' && ch <= '9';
        }


        /// <summary>
        /// Returns the number of lines of text
        /// </summary>
        public int LineCount { get { return mLines.Count; } }

        /// <summary>
        /// Returns a line of text
        /// </summary>
        public string GetLine(int index) { return mLines[index]; }

        /// <summary>
        /// Returns v, bounded by min and max (or min if min >= max)
        /// </summary>
        int Bound(int v, int min, int max)
        {
            return Math.Max(Math.Min(v, max), min);
        }

        /// <summary>
        /// Gets a section of text.
        /// </summary>
        public string[] GetText(TokenLoc start, TokenLoc end)
        {
            // Bounds check parameters
            start.Y = Bound(start.Y, 0, mLines.Count-1);
            start.X = Bound(start.X, 0, mLines[start.Y].Length);
            end.Y = Bound(end.Y, 0, mLines.Count-1);
            end.X = Bound(end.X, 0, mLines[end.Y].Length);
            if (end.Y < start.Y)
                end.Y = start.Y;
            if (start.Y == end.Y && end.X < start.X)
                end.X = start.X;

            int startIndex = start.X;
            int endIndex = end.X;

            if (start.Y == end.Y && startIndex >= endIndex)
                return new string[0];

            if (start.Y == end.Y)
            {
                return new string[] { mLines[start.Y].Substring(startIndex, endIndex-startIndex) };
            }

            // Break up the first and last line at the start position
            string []lines = new string[end.Y-start.Y+1];
            lines[0] = mLines[start.Y].Substring(startIndex);
            for (int i = 1; i < lines.Length-1; i++)
                lines[i] = mLines[start.Y+i];
            lines[end.Y-start.Y] = mLines[end.Y].Substring(0, endIndex);
            return lines;
        }

        /// <summary>
        /// Returns a copy of all the text
        /// </summary>
        public string[] GetText()
        {
            return mLines.ToArray();
        }

        /// <summary>
        /// Replace a section of text.  This function is used to insert, 
        /// delete, and change a section of text.  It will re-analyze the
        /// text (replacing all tokens on all lines that were changed)
        /// and updating the positions of the tokens.
        /// RETURNS: The new end location of the inserted text
        /// </summary>
        public TokenLoc ReplaceText(string[] replacementText,
                         TokenLoc start, TokenLoc end)
        {
            // Bounds check parameters
            start.Y = Bound(start.Y, 0, mLines.Count-1);
            start.X = Bound(start.X, 0, mLines[start.Y].Length);
            end.Y = Bound(end.Y, 0, mLines.Count-1);
            end.X = Bound(end.X, 0, mLines[end.Y].Length);
            if (end.Y < start.Y)
                end.Y = start.Y;
            if (start.Y == end.Y && end.X < start.X)
                end.X = start.X;

            int startIndex = start.X;
            int endIndex = end.X;

            // Adjust first line
            if (start.Y != end.Y || startIndex != endIndex)
                mLines[start.Y] = mLines[start.Y].Substring(0, startIndex)
                                    + mLines[end.Y].Substring(endIndex);

            // Remove unused lines
            if (start.Y != end.Y)
            {
                mLines.RemoveRange(start.Y+1, end.Y-start.Y);
                mTokens.RemoveRange(start.Y+1, end.Y-start.Y);
            }

            // Start and end are the same
            end.Y = start.Y;
            endIndex = startIndex;

            // Insert new text
            if (replacementText != null && replacementText.Length != 0)
            {
                // Break up the first line at the start position
                string startStr = mLines[start.Y].Substring(0, startIndex);
                string endStr = mLines[start.Y].Substring(startIndex);

                if (replacementText.Length <= 1)
                {
                    mLines[start.Y] = startStr + replacementText[0] + endStr;
                    endIndex = startStr.Length + replacementText[0].Length;
                }
                else
                {
                    // Insert new lines
                    mLines[start.Y] = startStr + replacementText[0];
                    for (int i = 1; i < replacementText.Length; i++)
                    {
                        mLines.Insert(start.Y+i, replacementText[i]);
                        mTokens.Insert(start.Y+i, Array.Empty<Token>());
                    }
                    end.Y = start.Y + replacementText.Length-1;
                    endIndex = replacementText[replacementText.Length-1].Length;
                    mLines[end.Y] += endStr;
                }
            }

            // Re-scan the updated text lines
            for (int i = start.Y; i <= end.Y; i++)
                mTokens[i] = ScanLine(mLines[i], i);

            // Re-adjust token line positions
            for (int i = start.Y; i < mTokens.Count; i++)
                foreach (Token token in mTokens[i])
                    token.Y = i;

            // Calculate end of inserted text
            end.X = endIndex;
            return end;
        }

        /// <summary>
        /// Scan lines of text from an array, completely 
        /// replacing all text in Lines, and all tokens
        /// </summary>
        public void ScanLines(string[] lines)
        {
            mTokens.Clear();

            // Must have at least one line of text
            if (lines.Length == 0)
                lines = new string[1] { "" };
            mLines = new List<string>(lines.Length);

            // For each line
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                mLines.Add(lines[lineIndex]);
                mTokens.Add(ScanLine(lines[lineIndex], lineIndex));
            }
        }

        /// <summary>
        /// Iterator to return all tokens
        /// </summary>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        /// <summary>
        /// Iterate through all tokens, starting at startLine
        /// </summary>
        public Enumerator GetEnumeratorStartAtLine(int startLine)
        {
            return new Enumerator(this, startLine);
        }

        /// <summary>
        /// Enumerate tokens in the lexer.  Call MoveNextLine to skip to next line
        /// </summary>
        public struct Enumerator:IEnumerator<Token>
        {
            Lexer		mLexer;
            int			mIndexLine;
            int			mIndexToken;
            Token		mCurrent;
            Token       []mCurrentLine;

            public IEnumerator<Token> GetEnumerator() { return this; }
            public void Dispose() { }
            public Token Current { get { return mCurrent; } }
            public int CurrentLineTokenCount { get { return mCurrentLine.Length; } }
            object System.Collections.IEnumerator.Current { get { return mCurrent; } }

            /// <summary>
            /// Enumerate all tokens
            /// </summary>
            public Enumerator(Lexer lexer)
            {
                mLexer = lexer;
                mIndexLine = 0;
                mIndexToken = 0;
                mCurrentLine = mLexer.mTokens.Count <= 0 ? Array.Empty<Token>() : mLexer.mTokens[0];
                mCurrent = null;
            }

            /// <summary>
            /// Enumerate all tokens, starting at startLine
            /// </summary>
            public Enumerator(Lexer lexer, int startLine)
            {
                mLexer = lexer;
                mIndexLine = Math.Max(0, startLine);
                mIndexToken = 0;
                mCurrentLine = mLexer.mTokens.Count <= mIndexLine ? Array.Empty<Token>() : mLexer.mTokens[mIndexLine];
                mCurrent = null;
            }

            /// <summary>
            /// Returns the next token on the line, or "" if at end of line
            /// </summary>
            public Token PeekOnLine(int i = 0)
            {
                if (mIndexToken+i < mCurrentLine.Length)
                    return mCurrentLine[mIndexToken+i];
                return Token.Empty;
            }

            public void Reset()
            {
                throw new NotSupportedException("Reset on lexer enumerator is not supported");
            }

            /// <summary>
            /// Move to next token, skipping blank lines
            /// </summary>
            public bool MoveNext()
            {
                // More tokens on this line?
                if (mIndexToken < mCurrentLine.Length)
                {
                    mCurrent = mCurrentLine[mIndexToken++];
                    return true;
                }
                // Move to next non-empty line
                mIndexToken = 0;
                do
                {
                    mIndexLine++;
                } while (mIndexLine < mLexer.mTokens.Count && mLexer.mTokens[mIndexLine].Length == 0);
                
                // Return next token
                if (mIndexLine < mLexer.mTokens.Count)
                {
                    mCurrentLine = mLexer.mTokens[mIndexLine];
                    mCurrent = mCurrentLine[mIndexToken++];
                    return true;
                }
                mCurrent = null;
                return false;
            }

            /// <summary>
            /// Move to next line, blank lines return empty token
            /// </summary>
            public bool MoveNextLine()
            {
                mIndexToken = 0;
                if (mIndexLine < mLexer.mTokens.Count)
                {
                    mCurrentLine = mLexer.mTokens[mIndexLine++];
                    mCurrent = mCurrentLine.Length == 0 ? Token.Empty : mCurrentLine[mIndexToken++];
                    return true;
                }
                mCurrent = null;
                return false;
            }

        }
    }
}
