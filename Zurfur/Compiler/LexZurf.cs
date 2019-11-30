﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// Lexer used for Zurfur and Json
    /// </summary>
    class LexZurf : Lexer
    {
        static HashSet<char> sStringEscapes = new HashSet<char> { '\"', '\\', '/', 'b', 'f', 'n', 'r', 't', 'u' };
        static Regex sFindUrl = new Regex(@"///|//|`|((http|https|file|Http|Https|File|HTTP|HTTPS|FILE)://([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?)");
        List<Token> mTokenBuffer = new List<Token>();  // Be kind to the GC
        Dictionary<long, bool> mSpecialSymbols = new Dictionary<long, bool>();
        bool mSpecialSymbolsHas3Chars;

        public MinTern Mintern { get; set; } = new MinTern();


        /// <summary>
        /// Set to true to process `//` style comments
        /// </summary>
        public bool TokenizeComments { get; set; }


        /// <summary>
        /// Set special symbols that should always be interpreted as a group (e.g. >=, etc.)
        /// Separate each symbol with a space character.  Symbols must not start with a
        /// number or letter.  They must not be longer than 3 characters.
        /// </summary>
        public void SetSpecialSymbols(string symbols)
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
        protected override Token[] ScanLine(string line, int lineIndex)
        {
            int charIndex = 0;

            // Build an array of tokens for this line
            while (charIndex < line.Length)
                ScanToken(line, ref charIndex, mTokenBuffer);

            foreach (var token in mTokenBuffer)
                token.Y = lineIndex;

            if (mTokenBuffer.Count != 0)
            {
                mTokenBuffer[0].SetBolnByLexerOnly();
                mTokenBuffer[mTokenBuffer.Count - 1].SetEolnByLexerOnly();
            }

            var tokens = mTokenBuffer.ToArray();
            mTokenBuffer.Clear();
            return tokens;
        }

        /// <summary>
        /// Get the next token on the line.  
        /// Returns a "" token if there are none left.
        /// NOTE: The token's LineIndex is set to zero
        /// NOTE: The token is stripped of TABs
        /// </summary>
        void ScanToken(string line, ref int charIndex, List<Token> tokens)
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
                tokens.Add(ScanIdentifier(line, ref charIndex, startIndex));
                return;
            }

            // Number
            if (IsAsciiDigit(ch1))
            {
                tokens.Add(ScanNumber(line, ref charIndex, startIndex));
                return;
            }
            // Quote
            if (ch1 == '\"')
            {
                tokens.Add(ScanString(line, ref charIndex, startIndex));
                return;
            }
            // Comment
            if (TokenizeComments && ch1 == '/')
            {
                if (charIndex + 1 < line.Length && line[charIndex + 1] == '/')
                {
                    ScanComment(line, startIndex, tokens);
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
                    tokens.Add(new Token(Mintern[line.Substring(startIndex, 3)], startIndex, 0));
                    return;
                }
                if (mSpecialSymbols.ContainsKey(code))
                {
                    charIndex += 2;
                    tokens.Add(new Token(Mintern[line.Substring(startIndex, 2)], startIndex, 0));
                    return;
                }
            }

            // Single character
            tokens.Add(new Token(Mintern[line[charIndex++].ToString()], startIndex, 0));
        }

        private void ScanComment(string comment, int startIndex, List<Token> tokens)
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

            comment = comment.Substring(startIndex).TrimEnd();
            if (comment != "")
                tokens.Add(new Token(comment, startIndex, 0, commentType));

        }

        Token ScanString(string line, ref int charIndex, int startIndex)
        {
            int endIndex = charIndex + 1;
            while (endIndex < line.Length && line[endIndex] != '\"')
            {
                if (line[endIndex] == '\\')
                {
                    // TBD: Handle /u
                    if (endIndex + 1 < line.Length && sStringEscapes.Contains(line[endIndex + 1]))
                        endIndex++;
                }
                endIndex++;
            }
            if (endIndex != line.Length)
                endIndex++; // Skip end quote
            string token = Mintern[line.Substring(charIndex, endIndex - charIndex)];
            charIndex = endIndex;
            return new Token(token, startIndex, 0, eTokenType.Quote);
        }

        protected static bool IsAsciiDigit(char ch)
        {
            return ch >= '0' && ch <= '9';
        }

        private Token ScanNumber(string line, ref int charIndex, int startIndex)
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
                return Token.Empty;
            string number = Mintern[line.Substring(charIndex, endIndex - charIndex)];
            charIndex = endIndex;  // Skip token
            return new Token(number, startIndex, 0, eTokenType.Number);
        }

        private Token ScanIdentifier(string line, ref int charIndex, int startIndex)
        {
            // Hop over identifier
            int endIndex = charIndex;
            while (endIndex < line.Length &&
                    (char.IsLetterOrDigit(line, endIndex) || line[endIndex] == '_'))
                endIndex++;
            string token = Mintern[line.Substring(charIndex, endIndex - charIndex)];
            charIndex = endIndex; // Skip token
            return new Token(token, startIndex, 0, eTokenType.Identifier);
        }
    }
}
