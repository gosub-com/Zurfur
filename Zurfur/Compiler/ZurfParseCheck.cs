﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gosub.Zurfur
{
    /// <summary>
    /// Check the parse tree for various errors and warnings.
    /// NOTE: Many of these errors will be moved to analysis phase
    /// </summary>
    class ZurfParseCheck
    {
        // Debug the parse tree
        bool mShowParseTree = true;

        // TBD: Still haven't decieded if `Map`, `Array`, and `List` should be lower case
        static WordSet sNoPubWarningKeywords = new WordSet("this object string void bool byte xint xuint int uint int8 uint8 int16 uint16 int32 uint32 int64 uint64 float32 float64 decimal");
        static WordSet sIllegalInterfaceQualifiers = new WordSet("pub private internal protected");
        static WordSet sRequireGlobalFieldQualifiers = new WordSet("static const");
        static WordSet sInterfaceQualifiersAllowed = new WordSet("static");
        static WordSet sGlobalFuncsRequiringStatic = new WordSet("func afunc construct destruct");
        static WordSet sStaticQualifier = new WordSet("static");
        static WordSet sGlobalFuncsNotAllowed = new WordSet("prop this operator");

        static WordSet sInterfaceQualifiers = new WordSet("pub public protected private internal");
        static WordSet sClassQualifiers = new WordSet("pub public protected private internal unsafe sealed sealed1 abstract");
        static WordSet sStructQualifiers = new WordSet("pub public protected private internal unsafe ro ref");
        static WordSet sEnumQualifiers = new WordSet("pub public protected private internal");
        static WordSet sFieldQualifiers = new WordSet("pub public protected private internal unsafe static volatile ro const");
        static WordSet sFuncQualifiers = new WordSet("pub public protected private internal unsafe static extern abstract virtual override new");
        static WordSet sFuncOperatorQualifiers = new WordSet("pub public protected private internal unsafe extern");

        ZurfParse mParser;

        public ZurfParseCheck(ZurfParse parser)
        {
            mParser = parser;
        }

        public void Check(SyntaxUnit unit)
        {
            CheckParseTree(unit);            
            ShowTypes(unit); // TBD: This is temporary until type analysis is complete
            ShowParseTree(unit);
        }

        void CheckParseTree(SyntaxUnit unit)
        {
            foreach (var aClass in unit.Classes)
            {
                CheckPublicQualifier(aClass.ParentClass, aClass.Name, aClass.Qualifiers);

                var keyword = aClass.Keyword;
                var outerKeyword = aClass.ParentClass == null ? "" : aClass.ParentClass.Keyword;
                if (aClass.Namespace == null && outerKeyword == "")
                    mParser.RejectToken(keyword, "The namespace must be defined before the " + keyword);
                if (outerKeyword != "" && (outerKeyword == "interface" || outerKeyword == "enum"))
                    mParser.RejectToken(keyword, "Classes, structs, enums, and interfaces may not be nested inside an interface or enum");

                switch (keyword)
                {
                    case "enum":
                        RejectQualifiers(aClass.Qualifiers, sEnumQualifiers, "Qualifier does not apply to enums");
                        break;
                    case "interface":
                        RejectQualifiers(aClass.Qualifiers, sInterfaceQualifiers, "Qualifier does not apply to interfaces");
                        break;
                    case "struct":
                        RejectQualifiers(aClass.Qualifiers, sStructQualifiers, "Qualifier does not apply to structs");
                        break;
                    case "class":
                        RejectQualifiers(aClass.Qualifiers, sClassQualifiers, "Qualifier does not apply to classes");
                        break;
                }
            }

            foreach (var func in unit.Funcs)
            {
                CheckPublicQualifier(func.ParentClass, func.Name, func.Qualifiers);

                var keyword = func.Keyword;
                var outerKeyword = func.ParentClass == null ? "" : func.ParentClass.Keyword;
                if (func.Namespace == null && outerKeyword == "")
                    mParser.RejectToken(keyword, "The namespace must be defined before method");
                if (func.Statements == null && outerKeyword != "interface" && keyword != "prop" && keyword != "get" && keyword != "set")
                    RejectTokenIfNotQualified(keyword, func.Qualifiers, "extern", "Function without a body must have the'extern' qualifier");
                if (func.Statements != null && outerKeyword == "interface")
                    mParser.RejectToken(keyword, "Interface may not contain function body");
                if (outerKeyword == "interface")
                    RejectQualifiers(func.Qualifiers, sInterfaceQualifiersAllowed, "This qualifier may not appear before a function defined inside an interface");
                if (outerKeyword == "" && sGlobalFuncsRequiringStatic.Contains(keyword) && !HasQualifier(func.Qualifiers, sStaticQualifier))
                    mParser.RejectToken(keyword, "Functions at the namespace level must be static");
                if (outerKeyword == "" && sGlobalFuncsNotAllowed.Contains(keyword))
                    mParser.RejectToken(keyword, "Must not be defined at the namespace level");

                switch (keyword)
                {
                    case "operator":
                        RejectQualifiers(func.Qualifiers, sFuncOperatorQualifiers, "Operator may not use this qualifier");
                        break;
                    case "construct":
                    case "func":
                    case "afunc":
                    case "this":
                    case "prop":
                        RejectQualifiers(func.Qualifiers, sFuncQualifiers, "Qualifier does not apply to this method type");
                        break;
                }
            }

            foreach (var field in unit.Fields)
            {
                CheckPublicQualifier(field.ParentClass, field.Name, field.Qualifiers);

                var outerKeyword = field.ParentClass == null ? "" : field.ParentClass.Keyword;
                if (outerKeyword == "" && !HasQualifier(field.Qualifiers, sRequireGlobalFieldQualifiers))
                    mParser.RejectToken(field.Name, "Fields at the namespace level must be static or const");
                if (outerKeyword == "interface")
                    mParser.RejectToken(field.Name, "Fields are not allowed inside an interface");

                RejectQualifiers(field.Qualifiers, sFieldQualifiers, "This qualifier does not apply to a field");
            }
        }

        void CheckPublicQualifier(SyntaxClass parentClass, Token token, Token[] qualifiers)
        {
            if (token == null || token.Name == "" || qualifiers == null)
                return;

            bool isInInterface = parentClass != null && parentClass.Keyword.Name == "interface";
            bool isConst = false;
            bool isPublic = false;
            foreach (var qualifier in qualifiers)
            {
                if (isInInterface && sIllegalInterfaceQualifiers.Contains(qualifier))
                {
                    mParser.RejectToken(qualifier, "An interface may not have this qualifier");
                }

                switch (qualifier.Name)
                {
                    case "public":
                        mParser.RejectToken(qualifier, "Use 'pub' instead of public");
                        return;
                    case "pub":
                        isPublic = true;
                        break;
                    case "protected":
                    case "internal":
                        return;
                    case "private":
                        break; // Private checked below
                    case "const":
                        isConst = true;
                        break;
                }
            }
            if (isPublic || isInInterface)
            {
                // Public warning
                if (char.IsLower(token.Name[0]) && !sNoPubWarningKeywords.Contains(token.Name))
                    token.AddWarning("Lower case symbols should not be public");
            }
            else if (!isConst)
            {
                // Private warnings
                if (char.IsUpper(token.Name[0]) && !sNoPubWarningKeywords.Contains(token.Name))
                    token.AddWarning("Upper case symbols should not be private");
                else if (ZurfParse.sOverloadableOperators.Contains(token.Name))
                    token.AddWarning("Operators should not be private");
                else if (token.Name == "this")
                    token.AddWarning("Indexer should not be private");
            }
        }

        bool HasQualifier(Token[] qualifiers, WordSet accept)
        {
            foreach (var q in qualifiers)
                if (accept.Contains(q.Name))
                    return true;
            return false;
        }

        // Reject the token if it's not qualified
        void RejectTokenIfNotQualified(Token token, IEnumerable<Token> qualifiers, string expected, string errorMessage)
        {
            foreach (var t in qualifiers)
                if (t.Name == expected)
                    return;
            mParser.RejectToken(token, errorMessage);
        }

        void RejectQualifiers(Token []qualifiers, string errorMessage)
        {
            RejectQualifiers(qualifiers, null, errorMessage);
        }

        // Reject tokens with errorMessage.  Reject all of them if acceptSet is null.
        void RejectQualifiers(Token []qualifiers, WordSet acceptSet, string errorMessage)
        {
            foreach (var token in qualifiers)
            {
                if (token == "readonly")
                {
                    mParser.RejectToken(token, "Use 'ro' instead");
                    continue;
                }
                if (acceptSet == null || !acceptSet.Contains(token))
                    mParser.RejectToken(token, errorMessage);
            }
        }

        public void ShowTypes(SyntaxUnit unit)
        {
            foreach (var aClass in unit.Classes)
                ShowTypes(aClass);
            foreach (var func in unit.Funcs)
                ShowTypes(func);
            foreach (var field in unit.Fields)
            {
                ShowTypes(field.TypeName, true);
                ShowTypes(field.InitExpr, false);
            }
        }

        void ShowTypes(SyntaxClass aClass)
        {
            ShowTypes(aClass.Alias, true);
            ShowTypes(aClass.BaseClass, true);
            aClass.Name.Type = eTokenType.TypeName;
            ShowTypes(aClass.TypeParams);
            if (aClass.Constraints != null)
                foreach (var constraint in aClass.Constraints)
                {
                    if (constraint.GenericTypeName != null)
                        constraint.GenericTypeName.Type = eTokenType.TypeName;
                    if (constraint.TypeNames != null)
                        foreach (var typeName in constraint.TypeNames)
                            ShowTypes(typeName, true);
                }
            if (aClass.Implements != null)
                foreach (var imp in aClass.Implements)
                    ShowTypes(imp, true);
        }

        void ShowTypes(SyntaxFunc func)
        {
            ShowTypes(func.ClassName, true);
            ShowTypes(func.TypeParams);
            ShowTypes(func.ReturnType, true);
            ShowTypes(func.Statements, false);
            if (func.Params != null)
                foreach (var p in func.Params)
                    ShowTypes(p.TypeName, true);
        }

        void ShowTypes(SyntaxExpr expr, bool isType)
        {
            if (expr == null)
                return;
            if (isType && expr.Token.Type == eTokenType.Identifier)
                expr.Token.Type = eTokenType.TypeName;

            // Cast
            if (expr.Token == ")")
            {
                if (expr.Count != 0)
                    ShowTypes(expr[0], true);
                for (int i = 1; i < expr.Count; i++)
                    ShowTypes(expr[i], isType);
                return;
            }

            if (expr.Token == ZurfParse.VIRTUAL_TOKEN_TYPE_ARG_LIST)
                isType = true;
            foreach (var e in expr)
                ShowTypes(e, isType);
        }

        void ShowTypes(SyntaxTypeParam []param)
        {
            if (param != null)
                foreach (var p in param)
                    p.Name.Type = eTokenType.TypeName;
        }

        public void ShowParseTree(SyntaxUnit unit)
        {
            if (!mShowParseTree)
                return;

            foreach (var aClass in unit.Classes)
            {
                ShowParseTree(aClass.BaseClass);
                ShowParseTree(aClass.Alias);
            }
            foreach (var func in unit.Funcs)
            {
                ShowParseTree(func.ReturnType);
                if (func.Params != null)
                    foreach (var param in func.Params)
                        ShowParseTree(param.TypeName);
                if (func.Statements != null)
                    foreach (var statement in func.Statements)
                        ShowParseTree(statement);
            }
            foreach (var field in unit.Fields)
            {
                ShowParseTree(field.InitExpr);
                ShowParseTree(field.TypeName);
            }
        }


        SyntaxExpr ShowParseTree(SyntaxExpr expr)
        {
            if (expr == null)
                return expr;
            expr.Token.AddInfo("Parse tree: " + expr.ToString());
            foreach (var e in expr)
                ShowParseTree(e); // Subtrees without info token
            return expr;
        }


    }
}