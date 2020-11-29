using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Lex
{
    /// <summary>
    /// Lexer used for Zurfur and Json
    /// </summary>
    sealed class ScanZurf : Scanner
    {
        // NOTE: >=, >>, and >>= are omitted and handled at parser level.
        public const string MULTI_CHAR_TOKENS = "<< <= == != && || += -= *= /= %= &= |= ~= <<= => -> !== === :: .. ... ++ -- ";
        static Regex sFindUrl = new Regex(@"///|//|`|((http|https|file|Http|Https|File|HTTP|HTTPS|FILE)://([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?)");
        Dictionary<long, bool> mSpecialSymbols = new Dictionary<long, bool>();
        bool mSpecialSymbolsHas3Chars;
        bool mTokenizeComments;

        public ScanZurf()
        {
            SetSpecialSymbols(MULTI_CHAR_TOKENS);
            mTokenizeComments = true;
        }

        /// <summary>
        /// Set special symbols that should always be interpreted as a group (e.g. >=, etc.)
        /// Separate each symbol with a space character.  Symbols must not start with a
        /// number or letter.  They must not be longer than 3 characters.
        /// </summary>
        void SetSpecialSymbols(string symbols)
        {
            mSpecialSymbols.Clear();
            mSpecialSymbolsHas3Chars = false;
            var sa = symbols.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var symbol in sa)
            {
                if (symbol.Length > 3)
                    throw new Exception("SetSpecialSymbols: Symbols may not be more than 3 characters long");
                mSpecialSymbolsHas3Chars = mSpecialSymbolsHas3Chars || symbol.Length == 3;
                long code = 0;
                foreach (var ch in symbol)
                    code = code * 65536 + ch;
                mSpecialSymbols[code] = true;
            }
        }

        /// <summary>
        /// Scan a line
        /// </summary>
        public override void ScanLine(string line, List<Token> tokens, MinTern mintern)
        {
            // Build an array of tokens for this line
            int charIndex = 0;
            while (charIndex < line.Length)
                ScanToken(line, ref charIndex, tokens, mintern);
        }

        /// <summary>
        /// Get the next token on the line.  
        /// Returns a "" token if there are none left.
        /// NOTE: The token's LineIndex is set to zero
        /// NOTE: The token is stripped of TABs
        /// </summary>
        void ScanToken(string line, ref int charIndex, List<Token> tokens, MinTern mintern)
        {
            // Skip white space
            while (charIndex < line.Length && char.IsWhiteSpace(line[charIndex]))
                charIndex++;

            // End of line?
            int startIndex = charIndex;
            if (charIndex >= line.Length)
                return;

            // Identifier
            char ch1 = line[charIndex];
            if (char.IsLetter(ch1) || ch1 == '_')
            {
                tokens.Add(ScanIdentifier(line, ref charIndex, startIndex, mintern));
                return;
            }

            // Number
            if (IsAsciiDigit(ch1))
            {
                tokens.Add(ScanNumber(line, ref charIndex, startIndex, mintern));
                return;
            }
            // Quote
            if (ch1 == '\"' || ch1 == '`')
            {
                tokens.Add(ScanString(line, ref charIndex, startIndex, mintern));
                return;
            }
            // Comment
            if (mTokenizeComments && ch1 == '/')
            {
                if (charIndex + 1 < line.Length && line[charIndex + 1] == '/')
                {
                    ScanComment(line, startIndex, tokens, mintern);
                    charIndex = line.Length;
                    return;
                }
            }

            // Special symbols
            if (mSpecialSymbols.Count != 0 && charIndex + 1 < line.Length)
            {
                long code = ch1 * 65536 + line[charIndex + 1];
                if (mSpecialSymbolsHas3Chars
                    && charIndex + 2 < line.Length
                    && mSpecialSymbols.ContainsKey(code * 65536 + line[charIndex + 2]))
                {
                    charIndex += 3;
                    tokens.Add(new Token(mintern[line.Substring(startIndex, 3)], startIndex, 0));
                    return;
                }
                if (mSpecialSymbols.ContainsKey(code))
                {
                    charIndex += 2;
                    tokens.Add(new Token(mintern[line.Substring(startIndex, 2)], startIndex, 0));
                    return;
                }
            }

            // Single character
            tokens.Add(new Token(mintern[line[charIndex++].ToString()], startIndex, 0));
        }

        void ScanComment(string comment, int startIndex, List<Token> tokens, MinTern mintern)
        {
            eTokenType commentType = startIndex + 2 < comment.Length && comment[startIndex + 2] == '/'
                                        ? eTokenType.PublicComment : eTokenType.Comment;

            // Chop up URLs in the comment
            var m = sFindUrl.Match(comment, startIndex);
            while (m.Success && startIndex < comment.Length)
            {
                var pre = comment.Substring(startIndex, m.Index - startIndex).TrimEnd();
                if (pre != "")
                    tokens.Add(new Token(pre, startIndex, 0, commentType));
                tokens.Add(new Token(m.Value, m.Index, 0, commentType));
                startIndex = m.Index + m.Length;
                while (startIndex < comment.Length && char.IsWhiteSpace(comment[startIndex]))
                    startIndex++;
                m = sFindUrl.Match(comment, startIndex);
            }

            comment = mintern[comment.Substring(startIndex).TrimEnd()];
            if (comment != "")
                tokens.Add(new Token(comment, startIndex, 0, commentType));

        }

        Token ScanString(string line, ref int charIndex, int startIndex, MinTern mintern)
        {
            // TBD: This will go away, let the higher level parser
            //      determine when to end the quote (and interpret escapes)
            char endQuote = line[charIndex];
            int endIndex = charIndex + 1;
            while (endIndex < line.Length && line[endIndex] != endQuote)
            {
                endIndex++;
            }
            if (endIndex != line.Length)
                endIndex++; // Skip end quote
            string token = mintern[line.Substring(charIndex, endIndex - charIndex)];
            charIndex = endIndex;
            return new Token(token, startIndex, 0, eTokenType.Quote);
        }

        Token ScanNumber(string line, ref int charIndex, int startIndex, MinTern mintern)
        {
            // Just scoop up everything that could be a number
            int endIndex = charIndex;
            while (endIndex < line.Length && IsAsciiDigit(line[endIndex]))
            {
                endIndex++;

                if (endIndex + 1 < line.Length
                    && (line[endIndex] == 'e' || line[endIndex] == 'E')
                    && (IsAsciiDigit(line[endIndex + 1]) || line[endIndex + 1] == '+' || line[endIndex + 1] == '-'))
                {
                    // Skip exponent
                    endIndex += 2;
                }
                else if (endIndex + 1 < line.Length
                        && line[endIndex] == '.' && IsAsciiDigit(line[endIndex + 1]))
                {
                    // Skip decimal point
                    endIndex++;
                }
                else
                {
                    // Skip letters and '_'
                    while (endIndex < line.Length
                            && (char.IsLetter(line[endIndex]) || line[endIndex] == '_'))
                        endIndex++;
                }
            }

            if (endIndex - charIndex < 0)
                return new Token();
            string number = mintern[line.Substring(charIndex, endIndex - charIndex)];
            charIndex = endIndex;  // Skip token
            return new Token(number, startIndex, 0, eTokenType.Number);
        }

        Token ScanIdentifier(string line, ref int charIndex, int startIndex, MinTern mintern)
        {
            // Hop over identifier
            int endIndex = charIndex;
            while (endIndex < line.Length &&
                    (char.IsLetterOrDigit(line, endIndex) || line[endIndex] == '_'))
                endIndex++;
            string token = mintern[line.Substring(charIndex, endIndex - charIndex)];
            charIndex = endIndex; // Skip token
            return new Token(token, startIndex, 0, eTokenType.Identifier);
        }
    }
}
