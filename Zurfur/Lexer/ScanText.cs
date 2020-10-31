using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gosub.Zurfur.Lex
{
    /// <summary>
    /// Default scanner for a generic text file.  Just split it by white space, nothing else.
    /// </summary>
    public sealed class ScanText : Scanner
    {
        public static readonly ScanText Empty = new ScanText();

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
            if (charIndex >= line.Length)
                return;

            // Until white space
            int startIndex = charIndex;
            while (charIndex < line.Length && !char.IsWhiteSpace(line[charIndex]))
                charIndex++;
            tokens.Add(new Token(mintern[line.Substring(startIndex, charIndex - startIndex)], startIndex, 0));
        }

    }
}
