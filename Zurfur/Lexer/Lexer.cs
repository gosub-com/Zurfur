using System;
using System.Collections.Generic;



namespace Gosub.Zurfur
{
    /// <summary>
    /// Lexical analyzer - scan and separate tokens in a file.
    /// </summary>
    public class Lexer
    {
        // Strings and tokens buffer
        List<string> mLines = new List<string>();
        List<List<Token>> mTokens = new List<List<Token>>();
        MinTern mMintern = new MinTern();

        /// <summary>
        /// Create an empty lexer
        /// </summary>
        public Lexer()
        {
            ScanLines(new string[] { "" });
        }
        
        /// <summary>
        /// Create a new lexer from the given text
        /// </summary>
        public Lexer(string[] lines)
        {
            ScanLines(lines);
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
            start.Line = Bound(start.Line, 0, mLines.Count-1);
            start.Char = Bound(start.Char, 0, mLines[start.Line].Length);
            end.Line = Bound(end.Line, 0, mLines.Count-1);
            end.Char = Bound(end.Char, 0, mLines[end.Line].Length);
            if (end.Line < start.Line)
                end.Line = start.Line;
            if (start.Line == end.Line && end.Char < start.Char)
                end.Char = start.Char;

            int startIndex = start.Char;
            int endIndex = end.Char;

            if (start.Line == end.Line && startIndex >= endIndex)
                return new string[0];

            if (start.Line == end.Line)
            {
                return new string[] { mLines[start.Line].Substring(startIndex, endIndex-startIndex) };
            }

            // Break up the first and last line at the start position
            string []lines = new string[end.Line-start.Line+1];
            lines[0] = mLines[start.Line].Substring(startIndex);
            for (int i = 1; i < lines.Length-1; i++)
                lines[i] = mLines[start.Line+i];
            lines[end.Line-start.Line] = mLines[end.Line].Substring(0, endIndex);
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
            start.Line = Bound(start.Line, 0, mLines.Count-1);
            start.Char = Bound(start.Char, 0, mLines[start.Line].Length);
            end.Line = Bound(end.Line, 0, mLines.Count-1);
            end.Char = Bound(end.Char, 0, mLines[end.Line].Length);
            if (end.Line < start.Line)
                end.Line = start.Line;
            if (start.Line == end.Line && end.Char < start.Char)
                end.Char = start.Char;

            int startIndex = start.Char;
            int endIndex = end.Char;

            // Adjust first line
            if (start.Line != end.Line || startIndex != endIndex)
                mLines[start.Line] = mLines[start.Line].Substring(0, startIndex)
                                    + mLines[end.Line].Substring(endIndex);

            // Remove unused lines
            if (start.Line != end.Line)
            {
                mLines.RemoveRange(start.Line+1, end.Line-start.Line);
                mTokens.RemoveRange(start.Line+1, end.Line-start.Line);
            }

            // Start and end are the same
            end.Line = start.Line;
            endIndex = startIndex;

            // Insert new text
            if (replacementText != null && replacementText.Length != 0)
            {
                // Break up the first line at the start position
                string startStr = mLines[start.Line].Substring(0, startIndex);
                string endStr = mLines[start.Line].Substring(startIndex);

                if (replacementText.Length <= 1)
                {
                    mLines[start.Line] = startStr + replacementText[0] + endStr;
                    endIndex = startStr.Length + replacementText[0].Length;
                }
                else
                {
                    // Insert new lines
                    mLines[start.Line] = startStr + replacementText[0];
                    for (int i = 1; i < replacementText.Length; i++)
                    {
                        mLines.Insert(start.Line+i, replacementText[i]);
                        mTokens.Insert(start.Line+i, new List<Token>());
                    }
                    end.Line = start.Line + replacementText.Length-1;
                    endIndex = replacementText[replacementText.Length-1].Length;
                    mLines[end.Line] += endStr;
                }
            }

            // Re-scan the updated text lines
            for (int i = start.Line; i <= end.Line; i++)
                mTokens[i] = ScanLine(mLines[i], i);

            // Re-adjust token line positions
            for (int i = start.Line; i < mTokens.Count; i++)
                foreach (Token token in mTokens[i])
                    token.Line = i;

            // Calculate end of inserted text
            end.Char = endIndex;
            return end;
        }

        static HashSet<char> sEscaped = new HashSet<char> { '\\', '\"', '\'', 't', 'r', 'n' };

        /// <summary>
        /// Get the next token on the line.  
        /// Returns a "" token if there are none left.
        /// NOTE: The token's LineIndex is set to zero
        /// NOTE: The token is stripped of TABs
        /// </summary>
        Token ScanToken(string line, ref int charIndex)
        {
            // Skip white space
            while (charIndex < line.Length && char.IsWhiteSpace(line[charIndex]))
                charIndex++;

            // End of line?
            int startIndex = charIndex;
            if (charIndex >= line.Length)
                return new Token("", 0, startIndex);

            // Identifier
            char ch1 = line[charIndex];
            if (char.IsLetter(ch1))
            {
                // Hop over identifier
                int endIndex = charIndex;
                while (endIndex < line.Length && 
                        (char.IsLetterOrDigit(line, endIndex) || line[endIndex] == '_'))
                    endIndex++;
                string token = mMintern[line.Substring(charIndex, endIndex - charIndex)];
                charIndex = endIndex; // Skip token
                return new Token(token, 0, startIndex, eTokenType.Identifier);
            }
            // Number
            if (char.IsDigit(ch1) && ch1 <= '9')
            {
                // Hop over number
                int endIndex = charIndex;
                while (endIndex < line.Length && (char.IsLetterOrDigit(line, endIndex) && line[endIndex] < '~'
                                                    || line[endIndex] == '.'))
                {
                    var ch = line[endIndex];
                    endIndex++;
                    if ((ch == 'e' || ch == 'E') && endIndex < line.Length
                            && (line[endIndex] == '+' || line[endIndex] == '-'))
                        endIndex++;
                }
                string token = mMintern[line.Substring(charIndex, endIndex - charIndex)];
                charIndex = endIndex;  // Skip token
                return new Token(token, 0, startIndex, eTokenType.Number);
            }
            // Quote
            if (ch1 == '\"')
            {
                int endIndex = charIndex + 1;
                while (endIndex < line.Length && line[endIndex] != '\"')
                {
                    if (line[endIndex] == '\\')
                    {
                        // TBD: Handle /x and /u
                        if (endIndex + 1 < line.Length && sEscaped.Contains(line[endIndex + 1]))
                            endIndex++;
                    }
                    endIndex++;
                }
                if (endIndex != line.Length)
                    endIndex++; // Skip end quote
                string token = mMintern[line.Substring(charIndex, endIndex - charIndex)];
                charIndex = endIndex;
                return new Token(token, 0, startIndex, eTokenType.Quote);
            }
            // Comment
            char ch2 = charIndex+1 < line.Length ? line[charIndex+1] : (char)0;
            if (ch1 == '/' && ch2 == '/')
            {
                int endIndex = charIndex;
                while (endIndex < line.Length  && (line[endIndex] != '\n' ||  line[endIndex] != '\r'))
                    endIndex++;
                string token = mMintern[line.Substring(charIndex, endIndex - charIndex)];
                charIndex = endIndex;
                return new Token(token, 0, startIndex, eTokenType.Comment);
            }

            // Two char tokens
            if (   ch1 == ':' && ch2 == ':'
                || ch1 == '+' && ch2 == '+'
                || ch1 == '-' && ch2 == '-'
                || ch1 == '*' && ch2 == '*'
                || ch1 == '<' && ch2 == '<'
                || ch1 == '>' && ch2 == '>'
                || ch1 == '<' && ch2 == '='
                || ch1 == '>' && ch2 == '='
                || ch1 == '=' && ch2 == '='
                || ch1 == '!' && ch2 == '='
                || ch1 == '&' && ch2 == '&'
                || ch1 == '|' && ch2 == '|'
                || ch1 == '+' && ch2 == '='
                || ch1 == '-' && ch2 == '='
                || ch1 == '*' && ch2 == '='
                || ch1 == '/' && ch2 == '='
                || ch1 == '%' && ch2 == '='
                || ch1 == '&' && ch2 == '='
                || ch1 == '|' && ch2 == '='
                || ch1 == '^' && ch2 == '='
                || ch1 == '=' && ch2 == '>')
            {
                charIndex += 2;
                return new Token(mMintern[line.Substring(startIndex, 2)], 0, startIndex);
            }

            // Three char tokens
            char ch3 = charIndex+2 < line.Length ? line[charIndex+2] : (char)0;
            if (   ch1 == '=' && ch2 == '=' && ch3 == '='
                || ch1 == '<' && ch2 == '<' && ch3 == '='
                || ch1 == '>' && ch2 == '>' && ch3 == '=')
            {
                charIndex += 3;
                return new Token(mMintern[line.Substring(startIndex, 3)], 0, startIndex);
            }

            // Single character
            return new Token(mMintern[line[charIndex++].ToString()], 0, startIndex);
        }

        /// <summary>
        /// Scan a line
        /// </summary>
        List<Token> ScanLine(string line, int lineIndex)
        {
            int charIndex = 0;

            // Build an array of tokens for this line
            List<Token> tokens = new List<Token>();
            Token token;
            do
            {
                token = ScanToken(line, ref charIndex);
                token.Line = lineIndex;

                // Add everything except LF to this line
                if (token.Name.Length != 0)
                    tokens.Add(token);
            } while (token.Name.Length != 0);

            // Copy tokens to array
            tokens.TrimExcess();
            return tokens;
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
        public IEnumerator<Token> GetEnumerator()
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
        /// Enumerate tokens in the lexer
        /// </summary>
        public struct Enumerator:IEnumerator<Token>
        {
            static List<Token>	sEmpty = new List<Token>();

            Lexer		mLexer;
            int			mIndexLine;
            int			mIndexToken;
            Token		mCurrent;
            List<Token>	mCurrentLine;

            /// <summary>
            /// Enumerate all tokens
            /// </summary>
            public Enumerator(Lexer lexer)
            {
                mLexer = lexer;
                mIndexLine = 0;
                mIndexToken = 0;
                mCurrentLine = mLexer.mTokens.Count <= 0 ? sEmpty : mLexer.mTokens[0];
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
                mCurrentLine = mLexer.mTokens.Count <= mIndexLine ? sEmpty : mLexer.mTokens[mIndexLine];
                mCurrent = null;
            }

            public IEnumerator<Token> GetEnumerator() { return this; }
            public void Dispose() { }
            public Token Current { get { return mCurrent; } }			
            object System.Collections.IEnumerator.Current { get { return mCurrent; } }
            
            public void Reset()
            {
                throw new NotSupportedException("Reset on lexer enumerator is not supported");
            }

            public bool MoveNext()
            {
                // More tokens on this line?
                if (mIndexToken < mCurrentLine.Count)
                {
                    mCurrent = mCurrentLine[mIndexToken++];
                    return true;
                }
                // Move to next non-empty line
                mIndexToken = 0;
                do
                {
                    mIndexLine++;
                } while (mIndexLine < mLexer.mTokens.Count && mLexer.mTokens[mIndexLine].Count == 0);
                
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
        }
    }
}
