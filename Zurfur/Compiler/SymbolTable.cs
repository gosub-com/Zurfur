using System;
using System.Collections.Generic;
using Gosub.Zurfur.Lex;

/// <summary>
/// This file contains high level symbol table types.  The 'info' types in SymbolInfo
/// contain the low level class data.  It might be better to completely separate them,
/// but for now the high level has access to the low level (not the other way around)
/// </summary>


namespace Gosub.Zurfur.Compiler
{


    class SymFile
    {
        public string Name = "";
        public string Path = "";
        public SyntaxFile SyntaxFile;
        public Symbol[] Use = new Symbol[0];

        public SymFile(string path, SyntaxFile syntaxFile) 
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            SyntaxFile = syntaxFile; 
        }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Symbol location (file + token)
    /// </summary>
    readonly struct SymLoc
    {
        public readonly SymFile File;
        public readonly Token Token;
        public SymLoc(SymFile file, Token token) { File = file;  Token = token; }
    }

    abstract class Symbol
    {
        public abstract string Kind { get; }
        public readonly string Name;
        public readonly Symbol Parent;
        public string Comments = "";

        public List<SymLoc> Locations;
        Dictionary<string, Symbol> mSymbols;
        List<Symbol> mDuplicates;

        public Symbol(Symbol parent, SymFile file, Token token)
        {
            Parent = parent;
            Name = token.Name;
            Locations = new List<SymLoc>(1);
            AddLocation(file, token);
        }

        public void AddLocation(SymFile file, Token token)
        {
            Locations.Add(new SymLoc(file, token));
        }

        /// <summary>
        /// Retrieve the file containing the symbol definition.
        /// Types, functions, and fields should have exactly one location.
        /// Namespaces and function groups may have multiples.
        /// Throws an exception if file location is not present or if
        /// multiple location exist.
        /// </summary>
        SymLoc Location
        {
            get
            {
                if (Locations.Count == 0)
                    throw new Exception("Symbol location cannot be found");
                if (Locations.Count > 1)
                    throw new Exception("Symbol has multiple locations");
                return Locations[0];
            }
        }

        /// <summary>
        /// Throws exception if not a unique symbol with exactly one location
        /// </summary>
        public Token Token => Location.Token;

        /// <summary>
        /// Throws exception if not a unique symbol with exactly one location
        /// </summary>
        public SymFile File => Location.File;

        public bool IsDuplicte => mDuplicates != null;

        public void AddDuplicate(Symbol symbol)
        {
            if (mDuplicates == null)
                mDuplicates = new List<Symbol>();
            mDuplicates.Add(symbol);
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

        public bool IsEmpty => mSymbols == null || mSymbols.Count == 0;

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
        public SymNamespace(Symbol parent, SymFile file, Token token) : base(parent, file, token) { }
        public override string Kind => "namespace";
    }

    /// <summary>
    /// Class, struct, enum, interface
    /// </summary>
    class SymType : Symbol
    {
        public string TypeKeyword = "type";
        public SymType(Symbol parent, SymFile file, Token token) : base(parent, file, token) { }
        public override string Kind => TypeKeyword;

        public SymTypeInfo Info;

    }


    class SymTypeArg : SymType
    {
        public SymTypeArg(Symbol parent, SymFile file, Token token) : base(parent, file, token) { }
        public override string Kind => "type argument";
    }

    class SymField : Symbol
    {
        public SymField(Symbol parent, SymFile file, Token token) : base(parent, file, token) { }
        public override string Kind => "field";
        public SyntaxField Syntax;
        public SymType Type;
    }

    class SymMethods : Symbol
    {
        public SymMethods(Symbol parent, SymFile file, Token token) : base(parent, file, token) { }
        public override string Kind => "methods";
    }

    class SymMethod : Symbol
    {
        public SymMethod(Symbol parent, SymFile file, Token token) : base(parent, file, token) { }
        public override string Kind =>  "method";
    }



}
