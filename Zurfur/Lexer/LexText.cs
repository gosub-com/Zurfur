using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gosub.Zurfur.Lex
{
    /// <summary>
    /// Default lexer for a generic text file.  Just split it by white space, nothing else.
    /// </summary>
    class LexText : Lexer
    {
        List<Token> mTokenBuffer = new List<Token>();  // Be kind to the GC
        public MinTern Mintern { get; set; }

        /// <summary>
        /// Scan a line
        /// </summary>
        protected override Token[] ScanLine(string line, int lineIndex)
        {
            if (Mintern == null)
                Mintern = new MinTern();

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
            if (charIndex >= line.Length)
                return;

            // Until white space
            int startIndex = charIndex;
            while (charIndex < line.Length && !char.IsWhiteSpace(line[charIndex]))
                charIndex++;
            tokens.Add(new Token(Mintern[line.Substring(startIndex, charIndex - startIndex)], startIndex, 0));
        }



    }
}
