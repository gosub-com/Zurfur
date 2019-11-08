using System;
using System.Collections.Generic;

namespace Gosub.Zurfur.Compiler
{

    enum SymTypeEnum
    {
        Namespace,
        Class, // or Struct, Enum, Interface
        Funcs,
        Func,
        Field
    }

    class SymFile
    {
        public string FileName = "(none)";
        public string TopNamespace = "";
        public Dictionary<string, SymScope> Symbols = new Dictionary<string, SymScope>();
    }

    /// <summary>
    /// Namespace, class, struct, enum, interface
    /// </summary>
    class SymScope
    {
        public string Name = "(none)";
        public SymFile File;
        public SymScope Parent;
        public bool IsRoot => Parent == null;
        public SymTypeEnum Type;
        public Token KeywordToken;
        public Token NameToken;
        //public Dictionary<string, SymScope> Symbols = new Dictionary<string, SymScope>();


        public SymScope(SymTypeEnum type)
        {
            Type = type;
        }

        public override string ToString()
        {
            return Type + ":" + Name + (Parent == null ? "" : " of " + Parent.Name);
        }

    }

}
