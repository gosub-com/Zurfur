using System;
using System.Collections.Generic;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Lex
{
    /// <summary>
    /// Lexer used for Zurfur and Json
    /// </summary>
    sealed class ScanZurf : Scanner
    {
        // NOTE: >=, >>, and >>= are omitted and handled at parser level.
        public const string MULTI_CHAR_TOKENS = "<< <= == != && || ?? "
            + "+= -= *= /= %= &= |= ~= <<= => -> !== === :: .. ..+ ... ++ -- // ``";
        Dictionary<long, bool> mSpecialSymbols = new Dictionary<long, bool>();
        bool mSpecialSymbolsHas3Chars;

        public ScanZurf()
        {
            SetSpecialSymbols(MULTI_CHAR_TOKENS);
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
