using System;
using System.Collections.Generic;

namespace Zurfur.Lex
{
    /// <summary>
    /// Minimalist string intern system.  Note that string.Intern holds
    /// on to junk that GC can never reclaim.  This doesn't.  Using a
    /// dictionary would build a big data structure.  This uses a small
    /// amount of memory to combine strings, and gets 98% of the repeats.
    /// </summary>
    public class MinTern
    {

        LRUCache<string, string> mCache = new(512);

        /// <summary>
        /// Returns the same string, except that it is either interned
        /// by the system, or intened by this data structur
        /// </summary>
        public string this[string newStr]
        {
            get
            {
                if (newStr.Length == 0)
                    return "";
                var isInterned = string.IsInterned(newStr);
                if (isInterned != null)
                    return isInterned;
                
                var s = mCache.Get(newStr);
                if (s != null)
                    return s;

                mCache.Add(newStr, newStr);
                return newStr;
            }
        }

    }
}
