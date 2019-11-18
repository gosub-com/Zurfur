using System;
using System.Collections.Generic;

namespace Gosub.Zurfur.Compiler
{

    enum SymbolTypeEnum
    {
        Namespace,
        Type,
        Field,
        Funcs,
        Func,
        TypeArg
    }

    class SymPackage
    {
        public string PackageName = "";
        public PackageInfo PackageInfo = new PackageInfo();
        public CompilerInfo CompilerInfo = new CompilerInfo();

        public Symbol Symbols = new Symbol(SymbolTypeEnum.Namespace, new Token());
    }

    class PackageInfo
    {
        public DateTime BuildDate = DateTime.Now;
        public string Title = "";
        public string Description = "";
        public string Company = "";
        public string Product = "";
        public string Copyright = "";
        public string Version = "";
    }

    class CompilerInfo
    {
        public string Name = "Zurfur";
        public string Version = "0.0.0";

        /// <summary>
        /// The platform is "ZSIL" for a package ready for public distribution.
        /// Everything else is tied to a specific compiler, buildname, 
        /// and all build options and constants.
        /// </summary>
        public string Platform = "";
        public int PlatformVersion;
        public string BuildName = ""; // Debug, Release, etc.
        public Dictionary<string, string> BuildOptions = new Dictionary<string, string>();
        public Dictionary<string, string> BuildConstants = new Dictionary<string, string>();
    }

    class SymFile
    {
        public string FileName = "";
    }

    class Symbol
    {
        public readonly SymbolTypeEnum Type;
        public readonly Token Name = Token.Empty;

        public Symbol Parent;
        public string Comments = "";
        public SymFile File;
        public Dictionary<string, Symbol> Symbols = new Dictionary<string, Symbol>();

        Symbol Duplicates;

        public Symbol(SymbolTypeEnum type, Token name)
        {
            Type = type;
            Name = name;
        }

        public bool IsDuplicte => Duplicates != null;

        public void AddDuplicate(Symbol symbol)
        {
            var d = this;
            while (d.Duplicates != null)
                d = d.Duplicates;
            d.Duplicates = symbol;
        }

        public string FullName =>
            Parent == null || Parent.Name == ""  ? Name
                : Parent.FullName + (Parent.Type == SymbolTypeEnum.Namespace && Type != SymbolTypeEnum.Namespace ? "/" : ".") + Name;

        public override string ToString()
        {
            return Type + ":" + FullName;
        }
    }

    /// <summary>
    /// Class, struct, enum, interface
    /// </summary>
    class SymType : Symbol
    {
        public SymType(Token name) : base(SymbolTypeEnum.Type, name) { }

        public int TypeArgCount;
    }

    class SymField : Symbol
    {
        public SymField(Token name) : base(SymbolTypeEnum.Field, name) { }
    }

    class SymFuncs : Symbol
    {
        public SymFuncs(Token name) : base(SymbolTypeEnum.Funcs, name) { }
    }

    class SymFunc : Symbol
    {
        public SymFunc(Token name) : base(SymbolTypeEnum.Func, name) { }
    }

    class SymTypeArg : Symbol
    {
        public readonly int Index;

        public SymTypeArg(Token name, int index) : base(SymbolTypeEnum.TypeArg, name)
        {
            Index = index;
        }
    }


}
