﻿using System;
using System.Collections.Generic;
using System.Text;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    class SymFile
    {
        public string Name = "";
        public string Path = "";
        public SyntaxFile SyntaxFile;
        public string[] Use = new string[0];

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
        public SymLoc(SymFile file, Token token) { File = file; Token = token; }
    }

    abstract class Symbol
    {
        public abstract string Kind { get; }
        public readonly string ParentName;
        public readonly string FullName;
        public readonly string Name;
        public string Comments = "";

        public List<SymLoc> Locations = new List<SymLoc>();
        public Dictionary<string, string> Children = new Dictionary<string, string>();

        List<Symbol> mDuplicates;

        public Symbol(string parentName, SymFile file, Token token)
        {
            ParentName = parentName;
            Name = token.Name;
            FullName = GetFullName();
            AddLocation(file, token);
        }

        public Symbol(string parentName, string name)
        {
            ParentName = parentName;
            Name = name;
            FullName = GetFullName();
        }

        string GetFullName()
        {
            if (ParentName == "")
                return Name;
            return ParentName + "." + Name;
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

        public override string ToString()
        {
            return FullName;
        }
    }

    class SymNamespace : Symbol
    {
        public SymNamespace(string parentName, SymFile file, Token token) : base(parentName, file, token) { }
        public override string Kind => "namespace";
    }

    /// <summary>
    /// Class, struct, enum, interface
    /// </summary>
    class SymType : Symbol
    {
        public string TypeKeyword = "";
        public SymType(string parentName, SymFile file, Token token) : base(parentName, file, token) { }
        public SymType(string parentName, string name) : base(parentName, name) { }
        public override string Kind => "type";

        // TBD: This is only needed to maintain type arg order.  Could remove this
        //      if we also store an ordered list of symbols in the symbol table
        public string[] TypeArgs = Array.Empty<string>();
    }


    class SymTypeArg : SymType
    {
        public SymTypeArg(string parentName, SymFile file, Token token) : base(parentName, file, token) { }
        public override string Kind => "type argument";
    }

    /// <summary>
    /// Parent is the full name of the generic type, typeParams are the full name of each argument.
    /// The symbol name is a combination of both parentName<T0,T1,T2...>
    /// or for generic functions fun(T0,T1)->(R0,R1)
    /// </summary>
    class SymSpecializedType : SymType
    {
        public readonly string[] Params;
        public readonly string[] Returns;

        public SymSpecializedType(string parentName, string[] typeParams)
            : base(parentName, "<" + TypeArgsFullName(typeParams) + ">") 
        {
            Params = typeParams;
            Returns = Array.Empty<string>();
        }
        public SymSpecializedType(string parentName, string[] typeParams, string []typeReturns)
            : base(parentName, ParamsFuncFullName(typeParams, typeReturns))
        {
            Params = typeParams;
            Returns = typeReturns;
        }

        public static string ParamsFuncFullName(string []typeParams, string []typeReturns)
        {
            return "(" + TypeArgsFullName(typeParams) + ")->(" + TypeArgsFullName(typeReturns) + ")";
        }

        static string TypeArgsFullName(string []typeParams)
        {
            if (typeParams.Length == 0)
                return "";
            if (typeParams.Length == 1)
                return typeParams[0];
            StringBuilder sb = new StringBuilder();
            sb.Append(typeParams[0]);
            for (int i = 1;  i < typeParams.Length;  i++)
            {
                sb.Append(",");
                sb.Append(typeParams[i]);
            }
            return sb.ToString();
        }

    }

    class SymField : Symbol
    {
        public SymField(string parentName, SymFile file, Token token) : base(parentName, file, token) { }
        public override string Kind => "field";
        public SyntaxField Syntax;
        public string TypeName = "(unresolved)";
    }

    class SymMethods : Symbol
    {
        public SymMethods(string parentName, SymFile file, Token token) : base(parentName, file, token) { }
        public override string Kind => "methods";
    }

    class SymMethod : Symbol
    {
        // The name is the function type
        public SymMethod(string parentName, string name) : base(parentName, name) { }
        public override string Kind => "method";
    }

    class SymMethodArg : Symbol
    {
        public SymMethodArg(string parentName, SymFile file, Token token) : base(parentName, file, token) { }
        public override string Kind => "method arg";
        public string TypeName = "(unresolved)";
    }
}