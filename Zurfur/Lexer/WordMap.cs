using System;
using System.Collections.Generic;

namespace Gosub.Zurfur.Lex
{
    class WordMap<V> : Dictionary<string, V>
    {
        public bool Contains(string key) { return ContainsKey(key); }
        public WordMap() { }
        public WordMap(string words, bool addEmptyString = false)
        {
            AddWords(words, default(V));
            if (addEmptyString)
                this[""] = default(V);
        }
        public void AddWords(string words, V value)
        {
            foreach (string s in words.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var interned = string.Intern(s);
                this[interned] = value;
            }
        }
    }
}
