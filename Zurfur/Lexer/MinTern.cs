using System;
using System.Collections.Generic;
using System.Text;

namespace Gosub.Zurfur
{
    /// <summary>
    /// Minimalist string intern system.  Note that string.Intern holds
    /// on to junk that GC can never reclaim.  This doesn't.  Using a
    /// dictionary would build a big data structure.  This uses a small
    /// amount of memory to combine strings, and gets 98% of the repeats.
    /// </summary>
    public class MinTern
    {
        const int WIDTH = 97;	// Number of different hash values (should be prime)
        const int DEPTH = 4;	// Number of symbols per has value
        string [][]mIntern;

        /// <summary>
        /// Returns the same string, except that it is either interned
        /// by the system, or intened by this data structur
        /// </summary>
        public string this[string newStr]
        {
            get
            {
                if (newStr == null)
                    return null;
                if (newStr.Length == 0)
                    return "";
                var isInterned = string.IsInterned(newStr);
                if (isInterned != null)
                    return isInterned;

                // Search table for newStr
                int hash = (int)((uint)newStr.GetHashCode() % WIDTH);
                string []table = mIntern[(int)((uint)newStr.GetHashCode() % WIDTH)];
                for (int i = 0; i < table.Length; i++)
                {
                    string tableString = table[i];
                    if (newStr == tableString)
                    {
                        // Shift strings, and re-insert at beginning
                        for (int ir = i-1; ir >= 0;  ir--)
                            table[ir+1] = table[ir];
                        table[0] = tableString;
                        return tableString;
                    }
                }

                // Insert new string at beginning of table
                string result = newStr;
                for (int i = 0;  i < table.Length && newStr != null;  i++)
                {
                    string temp = table[i];
                    table[i] = newStr;
                    newStr = temp;
                }
                return result;
            }
        }

        /// <summary>
        /// Clear the string in the table
        /// </summary>
        public void Clear()
        {
            foreach (string []list in mIntern)
                for (int i = 0;  i < list.Length;  i++)
                    list[i] = null;
        }

        /// <summary>
        /// Create a MinTern to combine strings.
        /// </summary>
        public MinTern()
        {
            mIntern = new string[WIDTH][];
            for (int i = 0; i < mIntern.Length; i++)
                mIntern[i] = new string[DEPTH];
        }

    }
}
