﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Gosub.Zurfur
{
    class SyntaxUnit
    {
        public List<SyntaxUsing> Using = new List<SyntaxUsing>();
        public SyntaxNamespace CurrentNamespace { get { return Namespaces.Count == 0 ? null : Namespaces[Namespaces.Count - 1]; } }

        public List<SyntaxNamespace> Namespaces = new List<SyntaxNamespace>();
        public List<SyntaxClass> Classes = new List<SyntaxClass>();
        public List<SyntaxFunc> Funcs = new List<SyntaxFunc>();
        public List<SyntaxField> Fields = new List<SyntaxField>();

    }

    class SyntaxNamespace
    {
        public string[] Comments;
        public Token Keyword;
        public SyntaxExpr QualifiedIdentifiers;

        public override string ToString()
        {
            if (QualifiedIdentifiers == null)
                return "(namespace)";
            var s = "";
            for (int i = 0; i < QualifiedIdentifiers.Count; i++)
                s += QualifiedIdentifiers[i].Token + (i == QualifiedIdentifiers.Count - 1 ? "" : ".");
            return s;
        }
    }

    class SyntaxUsing
    {
        public Token Keyword;
        public SyntaxExpr QualifiedIdentifiers;
    }

    class SyntaxClass // or struct or interface
    {
        public string[] Comments;
        public Token[] Qualifiers;
        public Token Keyword;
        public SyntaxExpr BaseClass;
        public SyntaxExpr ClassName;
        public SyntaxExpr Alias;
        public SyntaxExpr[] Implements;
        public SyntaxConstraint []Constraints;

        public override string ToString()
        {
            return ClassName == null ? "(class)" : ClassName.ToString();
        }
    }

    class SyntaxConstraint
    {
        public Token Keyword;  // Where
        public Token GenericTypeName;
        public SyntaxExpr []TypeNames;
    }

    class SyntaxField
    {
        public string[] Comments;
        public Token[] Qualifiers;
        public Token Name;
        public SyntaxExpr TypeName;
        public SyntaxExpr InitExpr;

        public override string ToString()
        {
            return Name == null ? "(token)" : Name;
        }
    }

    class SyntaxFunc
    {
        public string[] Comments;
        public Token[] Qualifiers;
        public Token Keyword;
        public SyntaxExpr ClassName;
        public Token GetOrSetToken;
        public SyntaxExpr FuncName;
        public SyntaxFuncParam[] Params;
        public SyntaxExpr Return;
        public SyntaxExpr Statements;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(FuncName == null ? "(func)" : FuncName.ToString());
            sb.Append("(");
            if (Params != null)
                for (int i = 0; i < Params.Length; i++)
                    sb.Append(Params[i].ToString() + (i == Params.Length - 1 ? "" : ","));
            sb.Append(")");
            return sb.ToString();
        }
    }

    class SyntaxFuncParam
    {
        public Token[] Qualifiers;
        public Token Name;
        public SyntaxExpr TypeName;

        public override string ToString()
        {
            return (Name == null ? "(param)" : Name) + (TypeName == null ? "" : TypeName.ToString());
        }
    }

}