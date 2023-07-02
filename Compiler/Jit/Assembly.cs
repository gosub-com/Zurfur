using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Zurfur.Jit
{

    public class Assembly
    {
        public readonly List<string> Comments = new();
        public readonly ConsolidatedList<Symbol> Calls = new();
        public readonly ConsolidatedList<Symbol> Types = new();
        public readonly ConsolidatedList<string> Strings = new();
        public readonly ConsolidatedList<string> Translated = new();
        public readonly Dictionary<string, AsFun> Functions = new();

        public Assembly() 
        {
            Strings.AddOrFind("");
            Translated.AddOrFind("");
        }

        public void Print(StringBuilder sb)
        {
            // Print code
            sb.Append("code:\r\n");
            foreach (var fun in Functions.Values.OrderBy(i => i.Name))
                fun.Print(sb);

            // Print calls
            sb.Append("\r\ncalls:\r\n");
            var index = 0;
            foreach (var call in Calls)
                sb.Append($"    {index++} {call}\r\n");

            // Print types
            sb.Append("\r\ntypes:\r\n");
            index = 0;
            foreach (var type in Types)
                sb.Append($"    {index++} {type}\r\n");

            // Print strings
            sb.Append("\r\nstrings:\r\n");
            index = 0;
            foreach (var str in Strings)
                sb.Append($"    {index++} \"{JsonEncodedText.Encode(str)}\"\r\n");

            sb.Append("\r\ntranslate:\r\n");
            sb.Append("    tbd...\r\n\r\n");
        }
    }

}
