using System;
using System.Drawing;
using System.Collections.Generic;

namespace Gosub.Zurfur
{
    /// <summary>
    /// Base class for the parser
    /// </summary>
    class ParseZurf
    {
        // Since '<' is ambiguous for type argument lists, this symbol
        // is used instead.  '<' is only for less than symbol.
        public const string VIRTUAL_TOKEN_TYPE_ARG_LIST = "<T<";

        const string VIRTUAL_TOKEN_SHIFT_RIGHT = ">>";
        const string VIRTUAL_TOKEN_GE = ">=";
        const bool SHOW_PARSE_TREE = true;

        bool				mParseError;	// Flag is set whenever a parse error occurs
        Lexer				mLexer;			// Lexer to be paresed
        Lexer.Enumerator	mLexerEnum;		// Enumerator for the Lexer
        string				mTokenName="*"; // Skipped by first accept
        Token				mToken;
        Token               mPrevToken;
        List<string>        mComments = new List<string>();
        WordSet             mIdentifierAllowedReservedWords;

        SyntaxUnit mUnit;

        // NOTE: >> and >= are omitted and handled at parser level.
        //       TBD: Need to handle >>= as well
        public const string TokenSymbols = "<< <= == != && || += -= *= /= %= &= |= ^= <<= -> === ::";

        static readonly string sReservedWordsList = "abstract as base break case catch class const "
            + "continue default delegate do else enum event explicit extern false defer use "
            + "finally fixed for goto if implicit in interface internal is extends lock namespace "
            + "new null operator out override params public private protected readonly ro ref "
            + "return sealed sealed1 sizeof stackalloc heapalloc static struct switch this throw true try "
            + "typeof unsafe using static virtual volatile while asm managed unmanaged implements "
            + "async await astart get set yield global partial var where nameof func construct destruct cast";
        static readonly string sReservedControlWords = "using namespace class struct interface func construct destruct if else switch case";
        static WordMap<eTokenType> sReservedWords = new WordMap<eTokenType>();

        static WordSet sInterfaceQualifiers = new WordSet("public protected private internal");
        static WordSet sClassQualifiers = new WordSet("public protected private internal unsafe sealed sealed1 abstract");
        static WordSet sStructQualifiers = new WordSet("public protected private internal unsafe");
        static WordSet sEnumQualifiers = new WordSet("public protected private internal");
        static WordSet sFuncQualifiers = new WordSet("public protected private internal unsafe static extern abstract virtual override new async");
        static WordSet sFieldQualifiers = new WordSet("public protected private internal unsafe static volatile ro const");


        static WordMap<int> sClassFuncFieldQualifiers = new WordMap<int>()
        {
            { "public", 1 }, { "protected", 1 }, { "private", 1 }, { "internal", 1 },
            { "unsafe", 2 }, { "static", 3 },  {"const", 3 }, { "extern", 4 },
            { "sealed", 6 }, { "sealed1", 6 },
            { "abstract", 8 }, { "virtual", 8},  { "override", 8 }, { "new", 8 },
            { "async", 9 }, { "volatile", 12 },
            { "readonly", 14 }, { "ro", 14 }
        };

        static WordSet sFieldDefTypeQualifiers = new WordSet("ref");
        static WordSet sFuncDefReturnTypeQualifiers = new WordSet("ref ro");
        static WordSet sFuncDefParamTypeQualifiers = new WordSet("out ref ro");
        static WordSet sTypeDefQualifiers = new WordSet("ref ro");
        static WordSet sTypeDefParamQualifiers = new WordSet("in out");
        static WordSet sFuncCallParamQualifiers = new WordSet("out ref");
        static WordSet sEmptyWordSet = new WordSet("");

        static WordSet sAllowNewKeyword = new WordSet("new");
        static WordSet sAllowThisKeyword = new WordSet("this");
        static WordSet sAllowConstraintKeywords = new WordSet("class struct unmanaged");


        static WordSet sOverloadableOperators = new WordSet("+ - * /");
        static WordSet sComparisonOperators = new WordSet("== === != > < <="); // For '>=', use VIRTUAL_TOKEN_GE
        static WordSet sAddOperators = new WordSet("+ - | ^");
        static WordSet sMultiplyOperators = new WordSet("* / % & << >"); // For '>>', use VIRTUAL_TOKEN_SHIFT_RIGHT
        static WordSet sAssignOperators = new WordSet("= += -= *= /= %= |= &= ^= <<= >>=");
        static WordSet sUnaryOperators = new WordSet("+ - ! ~ & * #");

        // C# uses these symbols to resolve type argument ambiguities: "(  )  ]  }  :  ;  ,  .  ?  ==  !=  |  ^"
        // This seems stange because something like `a = F<T1,T2>;` is not a valid expression
        // The followign symbols allow us to call functions, create types, and access static members
        // For example `F<T1>()` to call a function or create a type, `F<T1>.Name` to access a static or member
        static WordSet sTypeArgumentParameterSymbols = new WordSet("( .");

        static WordSet sStatementsDone = new WordSet("} func using namespace class struct interface enum", true);
        static WordSet sRejectAnyStop = new WordSet("; { } using namespace class struct interface enum if else for while throw switch case func", true);
        static WordSet sRejectStatement = new WordSet("");
        static WordSet sRejectFuncName = new WordSet("( [");
        static WordSet sRejectFuncParamsParen = new WordSet(")");
        static WordSet sRejectFuncParamsBracket = new WordSet("]");
        static WordSet sRejectFuncParam = new WordSet(", )");
        static WordSet sRejectParen = new WordSet(")");
        static WordSet sRejectBracket = new WordSet("]");

        static ParseZurf()
        {
            sReservedWords.AddWords(sReservedWordsList, eTokenType.Reserved);
            sReservedWords.AddWords(sReservedControlWords, eTokenType.ReservedControl);
        }

        /// <summary>
        /// Parse the given lexer
        /// </summary>
        public ParseZurf(Lexer tokens)
        {
            mLexer = tokens;
            mLexerEnum = new Lexer.Enumerator(mLexer);
            Accept();
        }

        public SyntaxUnit Parse()
        {
            mUnit = new SyntaxUnit();
            ParseCompilationUnit();
            ShowTypes(mUnit);
            return mUnit;
        }

        /// Temporary, remove this later
        void ShowTypes(SyntaxUnit unit)
        {
            foreach (var class2 in mUnit.Classes)
                ShowTypes(class2);
            foreach (var func in mUnit.Funcs)
                ShowTypes(func);
            foreach (var field in mUnit.Fields)
            {
                ShowTypes(field.TypeName, true);
                ShowTypes(field.InitExpr, false);
            }
        }

        /// Temporary, remove this later
        void ShowTypes(SyntaxClass class1)
        {
            ShowTypes(class1.Alias, true);
            ShowTypes(class1.BaseClass, true);
            ShowTypes(class1.ClassName, true);
            if (class1.Constraints != null)
                foreach (var constraint in class1.Constraints)
                {
                    if (constraint.GenericTypeName != null)
                        constraint.GenericTypeName.Type = eTokenType.TypeName;
                    if (constraint.TypeNames != null)
                        foreach (var typeName in constraint.TypeNames)
                            ShowTypes(typeName, true);
                }
            if (class1.Implements != null)
                foreach (var imp in class1.Implements)
                    ShowTypes(imp, true);
        }

        /// Temporary, remove this later
        void ShowTypes(SyntaxFunc func)
        {
            ShowTypes(func.ClassName, true);
            ShowTypes(func.Return, true);
            ShowTypes(func.Statements, false);
            if (func.Params != null)
                foreach (var p in func.Params)
                        ShowTypes(p.TypeName, true);
        }

        /// Temporary, remove this later
        void ShowTypes(SyntaxExpr expr, bool isType)
        {
            if (expr == null)
                return;
            if (isType && expr.Token.Type == eTokenType.Identifier)
                expr.Token.Type = eTokenType.TypeName;
            if (expr.Token == VIRTUAL_TOKEN_TYPE_ARG_LIST)
                isType = true;
            foreach (var e in expr)
                ShowTypes(e, isType);
        }

        /// <summary>
        /// Parse the file
        /// </summary>
        public void ParseCompilationUnit()
        {
            mUnit = new SyntaxUnit();
            var qualifiers = new List<Token>();

            while (mTokenName != "")
            {
                ParseScopeStatements("");
                if (mTokenName != "")
                {
                    RejectToken(mToken, "Unexpected symbol at top level scope");
                    Accept();
                }
            }
        }

        /// <summary>
        /// Parse using, namespace, enum, interface, struct, class, func, or field
        /// </summary>
        private void ParseScopeStatements(string outerKeyword)
        {
            var qualifiers = new List<Token>();
            while (mTokenName != "" && mTokenName != "}")
            {
                // Read qualifiers
                qualifiers.Clear();
                var attributes = ParseAttributes();
                ParseQualifiers(qualifiers, sClassFuncFieldQualifiers);
                var comments = mComments.ToArray();
                var keyword = mToken;
                switch (mTokenName)
                {
                    case ";":
                        RejectQualifiers(qualifiers, "Expecting a statement after qualifier");
                        SkipSemicolon();
                        break;

                    case "{":
                        RejectQualifiers(qualifiers, "Unexpected qualifiers");
                        RejectToken(mToken, "Unexpected start of scope.  Expecting a keyword, such as 'class', 'func', etc. before the start of a new scope.");
                        Accept();
                        break;

                    case "using":
                        mUnit.Using.Add(ParseUsingStatement());
                        RejectQualifiers(qualifiers, "Qualifiers are not allowed on the 'using' statement");
                        if (outerKeyword != "")
                            RejectToken(keyword, "Using statements must not be inside a class body");
                        else if (mUnit.Namespaces.Count != 0)
                            RejectToken(keyword, "Using statements must come before the namespace");
                        SkipSemicolon();
                        break;

                    case "namespace":
                        mUnit.Namespaces.Add(ParseNamespaceStatement(mUnit, comments));
                        RejectQualifiers(qualifiers, "Qualifiers are not allowed on the 'namespace' statement");
                        if (outerKeyword != "")
                            RejectToken(keyword, "Namespace statements must not be inside a class body");
                        SkipSemicolon();
                        break;

                    case "enum":
                        RejectQualifiers(qualifiers, "This qualifier does not apply to enum statements", sEnumQualifiers);
                        goto ParseClass;
                    case "interface":
                        RejectQualifiers(qualifiers, "This qualifier does not apply to interface statements", sInterfaceQualifiers);
                        goto ParseClass;
                    case "struct":
                        RejectQualifiers(qualifiers, "This qualifier does not apply to struct statements", sStructQualifiers);
                        goto ParseClass;
                    case "class":
                        RejectQualifiers(qualifiers, "This qualifier does not apply to class statements", sClassQualifiers);
ParseClass:
                        if (mUnit.CurrentNamespace == null)
                            RejectToken(keyword, "The namespace must be defined before " + keyword.Name + " statements");
                        if (outerKeyword == "interface" || outerKeyword == "enum")
                            RejectToken(mToken, "Classes, structs, enums, and interfaces may not be nested inside an interface or enum");
                        var classDef = ParseClass(qualifiers, comments);
                        if (classDef.ClassName != null)
                            CheckPublicQualifier(classDef.ClassName.Token, qualifiers);
                        mUnit.Classes.Add(classDef);
                        break;

                    case "operator":
                    case "get":
                    case "set":
                    case "construct":
                    case "func":
                        RejectQualifiers(qualifiers, "This qualifier does not apply to func statements", sFuncQualifiers);
                        if (mUnit.CurrentNamespace == null)
                            RejectToken(keyword, "The namespace must be defined before func statements");
                        var funcDef = ParseMethodDef(qualifiers, comments, outerKeyword == "interface");
                        if (funcDef.FuncName != null)
                            CheckPublicQualifier(funcDef.FuncName.Token, qualifiers);
                        mUnit.Funcs.Add(funcDef);
                        break;

                    default:
                        if (outerKeyword == "")
                        {
                            // Top level, fields not allowed
                            RejectToken(mToken, "Expecting keyword using, namespace, class, struct, interface, enum, or func.  " 
                                + "Fields may not be defined the namesapce level, they must be contained in a class or struct.");
                            Accept();
                        }
                        else if (mToken.Type != eTokenType.Identifier)
                        {
                            RejectToken(mToken, "Expecting keyword using, namespace, class, struct, interface, enum, func, or a field name identifier.");
                            Accept();
                        }
                        else
                        {
                            // Parse a field definition
                            RejectQualifiers(qualifiers, "This qualifier does not apply to a field", sFieldQualifiers);
                            ParseIdentifier("Expecting a variable name or keyword such as 'class', etc.", sRejectStatement, out var fieldName);
                            CheckPublicQualifier(fieldName, qualifiers);
                            if (outerKeyword == "interface")
                                RejectToken(mToken, "Fields are not allowed inside an interface");
                            mUnit.Fields.Add(ParseField(qualifiers, comments, fieldName, outerKeyword == "enum"));
                            SkipSemicolon();
                        }
                        break;
                }
            }
        }

        void ParseQualifiers(List<Token> acceptedQualifiers, WordMap<int> qualifiers)
        {
            int sortOrder = 0;
            while (qualifiers.TryGetValue(mTokenName, out var newSortOrder))
            {
                // Verify no duplicates
                foreach (var token in acceptedQualifiers)
                    if (token == mTokenName)
                        RejectToken(mToken, "Cannot have duplicate qualifiers");

                // Verify sort order
                acceptedQualifiers.Add(mToken);
                if (newSortOrder == sortOrder)
                    RejectToken(mToken, "The '" + mToken + "' qualifier cannot be combined with '" + mPrevToken + "'");
                if (newSortOrder < sortOrder)
                    RejectToken(mToken, "The '" + mToken + "' qualifier must come before '" + mPrevToken + "'");
                sortOrder = newSortOrder;
                Accept();
            }
        }

        // Reject tokens with errorMessage.  Reject all of them if acceptSet is null.
        void RejectQualifiers(List<Token> qualifiers, string errorMessage, WordSet acceptSet = null)
        {
            foreach (var token in qualifiers)
            {
                if (token == "readonly")
                {
                    RejectToken(token, "Use 'ro' instead");
                    continue;
                }
                if (acceptSet == null || !acceptSet.Contains(token))
                    RejectToken(token, errorMessage);
            }
        }

        void CheckPublicQualifier(Token token, List<Token> qualifiers)
        {
            if (token == null || token.Name == "")
                return;
            foreach (var qualifier in qualifiers)
            {
                if (qualifier == "public" && char.IsUpper(token.Name[0]))
                {
                    qualifier.Grayed = true;
                    qualifier.AddInfo("'public' is not needed because the name is upper case");
                }
                if (qualifier == "private" && char.IsLower(token.Name[0]))
                {
                    qualifier.Grayed = true;
                    qualifier.AddInfo("'private' is not needed because the name is lower case");
                }
            }
        }

        SyntaxUsing ParseUsingStatement()
        {
            var synUsing = new SyntaxUsing();
            synUsing.Keyword = Accept();
            synUsing.QualifiedIdentifiers = TryParseQualifiedIdentifier("Expecting a namespace identifier", sRejectStatement);
            return synUsing;
        }

        SyntaxNamespace ParseNamespaceStatement(SyntaxUnit unit, string []comments)
        {
            var nspace = new SyntaxNamespace();
            nspace.Comments = comments;
            nspace.Keyword = Accept();
            nspace.QualifiedIdentifiers = TryParseQualifiedIdentifier("Expecting a namespace identifier", sRejectStatement);
            return nspace;
        }

        /// <summary>
        /// Returns null if there are no attributes
        /// </summary>
        SyntaxExpr ParseAttributes()
        {
            if (mTokenName != "[")
                return null;
            var openToken = mToken;
            var attributes = new List<SyntaxExpr>();
            while (AcceptMatch("["))
            {
                if (mToken == "]")
                {
                    RejectToken(mToken, "Attribute list may not be empty");
                }
                else
                {
                    attributes.Add(ParseExpr());
                    while (AcceptMatch(","))
                        attributes.Add(ParseExpr());
                }
                if (!AcceptMatch("]"))
                    RejectToken(mToken, "Expecting ']', end of attributes");
            }
            return new SyntaxExprMulti(openToken, attributes.ToArray());
        }

        // Parse class, struct, interface, or enum
        SyntaxClass ParseClass(List<Token> qualifiers, string []comments)
        {
            var synClass = new SyntaxClass();
            synClass.Comments = comments;
            synClass.Keyword = Accept();
            synClass.Qualifiers = qualifiers.ToArray();
            var classIdentifier = mToken;
            synClass.ClassName = ParseTypeDef(sTypeDefQualifiers, sRejectStatement);
            ShowParseTree(synClass.ClassName);

            // Parse base type
            if (AcceptMatch("extends"))
            {
                TryParseTypeName(out synClass.BaseClass, sRejectStatement);
                ShowParseTree(synClass.BaseClass);
            }

            // Parse implements
            if (AcceptMatch("implements"))
            {
                var imp = new List<SyntaxExpr>();
                do
                {
                    if (TryParseTypeName(out var implementType, sRejectStatement))
                        imp.Add(implementType);
                } while (AcceptMatch(","));
                synClass.Implements = imp.ToArray();
            }

            // Parse constraints
            if (mTokenName == "where")
            {
                List<SyntaxConstraint> constraints = new List<SyntaxConstraint>();
                while (mTokenName == "where")
                    constraints.Add(ParseConstraint());
                synClass.Constraints = constraints.ToArray();
            }
            if (mTokenName == ";")
            {
                RejectToken(mToken, "Empty body not allowed");
                SkipSemicolon();
                return synClass;
            }
            if (AcceptMatch("="))
            {
                TryParseTypeName(out synClass.Alias, sRejectStatement);
                ShowParseTree(synClass.Alias);
                SkipSemicolon();
                return synClass;
            }
            if (mTokenName != "{")
                Reject("Expecting start of " + synClass.Keyword + " body, '{' or ';'", sRejectStatement);
            if (mTokenName != "{")
                return synClass;

            // Parse class body
            var openToken = Accept();
            ParseScopeStatements(synClass.Keyword.Name);

            if (mTokenName != "}")
            {
                Reject("Expecting '}' while parsing " + synClass.Keyword.Name + " body"
                    + (classIdentifier.Type == eTokenType.Identifier ? " of '" + classIdentifier + "'" : ""), sRejectStatement);
                RejectToken(openToken, mTokenName == "" ? "This scope has no closing brace" 
                                                        : "This scope has an error on its closing brace");
            }
            if (AcceptMatch("}"))
                Connect(openToken, mPrevToken);
            return synClass;
        }


        /// <summary>
        /// Returns NULL if it fails
        /// </summary>
        SyntaxExpr ParseTypeDef(WordSet qualifiers, WordSet errorStop)
        {
            // TBD: Collect qualifiers, store in AST, but keep the
            //      name identifier as the primary token
            while (qualifiers.ContainsKey(mTokenName))
                Accept();

            if (!ParseIdentifier("Expecting a type name", sRejectStatement, out var typeName))
                return null;

            if (mToken != "<")
                return new SyntaxExprToken(typeName);

            var typeParams = new List<SyntaxExpr>();
            var openToken = Accept();
            bool moreParams = true;
            while (moreParams)
            {
                // Optional parameter can be 'in' or 'out'
                Token qualifier = null;
                if (sTypeDefParamQualifiers.Contains(mTokenName))
                    qualifier = Accept();

                if (!ParseIdentifier("Expecting a type name", sRejectStatement, out var typeParamName))
                    return null;

                if (qualifier == null)
                    typeParams.Add(new SyntaxExprToken(typeParamName));
                else
                    typeParams.Add(new SyntaxExprUnary(typeParamName, new SyntaxExprToken(qualifier)));

                moreParams = AcceptMatch(",");
            }
            if (!AcceptMatch(">"))
            {
                Reject("Expecing end of type parameters: '>'", errorStop);
                return null;
            }
            Connect(mPrevToken, openToken);
            return new SyntaxExprMulti(typeName, typeParams.ToArray());
        }

        SyntaxConstraint ParseConstraint()
        {
            var constraint = new SyntaxConstraint();
            constraint.Keyword = Accept();
            if (!ParseIdentifier("Expecting a type name", sRejectStatement, out constraint.GenericTypeName))
                return constraint;
            if (!AcceptMatch("is"))
            {
                RejectToken(mToken, "Expecting 'is'");
                return constraint;
            }
            var constraintTypeNames = new List<SyntaxExpr>();
            do
            {
                if (sAllowConstraintKeywords.Contains(mToken))
                {
                    mToken.Type = eTokenType.Reserved;
                    constraintTypeNames.Add(new SyntaxExprToken(Accept()));
                    continue;
                }
                if (TryParseTypeName(out var typeName, sRejectStatement))
                    constraintTypeNames.Add(typeName);
            } while (AcceptMatch(","));
            constraint.TypeNames = constraintTypeNames.ToArray();
            return constraint;                       
        }

        SyntaxField ParseField(List<Token> qualifiers, string []comments, Token fieldName, bool isEnum)
        {
            var field = new SyntaxField();
            field.Qualifiers = qualifiers.ToArray();
            field.Comments = comments;
            field.Name = fieldName;

            // Fields must have the type name, enum must not have type name
            if (!isEnum && mTokenName == "=")
                RejectToken(mToken, "Expecting a type name");
            if (isEnum && mTokenName != "=")
                RejectToken(mToken, "Expecting '=', enum field must not have a type name");

            // Parse type name
            if (mTokenName != "=")
                TryParseTypeNameWithQualifiers(out field.TypeName, sFieldDefTypeQualifiers, sRejectStatement);

            if (AcceptMatch("="))
            {
                // Parse initialization expression
                mIdentifierAllowedReservedWords = isEnum ? null : sAllowNewKeyword;
                var eqToken = mPrevToken;
                field.InitExpr = ParseExpr();
                ShowParseTree(field.InitExpr, eqToken);
                mIdentifierAllowedReservedWords = null;
            }
            ShowParseTree(field.TypeName, field.Name);
            return field;
        }

        SyntaxExpr ShowParseTree(SyntaxExpr expr, Token infoToken = null)
        {
            if (!SHOW_PARSE_TREE || expr == null)
                return expr;
            var info = "Parse tree: " + expr.ToString();
            expr.Token.AddInfo(info);
            if (infoToken != null)
                infoToken.AddInfo(info);            
            foreach (var e in expr)
                ShowParseTree(e, null); // Subtrees without info token
            return expr;
        }

        /// <summary>
        /// Func, construct, operator, get, set
        /// </summary>
        SyntaxFunc ParseMethodDef(List<Token> qualifiers, string []comments, bool isInterface)
        {
            // Parse func keyword
            var synFunc = new SyntaxFunc();
            synFunc.Comments = comments;
            synFunc.Qualifiers = qualifiers.ToArray();
            synFunc.Keyword = Accept();
            string keyword = mPrevToken.Name;
            mPrevToken.Type = eTokenType.ReservedControl;

            switch (keyword)
            {
                case "func": ParseFuncDef(synFunc); break;
                case "construct": ParseConstructDef(synFunc); break;
                case "set": // TBD: No type params, require to be after "get"
                case "get": ParseGetSetDef(synFunc); break;
                case "operator": ParseOperatorDef(synFunc);  break;
                default:
                    throw new Exception("Error parsing function def");
            }

            ShowParseTree(synFunc.FuncName);
            ShowParseTree(synFunc.Return);

            if (mToken == ";")
            {
                if (!isInterface)
                    RejectTokenIfNotQualified(mToken, qualifiers, "extern", "Expecting '{', or a function without a body must have the'extern' qualifier");
                SkipSemicolon();
                return synFunc;
            }
            if (mToken != "{")
                Reject("Expecting start of function body, '{' or ';'", sRejectStatement);

            if (mToken == "{")
            {
                if (isInterface)
                    RejectToken(mToken, "Interface may not contain function body");
                synFunc.Statements = ParseFuncStatements();
            }
            return synFunc;
        }

        private void ParseFuncDef(SyntaxFunc synFunc)
        {
            // Try parsing a class name first (Optional, must be followed by "::")
            var pp = SaveParsePoint();
            if (TryParseTypeName(out var className, null) && AcceptMatch("::"))
                synFunc.ClassName = className;
            else
                RestoreParsePoint(pp);

            // Parse function name, possibly generic
            synFunc.FuncName = ParseTypeDef(sEmptyWordSet, sRejectFuncName);

            // Parse parameters
            if (mToken != "(" && mToken != "[")
                Reject("Expecting '(' or '['", sRejectFuncName);
            if (mToken == "(" || mToken == "[")
                synFunc.Params = ParseFuncDefParams();

            // Parse return type
            if (mTokenName != "{" && mTokenName != ";")
                TryParseTypeNameWithQualifiers(out synFunc.Return, sFuncDefReturnTypeQualifiers, sRejectFuncParam);
        }

        private void ParseConstructDef(SyntaxFunc synFunc)
        {
            synFunc.FuncName = new SyntaxExprToken(mToken);
            if (mToken != "(")
                Reject("Expecting '(' after 'new' token", sRejectFuncName);

            // Parse parameters
            if (mToken == "(")
                synFunc.Params = ParseFuncDefParams();
        }

        private void ParseGetSetDef(SyntaxFunc synFunc)
        {
            synFunc.GetOrSetToken = mPrevToken;

            ParseIdentifier("Expecting property name", sRejectFuncName, out var propName, sAllowThisKeyword);
            synFunc.FuncName = new SyntaxExprToken(propName);

            // Require array parameters for indexer
            if (propName == "this")
            {
                if (mToken != "[")
                    Reject("Expecting '['", sRejectFuncName);
                if (mToken == "[")
                    synFunc.Params = ParseFuncDefParams();
            }

            // Parse property type
            if (mTokenName == "{" || mTokenName == ";")
                RejectToken(mToken, "Expecting type name");
            TryParseTypeNameWithQualifiers(out synFunc.Return, sFuncDefReturnTypeQualifiers, sRejectFuncParam);
        }

        private void ParseOperatorDef(SyntaxFunc synFunc)
        {
            // Function name is an operator
            synFunc.FuncName = new SyntaxExprToken(mToken);
            if (sOverloadableOperators.Contains(mTokenName))
                Accept();
            else
                Reject("Expecting an overloadable operator", sRejectFuncName);

            // Parse parameters
            if (mToken != "(" && mToken != "[")
                Reject("Expecting '(' or '['", sRejectFuncName);
            if (mToken == "(" || mToken == "[")
                synFunc.Params = ParseFuncDefParams();

            // Parse return type
            if (mTokenName != "{" && mTokenName != ";")
                TryParseTypeNameWithQualifiers(out synFunc.Return, sFuncDefReturnTypeQualifiers, sRejectFuncParam);
        }

        SyntaxFuncParam []ParseFuncDefParams()
        {
            // Read open token, '(' or '['
            var openToken = Accept();
            if (openToken != "(" && openToken != "[")
                throw new Exception("Compiler error: Expecting '[' or '(' while parsing function parameters");

            // Parse parameters
            var closeToken = openToken.Name == "(" ? ")" : "]";
            var parameters = new List<SyntaxFuncParam>();
            if (mTokenName != closeToken)
                parameters.Add(ParseFuncDefParam());
            while (AcceptMatch(","))
            {
                Connect(openToken, mPrevToken);
                parameters.Add(ParseFuncDefParam());
            }

            if (mTokenName != closeToken)
                Reject("Expecting '" + closeToken + "'", closeToken == ")" ? sRejectFuncParamsParen : sRejectFuncParamsBracket);
            if (AcceptMatch(closeToken))
                Connect(openToken, mPrevToken);
            return parameters.ToArray();
        }

        SyntaxFuncParam ParseFuncDefParam()
        {
            var synParam = new SyntaxFuncParam();
            if (!ParseIdentifier("Expecting a variable name", sRejectFuncParam, out synParam.Name))
                return synParam;
            TryParseTypeNameWithQualifiers(out synParam.TypeName, sFuncDefParamTypeQualifiers, sRejectFuncParam);
            ShowParseTree(synParam.TypeName, synParam.Name);
            return synParam;
        }

        private SyntaxExpr ParseFuncStatements()
        {
            // Read open token, '{'
            if (!AcceptMatch("{"))
            {
                RejectToken(mToken, "Expecting '{'");
                return new SyntaxExprToken(mToken);
            }
            var openToken = mPrevToken;
            var statements = new List<SyntaxExpr>();
            while (!sStatementsDone.Contains(mTokenName))
            {
                ParseStatement(statements);
            }
            if (AcceptMatch("}"))
                Connect(openToken, mPrevToken);
            else
                RejectToken(mToken, "Expecting '}'");

            return new SyntaxExprMulti(openToken, statements.ToArray());

        }

        private void ParseStatement(List<SyntaxExpr> statements)
        {
            var keyword = mToken;
            switch (mToken)
            {
                case ";": SkipSemicolon(); break;

                case "{":
                    statements.Add(ParseFuncStatements());
                    break;

                case "while":
                case "if":
                    Accept();
                    var conditionExpr = ParseExpr();
                    ShowParseTree(conditionExpr);
                    var bodyExpr = ParseFuncStatements();
                    if (mToken == "else" && keyword == "if")
                    {
                        // IF (condition) (if-body) (else-body)
                        Accept();
                        statements.Add(new SyntaxExprMulti(keyword,
                            new SyntaxExpr[] { conditionExpr, bodyExpr, ParseFuncStatements() }));
                    }
                    else
                    {
                        // IF/WHILE (condition) (body)
                        statements.Add(new SyntaxExprBinary(keyword, conditionExpr, bodyExpr));
                    }
                    break;

                case "else":
                    Reject("Else must follow 'if' statement", sRejectStatement);
                    break;

                case "for":
                    Reject("Not parsed yet", sRejectStatement);
                    SkipSemicolon();
                    break;

                case "return":
                    Accept();
                    if (mTokenName == ";" || mTokenName == "}")
                        statements.Add(new SyntaxExprToken(keyword));
                    else
                        statements.Add(new SyntaxExprUnary(keyword, ShowParseTree(ParseExpr())));
                    SkipSemicolon();
                    break;

                case "throw":
                    Accept();
                    statements.Add(new SyntaxExprUnary(keyword, ShowParseTree(ParseExpr())));
                    break;

                default:
                    var result = ParseExpr();
                    if (sAssignOperators.Contains(mToken))
                        result = new SyntaxExprBinary(Accept(), result, ParseExpr());
                    ShowParseTree(result);
                    statements.Add(result);
                    SkipSemicolon();
                    break;
            }
        }

        ///  Parse expression (doesn't include ',' or '=' statements)
        public SyntaxExpr ParseExpr()
        {
            return ParseLambda();
        }

        SyntaxExpr ParseLambda()
        {
            var result = ParseTernary();
            if (mTokenName == "->")
            {
                var lambdaToken = mToken;
                result = new SyntaxExprBinary(Accept(), result, ParseTernary());
                if (mTokenName == "->")
                    Reject("Lambda operator is not associative, must use parentheses", sRejectStatement);
                RejectToken(mToken, "Lambda not yet supported, use 'func' keyword instead");
            }
            return result;
        }

        SyntaxExpr ParseTernary()
        {
            var result = ParseConditionalOr();
            if (mTokenName == "?")
            {
                Token operatorToken = Accept();
                var firstConditional = ParseConditionalOr();
                if (mTokenName != ":")
                {
                    Connect(mToken, operatorToken);
                    Reject("Expecting a ':' to separate expression for the ternary '?' operator", sRejectStatement);
                    return result;
                }
                Accept();
                result = new SyntaxExprMulti(operatorToken, new SyntaxExpr[] { result, firstConditional, ParseConditionalOr() });
            }
            return result;
        }

        SyntaxExpr ParseConditionalOr()
        {
            var result = ParseConditionalAnd();
            while (mTokenName == "||")
                result = new SyntaxExprBinary(Accept(), result, ParseConditionalAnd());
            return result;
        }

        SyntaxExpr ParseConditionalAnd()
        {
            var result = ParseComparison();
            while (mTokenName == "&&")
                result = new SyntaxExprBinary(Accept(), result, ParseComparison());
            return result;
        }

        SyntaxExpr ParseComparison()
        {
            var result = ParseRange();
            if (sComparisonOperators.Contains(mTokenName)
                && (mTokenName != ">" || InterceptAndReplaceToken("=", VIRTUAL_TOKEN_GE)) )
            {
                result = new SyntaxExprBinary(Accept(), result, ParseRange());
                if (sComparisonOperators.Contains(mTokenName))
                    Reject("Compare operators are not associative, must use parentheses", sRejectStatement);
            }
            return result;
        }

        SyntaxExpr ParseRange()
        {
            var result = ParseAdd();
            if (mTokenName == "..")
            {
                result = new SyntaxExprBinary(Accept(), result, ParseAdd());
                if (mTokenName == "..")
                    Reject("Range operator is not associative, must use parentheses", sRejectStatement);
            }
            return result;
        }

        SyntaxExpr ParseAdd()
        {
            var result = ParseMultiply();
            while (sAddOperators.Contains(mTokenName))
                result = new SyntaxExprBinary(Accept(), result, ParseMultiply());
            return result;
        }

        SyntaxExpr ParseMultiply()
        {
            var result = ParseExpon();
            while (sMultiplyOperators.Contains(mTokenName))
            {
                if (mTokenName == ">" && !InterceptAndReplaceToken(">", VIRTUAL_TOKEN_SHIFT_RIGHT))
                    break;

                // Intercept '>' and combine to '>>' if possible (otherwise ignore it)
                result = new SyntaxExprBinary(Accept(), result, ParseExpon());
            }
            return result;
        }

        bool InterceptAndReplaceToken(string match, string replace)
        {
            var peek = mLexerEnum.PeekOnLine();
            if (peek.Name != match || peek.Char != mToken.Char + 1)
                return false;

            // Replace with a virtual token
            Accept();  // Skip first '>'
            mToken = new Token(replace, mToken.Line, mToken.Char);
            return true;
        }

        SyntaxExpr ParseExpon()
        {
            var result = ParseUnary();
            // TBD: Do we want exponentiation?
            //while (mTokenName == "**")
            //    result = new SyntaxExprBinary(Accept(), result, ParseUnary());
            return result;
        }

        SyntaxExpr ParseUnary()
        {
            if (sUnaryOperators.Contains(mTokenName))
            {
                return new SyntaxExprUnary(Accept(), ParseUnary());
            }
            return ParsePrimary();
        }

        SyntaxExpr ParsePrimary()
        {
            var result = ParseAtom();
            bool accepted;
            do
            {
                accepted = false;
                if (mTokenName == "(" || mTokenName == "[")
                {
                    // Function call or array access
                    accepted = true;
                    var openToken = mToken;
                    var parameters = new List<SyntaxExpr>() { result };
                    ParseFuncCallParameters(parameters);
                    result = new SyntaxExprMulti(openToken, parameters.ToArray());
                }
                else if (AcceptMatch("."))
                {
                    // Member access
                    accepted = true;
                    result = new SyntaxExprBinary(mPrevToken, result,
                        new SyntaxExprToken(ParseIdentifier("Expecting identifier", sRejectStatement)));
                }
                else if (mTokenName == "<" && mPrevToken.Type == eTokenType.Identifier)
                {
                    // Possibly a type argument list.  Let's try it and find out.
                    var p = SaveParsePoint();
                    if (TryParseTypeArgumentList(out var typeArguments, null) && sTypeArgumentParameterSymbols.Contains(mTokenName))
                    {
                        // Type argument list
                        accepted = true;
                        typeArguments.Insert(0, result);
                        result = new SyntaxExprMulti(new Token(VIRTUAL_TOKEN_TYPE_ARG_LIST, 0, 0), typeArguments.ToArray());
                    }
                    else
                    {
                        // Failed, restore the enumerator back to before trying type argument list
                        RestoreParsePoint(p);
                    }
                }
            } while (accepted);
            return result;
        }

        /// <summary>
        /// Parse an atom - identifier, number, string literal, or parentheses
        /// </summary>
        SyntaxExpr ParseAtom()
        {
            if (mTokenName == "")
            {
                Reject("Unexpected end of file", sRejectStatement);
                return new SyntaxExprToken(mToken);
            }

            // Parse parentheses, expression or cast (not function call)
            if (mTokenName == "(")
            {
                // Try expression first since that's the most likely
                var pp = SaveParsePoint();
                mParseError = false;
                var result = ParseParen();
                if (!mParseError && mToken.Type != eTokenType.Identifier && mTokenName != "(")
                {
                    // Valid expression, not followed by identifier or '('
                    mParseError = pp.ParseError; // Restore parse error state from before
                    return result;
                }

                // Parse a cast, closing ')' is followed by '(' or identifier
                RestoreParsePoint(pp);
                var castOpen = Accept();
                if (!TryParseTypeName(out var castExpr, sRejectStatement))
                    return result; // Error, just use any expression

                if (!AcceptMatch(")"))
                {
                    Reject("End of cast, expecting ')' after type name", sRejectStatement);
                    return castExpr;
                }
                // Use ')' to differentiate from function call which is '('
                Connect(castOpen, mPrevToken);
                return new SyntaxExprBinary(mPrevToken, castExpr, ParsePrimary());
            }

            // Parse number or string
            if (char.IsDigit(mTokenName, 0) || mTokenName[0] == '"')
                return new SyntaxExprToken(Accept());

            // Parse variable name
            if (char.IsLetter(mTokenName, 0))
            {
                return new SyntaxExprToken(ParseIdentifier("Expecting an identifier", sRejectStatement));
            }
            var errorToken = mToken;
            Reject("Expecting an identifier, number, string literal, or parentheses", sRejectStatement);
            return new SyntaxExprToken(errorToken);
        }

        /// <summary>
        /// Read the open '(' or '[' and parse the expression.
        /// Returns the expression that was parsed.
        /// Reject empty '()' or '[]'
        /// </summary>
        SyntaxExpr ParseParen()
        {
            // Read open token, '(' or '['
            var openToken = Accept();
            if (openToken != "(" && openToken != "[")
                throw new Exception("Compiler error: Expecting '[' or '(' while parsing paren");

            // Read open '(' or '[' and create an Expr
            var expectedToken = openToken == "(" ? ")" : "]";
            var rejectTokens = openToken == "(" ? sRejectParen : sRejectBracket;

            // Reject empty () or []
            if (mTokenName == expectedToken)
            {
                // Return an empty () or []
                Connect(openToken, mToken);
                Reject("Expecting an expression", rejectTokens);
                var emptyExpr = new SyntaxExprToken(Accept());
                return emptyExpr;
            }

            // Parse the expression
            SyntaxExpr result = ParseExpr();
            if (mTokenName != expectedToken)
            {
                // The rest of the line is rejected
                Reject("Expecting '" + expectedToken + "'", rejectTokens);
            }
            if (mTokenName == expectedToken)
            {
                Connect(openToken, mToken);
                Accept();
            }
            return result;
        }

        /// <summary>
        /// Read the open '(' or '[' and then parse the parameters into parameters
        /// </summary>
        void ParseFuncCallParameters(List<SyntaxExpr> parameters)
        {
            // Read open token, '(' or '['
            var openToken = Accept();
            if (openToken != "(" && openToken != "[")
                throw new Exception("Compiler error: Expecting '(' or '[' while parsing parameters");

            // Empty () function call?
            var expectedToken = openToken == "(" ? ")" : "]";
            if (mTokenName == expectedToken)
            {
                // Return an empty () or []
                Connect(openToken, Accept());
                return;
            }

            // Parse parameters
            var isFunc = openToken == "(";
            parameters.Add(isFunc ? ParseFuncCallParameter() : ParseExpr());
            while (AcceptMatch(","))
                parameters.Add(isFunc ? ParseFuncCallParameter() : ParseExpr());

            // If not ended properly, reject this expression
            if (mTokenName != expectedToken)
                Reject("Expecting '" + expectedToken + "' or ','",
                    openToken == "(" ? sRejectParen : sRejectBracket);

            if (AcceptMatch(expectedToken))
                Connect(openToken, mPrevToken);
        }

        SyntaxExpr ParseFuncCallParameter()
        {
            // Allow 'ref' or 'out' qualifier
            Token qualifier = null;
            if (sFuncCallParamQualifiers.Contains(mTokenName))
                qualifier = Accept();

            var expr = ParseExpr();
            if (qualifier != null)
                expr = new SyntaxExprUnary(qualifier, expr);
            if (AcceptMatch(":"))
            {
                // Parse named parameter
                var colonToken = mPrevToken;
                if (expr.Count != 0 || expr.Token.Type != eTokenType.Identifier)
                    RejectToken(colonToken, "Expecting one identifier before the named parameter");
                if (qualifier != null)
                    RejectToken(qualifier, "Qualifier cannot come before a named parameter");

                // Allow 'ref' or 'out' qualifier
                if (sFuncCallParamQualifiers.Contains(mTokenName))
                    qualifier = Accept();

                var exprRight = ParseExpr();
                if (qualifier != null)
                    exprRight = new SyntaxExprUnary(qualifier, exprRight);
                expr = new SyntaxExprBinary(colonToken, expr, exprRight);
            }

            return expr;
        }


        /// <summary>
        /// Error causes reject until errorStop unless errorStop is null.
        /// </summary>
        bool TryParseTypeNameWithQualifiers(out SyntaxExpr result, WordSet qualifiers, WordSet errorStop)
        {
            result = null;
            if (qualifiers.Contains(mTokenName))
            {
                var token = Accept();
                if (!TryParseTypeNameWithQualifiers(out var expr, qualifiers, errorStop))
                    return false;
                result = new SyntaxExprUnary(token, expr);
                return true;
            }
            return TryParseTypeName(out result, errorStop);
        }

        /// <summary>
        /// Error causes reject until errorStop unless errorStop is null.
        /// </summary>
        bool TryParseTypeName(out SyntaxExpr result, WordSet errorStop)
        {
            // Unary operators '*' and '[]', short for Pointer<type> and Array<type>
            // Treat qualifiers `in`, `out`, `ref`, `ro` similar to unary operators
            result = null;
            if (mToken == "*" || mToken == "[")
            {
                var token = Accept();
                if (token.Name == "[" && !AcceptMatch("]"))
                {
                    // Unaray array operator is always '[]', so swallow the ']'
                    if (errorStop != null)
                        Reject("Expecting ']'", errorStop);
                    return false;
                }
                if (!TryParseTypeName(out var expr, errorStop))
                    return false;
                result = new SyntaxExprUnary(token, expr);
                return true;
            }

            if (!ParseIdentifier("Expecting a type name", errorStop, out var typeName))
                return false;

            result = new SyntaxExprToken(typeName);

            bool accepted;
            do
            {
                accepted = false;
                if (AcceptMatch("."))
                {
                    accepted = true;
                    var dotToken = mPrevToken;
                    if (!ParseIdentifier("Expecting a type name", errorStop, out var dotTypeName))
                        return false;
                    result = new SyntaxExprBinary(dotToken, result, new SyntaxExprToken(dotTypeName));
                }

                if (mToken == "<")
                {
                    accepted = true;
                    var openToken = mToken;
                    if (!TryParseTypeArgumentList(out var expr, errorStop))
                        return false;
                    expr.Insert(0, result);
                    result = new SyntaxExprMulti(openToken, expr.ToArray());
                }
            } while (accepted);
            return true;
        }

        /// <summary>
        /// Try parsing a type argument list. 
        /// Error causes reject until errorStop unless errorStop is null.
        bool TryParseTypeArgumentList(out List<SyntaxExpr> arguments, WordSet errorStop)
        {
            var openToken = Accept();
            if (openToken.Name != "<")
                throw new Exception("Compiler error: Expecting '<' while parsing type argument list");

            // Parse the first parameter
            arguments = new List<SyntaxExpr>();
            if (!TryParseTypeName(out var p, errorStop))
                return false;
            arguments.Add(p);

            // Parse the rest of the parameters
            while (AcceptMatch(","))
            {
                if (!TryParseTypeName(out p, errorStop))
                    return false;
                arguments.Add(p);
            }

            if (!AcceptMatch(">"))
            {
                if (errorStop != null)
                    Reject("Expecting '>' to end the type argument list", errorStop);
                return false;
            }
            Connect(openToken, mPrevToken);
            return true;
        }
        
        void SkipSemicolon()
        {
            if (AcceptMatch(";"))
                return;
            if (mToken == "}")
                return;

            Reject("Expecting ';'", sRejectStatement);
            AcceptMatch(";");
        }

        /// <summary>
        /// Parse a qualified identifier.  
        /// Error causes reject until errorStop and returns null.
        /// </summary>
        SyntaxExpr TryParseQualifiedIdentifier(string errorMessage, WordSet errorStop)
        {
            // Parse first identifier
            if (!ParseIdentifier(errorMessage, errorStop, out var t1))
                return null;
            var identifier = new SyntaxExprToken(t1);
            if (mTokenName != ".")
                return identifier;

            // Parse the rest
            var dotToken = mToken;
            var tokens = new List<SyntaxExpr>();
            tokens.Add(identifier);
            while (AcceptMatch("."))
            {
                if (!ParseIdentifier(errorMessage, errorStop, out var t2))
                    return null;
                tokens.Add(new SyntaxExprToken(t2));
            }
            return new SyntaxExprMulti(dotToken, tokens.ToArray());
        }

        /// <summary>
        /// Parse an identifier.  Error causes reject until errorStop unless errorStop is null.
        /// </summary>
        Token ParseIdentifier(string errorMessage, WordSet errorStop)
        {
            ParseIdentifier(errorMessage, errorStop, out var token);
            return token;
        }

        /// <summary>
        /// Parse an identifier.  Error returns false and causes
        /// reject until errorStop unless errorStop is null.
        /// </summary>
        bool ParseIdentifier(string errorMessage, WordSet errorStop, out Token token, WordSet allowedReserved = null)
        {
            token = mToken;
            if (mToken.Type == eTokenType.Identifier
                || allowedReserved != null && allowedReserved.Contains(mToken)
                || mIdentifierAllowedReservedWords != null && mIdentifierAllowedReservedWords.Contains(mToken))
            {
                Accept();
                return true;
            }
            if (errorStop != null)
            {
                if (mToken.Type == eTokenType.Reserved || mToken.Type == eTokenType.ReservedControl)
                    Reject(errorMessage + ", must not be a reserved word", errorStop);
                else
                    Reject(errorMessage + ", must begin with a letter", errorStop);
            }
            return false;
        }

        // Accept the token if it matches.  Returns true if it was accepted.
        bool AcceptMatch(string matchToken)
        {
            if (mTokenName == matchToken)
            {
                Accept();
                return true;
            }
            return false;
        }

        struct ParsePoint
        {
            public Lexer.Enumerator LexerEnum;
            public Token PrevToken;
            public Token Token;
            public bool ParseError;
        }

        ParsePoint SaveParsePoint()
        {
            var p = new ParsePoint();
            p.LexerEnum = mLexerEnum;
            p.PrevToken = mPrevToken;
            p.Token = mToken;
            p.ParseError = mParseError;
            return p;
        }

        void RestoreParsePoint(ParsePoint p)
        {
            mLexerEnum = p.LexerEnum;
            mPrevToken = p.PrevToken;
            mToken = p.Token;
            mTokenName = mToken.Name;
            mParseError = p.ParseError;
        }

        // Accept the current token and advance to the next, skipping all comments.
        // The new token is saved in mToken and the token name is saved in mTokenName.
        // Returns the token that was accepted.  Token is pre-maked with token type
        Token Accept()
        {
            // Already at end of file?
            mPrevToken = mToken;
            if (mTokenName == "")
                return mPrevToken;

            // Read next token, and skip comments
            var firstComment = true;
            do
            {
                // Read next token (set EOF flag if no more tokens on line)
                if (mLexerEnum.MoveNext())
                    mToken = mLexerEnum.Current;
                else
                    mToken = new Token("", 0, 0, eTokenType.Reserved);

                if (mToken.Type == eTokenType.Comment && mTokenName.StartsWith("///"))
                {
                    if (firstComment)
                        mComments.Clear();
                    firstComment = false;
                    mComments.Add(mToken.Name);
                }

            } while (mToken.Type == eTokenType.Comment);
            
            // Reset token info
            mTokenName = mToken.Name;
            mToken.ClearInfo();
            mToken.ClearBits();
            if (mTokenName.Length == 0)
                mToken.Type = eTokenType.Normal;
            else if (mTokenName[0] == '\"')
                mToken.Type = eTokenType.Quote;
            else if (sReservedWords.TryGetValue(mTokenName, out var tokenType))
                mToken.Type = tokenType;
            else if (mTokenName[0] >= '0' && mTokenName[0] <= '9')
                mToken.Type = eTokenType.Number;
            else if (char.IsLetter(mTokenName[0]))
                mToken.Type = eTokenType.Identifier;
            else
                mToken.Type = eTokenType.Normal;

            return mPrevToken;
        }
      
        // Reject the token if it's not qualified
        void RejectTokenIfNotQualified(Token token, IEnumerable<Token> qualifiers, string expected, string errorMessage)
        {
            foreach (var t in qualifiers)
                if (t.Name == expected)
                    return;
            RejectToken(token, errorMessage);
        }

        // Reject the given token
        void RejectToken(Token token, string errorMessage)
        {
            mParseError = true;
            token.Reject(errorMessage);

            // If the error is after the end of file, put it on the last visible token
            if (token.Name == "")
                mPrevToken.Reject(errorMessage);
        }

        // Reject the current token, then advance until the first stopToken
        void Reject(string errorMessage, WordSet extraStops)
        {
            RejectToken(mToken, errorMessage);
            while (!sRejectAnyStop.Contains(mToken) && !extraStops.Contains(mToken))
            {
                mToken.Grayed = true;
                Accept();
            }
        }

        /// <summary>
        /// Connect the tokens so the user sees the same info
        /// for both tokens (and both are grayed out when
        /// hovering with the mouse).  
        /// </summary>
        void Connect(Token s1, Token s2)
        {
            // Find tokens that are already connected
            List <Token> tokens = new List<Token>();
            Token []s1Connectors = s1.GetInfo<Token[]>();
            if (s1Connectors != null)
                foreach (Token s in s1Connectors)
                    tokens.Add(s);
            Token []s2Connectors = s2.GetInfo<Token[]>();
            if (s2Connectors != null)
                foreach (Token s in s2Connectors)
                    tokens.Add(s);

            // Add these tokens to the list
            tokens.Add(s1);
            tokens.Add(s2);

            // Set token info
            Token []sa = tokens.ToArray();
            foreach (Token s in sa)
                s.RepaceInfo(sa);
        }

    }

}
