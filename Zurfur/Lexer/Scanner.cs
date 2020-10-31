using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gosub.Zurfur.Lex
{
    /// <summary>
    /// Base class for all scanners
    /// </summary>
    public abstract class Scanner
    {
        /// <summary>
        /// Logic to tokenize text based on the language. 
        /// </summary>
        public abstract void ScanLine(string line, List<Token> tokens, MinTern mintern );

        /// <summary>
        /// Conveninece function
        /// </summary>
        /// <param name="ch"></param>
        /// <returns></returns>
        public static bool IsAsciiDigit(char ch)
        {
            return ch >= '0' && ch <= '9';
        }

    }
}
