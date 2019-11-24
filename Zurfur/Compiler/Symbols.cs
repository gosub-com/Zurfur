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

        public Symbol Symbols = new Symbol(SymbolTypeEnum.Namespace, new Token(), null);
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
        public readonly Symbol Parent;
        public string Comments = "";

        SymFile mFile;
        Dictionary<string, Symbol> mSymbols;
        Symbol mDuplicates;
        string mFullName;


        public Symbol(SymbolTypeEnum type, Token name, Symbol parent)
        {
            Type = type;
            Name = name;
            Parent = parent;
        }

        public bool IsDuplicte => mDuplicates != null;

        public void AddDuplicate(Symbol symbol)
        {
            var d = this;
            while (d.mDuplicates != null)
                d = d.mDuplicates;
            d.mDuplicates = symbol;
        }

        public SymFile File
        {
            set { mFile = value; }
            get
            {
                if (mFile == null)
                    mFile = new SymFile();
                return mFile;
            }
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
                if (mFullName == null)
                {
                    if (Parent == null || Parent.Name == "")
                        mFullName = Name;
                    else
                        mFullName = Parent.FullName + (Parent.Type == SymbolTypeEnum.Namespace && Type != SymbolTypeEnum.Namespace ? "/" : ".") + Name;
                }
                return mFullName;
            }
        }

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
        public SymType(Token name, Symbol parent) : base(SymbolTypeEnum.Type, name, parent) { }

        public int TypeArgCount;
    }

    class SymField : Symbol
    {
        public SymField(Token name, Symbol parent) : base(SymbolTypeEnum.Field, name, parent) { }
        public string FullTypeName = "TBD";
    }

    class SymFuncs : Symbol
    {
        public SymFuncs(Token name, Symbol parent) : base(SymbolTypeEnum.Funcs, name, parent) { }
    }

    class SymFunc : Symbol
    {
        public SymFunc(Token name, Symbol parent) : base(SymbolTypeEnum.Func, name, parent) { }
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
