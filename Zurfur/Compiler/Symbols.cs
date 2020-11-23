using System;
using System.Collections.Generic;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{

    class SymPackage
    {
        public string PackageName = "";
        public PackageInfo PackageInfo = new PackageInfo();
        public CompilerInfo CompilerInfo = new CompilerInfo();

        public Symbol Symbols = new SymNamespace("", null);
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
        public string FileName;
        public SyntaxFile SyntaxFile;
        public Symbol[] Use;
        public SymFile(string fileName, SyntaxFile syntaxFile ) { FileName = fileName; SyntaxFile = syntaxFile; }
    }

    abstract class Symbol
    {
        public abstract string TypeName { get; }
        public readonly string Name = "";
        public Token Token = Token.Empty;
        public readonly Symbol Parent;
        public string Comments = "";

        SymFile mFile;
        Dictionary<string, Symbol> mSymbols;
        List<Symbol> mDuplicates;

        public Symbol(Token token, Symbol parent)
        {
            Token = token;
            Parent = parent;
            Name = token.Name;
        }

        public Symbol(string name, Symbol parent)
        {
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
                if (this is SymField || this is SymMethods)
                    name = "." + Name;
                else if (this is SymMethod)
                    name = "." + Name;
                else if (this is SymType)
                    name = ":" + Name;
                else if (this is SymNamespace)
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

    class SymNamespace : Symbol
    {
        public SymNamespace(string name, Symbol parent) : base(name, parent) { }
        public override string TypeName => "namespace";
    }

    /// <summary>
    /// Class, struct, enum, interface
    /// </summary>
    class SymType : Symbol
    {
        public string TypeKeyword = "type";
        public SymType(Token name, Symbol parent) : base(name, parent) { }
        public override string TypeName => TypeKeyword;
    }

    class SymField : Symbol
    {
        public SymField(Token name, Symbol parent) : base(name, parent) { }
        public override string TypeName => "field";
    }

    class SymMethods : Symbol
    {
        public SymMethods(Token name, Symbol parent) : base(name, parent) { }
        public override string TypeName => "methods";
    }

    class SymMethod : Symbol
    {
        public SymMethod(string name, Token funcGroupName, Symbol parent) : base(name, parent)
            { Token = funcGroupName;  }
        public override string TypeName =>  "method";

    }



}
