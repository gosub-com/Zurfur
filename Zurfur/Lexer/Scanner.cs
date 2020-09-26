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
        /// Logic to tokenize text based on the language
        /// </summary>
        public abstract Token[] ScanLine(string line, int lineIndex);

        /// <summary>
        /// This must be overridden by the inheriting class to create the same
        /// type of scanner and clone anything that is mutable.
        /// </summary>
        public abstract Scanner Clone();

        public static bool IsAsciiDigit(char ch)
        {
            return ch >= '0' && ch <= '9';
        }

    }
}
