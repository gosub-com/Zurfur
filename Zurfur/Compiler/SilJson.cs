using System;
using System.Collections.Generic;

namespace Gosub.Zurfur.Compiler
{

    /// <summary>
    /// The json is flattened so that classes, methods, and constants 
    /// all at the top level, but fields are contained within a class.
    /// NOTE: Still TBD whether to flatten or use symbol table layout
    /// </summary>
    class SilJsonPackage
    {
        public SymPackage Package; // Serialize info, but not symbols

        // Header file
        public Dictionary<string, SilJsonConst> Constants = new Dictionary<string, SilJsonConst>();
        public Dictionary<string, SilJsonType> Types = new Dictionary<string, SilJsonType>();

        // Populated for header file, blank for object file in which case `Code` has it
        public Dictionary<string, SilJsonFunc> Funcs = new Dictionary<string, SilJsonFunc>();

        // Object file (not needed by header file)
        public Dictionary<string, string> Strings = new Dictionary<string, string>();
        public Dictionary<string, SilJsonTrString> TrStrings = new Dictionary<string, SilJsonTrString>();
        public Dictionary<string, SilJsonFunc> Code = new Dictionary<string, SilJsonFunc>();
    }

    class SilJsonConst
    {

    }

    class SilJsonType
    {
        public string Name = "";
        public List<SilGenJsonField> Fields = new List<SilGenJsonField>();
    }

    class SilGenJsonField
    {
        public string Name = "";
        public string Type = "";

        public SilGenJsonField(string name, string type)
        {
            Name = name;
            Type = type;
        }
    }

    class SilJsonTrString
    {
        public string Name = "";
        public string Context = "";
    }

    class SilJsonFunc
    {
        public string Name = "";
    }


}
