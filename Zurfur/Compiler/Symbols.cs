using System;
using System.Collections.Generic;

using Gosub.Zurfur.Lex;

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

        public Symbol Symbols = new Symbol(SymbolTypeEnum.Namespace, new Token(), null);
        public string[] Namespaces = Array.Empty<string>();
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
        public SymFile(string fileName) { FileName = fileName; }
    }

    class Symbol
    {
        public readonly SymbolTypeEnum Type;
        public readonly string Name = "";
        public Token Token = Token.Empty;
        public readonly Symbol Parent;
        public string Comments = "";

        SymFile mFile;
        Dictionary<string, Symbol> mSymbols;
        List<Symbol> mDuplicates;


        public Symbol(SymbolTypeEnum type, Token token, Symbol parent)
        {
            Type = type;
            Token = token;
            Parent = parent;
            Name = token.Name;
        }

        public Symbol(SymbolTypeEnum type, string name, Symbol parent)
        {
            Type = type;
            Parent = parent;
            Name = name;
        }


        public bool IsDuplicte => mDuplicates != null;

        public void AddDuplicate(Symbol symbol)
        {
            if (mDuplicates == null)
                mDuplicates = new List<Symbol>();
            mDuplicates.Add(symbol);
        }

        public SymFile File
        {
            set { mFile = value; }
            get { return mFile; }
        }

        public Dictionary<string, Symbol> Symbols
        {
            set { mSymbols = value; }
            get
            {
                if (mSymbols == null)
                    mSymbols = new Dictionary<string, Symbol>();
                return mSymbols;
            }
        }

        public string FullName
        {
            get
            {
                string name;
                if (Type == SymbolTypeEnum.Field || Type == SymbolTypeEnum.Funcs)
                    name = "." + Name;
                else if (Type == SymbolTypeEnum.Func)
                    name = Name;
                else if (Type == SymbolTypeEnum.Type)
                    name = ":" + Name;
                else if (Type == SymbolTypeEnum.Namespace)
                    name = "/" + Name;
                else
                    name = "?" + Name;

                if (Parent != null)
                    name = Parent.FullName + name;
                return name;
            }
        }

        public override string ToString()
        {
            return FullName;
        }
    }

    /// <summary>
    /// Class, struct, enum, interface
    /// </summary>
    class SymType : Symbol
    {
        public SymType(Token name, Symbol parent) : base(SymbolTypeEnum.Type, name, parent) { }

        public int TypeArgCount;
    }

    class SymField : Symbol
    {
        public SymField(Token name, Symbol parent) : base(SymbolTypeEnum.Field, name, parent) { }
    }

    class SymFuncs : Symbol
    {
        public SymFuncs(Token name, Symbol parent) : base(SymbolTypeEnum.Funcs, name, parent) { }
    }

    class SymFunc : Symbol
    {
        public SymFunc(string name, Token funcGroupName, Symbol parent) : base(SymbolTypeEnum.Func, name, parent)
            { Token = funcGroupName;  }
    }


    class SymTypeArg : Symbol
    {
        public readonly int Index;

        public SymTypeArg(Token name, int index, Symbol parent) : base(SymbolTypeEnum.TypeArg, name, parent)
        {
            Index = index;
        }
    }


}
