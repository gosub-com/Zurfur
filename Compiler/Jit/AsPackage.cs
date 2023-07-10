using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Zurfur.Jit
{
    /// <summary>
    /// Assembly package
    /// </summary>
    public class AsPackage
    {
        public readonly List<string> Comments = new();
        public readonly ConsolidatedList<Symbol> Calls = new();
        public readonly ConsolidatedList<Symbol> Types = new();
        public readonly ConsolidatedList<string> Strings = new();
        public readonly ConsolidatedList<string> Translated = new();
        public readonly Dictionary<string, AsFun> Functions = new();

        public AsPackage() 
        {
            Strings.AddOrFind("");
            Translated.AddOrFind("");
        }

        public void Print(List<string> sb)
        {
            // Print code
            sb.Add("code:");
            var tracer = new AsTracer();
            foreach (var fun in Functions.Values.OrderBy(i => i.Name))
                fun.Print(sb, tracer);

            // Print calls
            sb.Add("");
            sb.Add("calls:");
            var index = 0;
            foreach (var call in Calls)
                sb.Add($"    {index++} {call}");

            // Print types
            sb.Add("");
            sb.Add("types:");
            index = 0;
            foreach (var type in Types)
                sb.Add($"    {index++} {type}");

            // Print strings
            sb.Add("");
            sb.Add("strings:");
            index = 0;
            foreach (var str in Strings)
                sb.Add($"    {index++} \"{JsonEncodedText.Encode(str)}\"");

            sb.Add("");
            sb.Add("translate:");
            sb.Add("    tbd...");
        }
    }

}
