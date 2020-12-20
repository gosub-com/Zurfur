using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    public class SilError : TokenError
    {
        public SilError(string message) : base(message) { }
    }
    public class SilWarn : TokenWarn
    {
        public SilWarn(string message) : base(message) { }
    }

    class SilGenHeader
    {
        static WordSet sTypeSymbols = new WordSet("? * ^ [ " + ParseZurf.VT_TYPE_ARG);
        static WordSet sTypeAttributes = new WordSet("in out mut ro ref");

        Dictionary<string, SymFile> mFiles = new Dictionary<string, SymFile>();
        SymbolTable mSymbols = new SymbolTable();
        Dictionary<string, SymSpecializedType> mSpecializedTypes = new Dictionary<string, SymSpecializedType>();
        Dictionary<string, SymSpecializedType> mFuncTypes = new Dictionary<string, SymSpecializedType>();

        Dictionary<string, SymTypeInfo> mSymTypeInfo = new Dictionary<string, SymTypeInfo>();

        List<string> mNamespaces = new List<string>();
        List<string> mAllSymbols = new List<string>();
        DateTime mStartTime = DateTime.Now;

        /// <summary>
        /// Step 1: GenerateTypeDefinitions, requires nothing from any other package.
        /// Step 2: ResolveTypeNames, requires type definitions from all other packages.
        /// Step 3: GenerateHeader.
        /// </summary>
        public SilGenHeader()
        {
        }


        /// <summary>
        /// Step 1: Find all the symbols in this package.
        /// Load namespaces, types, fields, and method group names.
        /// Each file is independent, requires nothing from other packages.
        /// The methods themselves can't be added until we have complete type info.
        /// </summary>
        public void EnumerateSymbols(Dictionary<string, SyntaxFile> syntaxFiles)
        {
            foreach (var syntaxFile in syntaxFiles)
                mFiles[syntaxFile.Key] = new SymFile(syntaxFile.Key, syntaxFile.Value);

            var preRoot = new SymNamespace(null, new SymFile("", new SyntaxFile()), new Token(""));
            mSymbols[""] = new SymNamespace(preRoot, new SymFile("", new SyntaxFile()), new Token(""));

            AddNamespaces();
            AddTypes();
            AddFields();
            AddMethodGroups();
            VerifyNoSameParentSymbols();
            return;

            void AddNamespaces()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var ns in symFile.SyntaxFile.Namespaces)
                    {
                        var symbol = AddNamespace(symFile, ns.Value.Path);
                        symbol.Comments += " " + ns.Value.Comments;
                    }
                }
            }

            Symbol AddNamespace(SymFile file, Token[] path)
            {
                var childrenSymbols = mSymbols[""].Children;
                Symbol parentSymbol = mSymbols[""];
                Symbol newNamespace = null;
                foreach (var token in path)
                {
                    if (childrenSymbols.TryGetValue(token.Name, out var childNamespace))
                    {
                        newNamespace = childNamespace;
                        newNamespace.AddLocation(file, token);
                    }
                    else
                    {
                        newNamespace = new SymNamespace(parentSymbol, file, token);
                        childrenSymbols[token] = newNamespace;
                        mSymbols[newNamespace.FullName] = newNamespace;
                        parentSymbol.Children[token.Name] = newNamespace;
                    }
                    Debug.Assert(mSymbols[newNamespace.FullName] == newNamespace);

                    parentSymbol = newNamespace;
                    childrenSymbols = newNamespace.Children;
                }
                return newNamespace;
            }

            void AddTypes()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var type in symFile.SyntaxFile.Types)
                    {
                        var newType = new SymType(mSymbols.FindPath(type.NamePath), symFile, type.Name);
                        newType.TypeKeyword = type.Keyword.Name;
                        newType.Comments = type.Comments;
                        if (mSymbols.Add(newType) && type.TypeParams.Count != 0)
                        {
                            // Add type arguments
                            var typeArgs = new List<string>();
                            foreach (var expr in type.TypeParams)
                            {
                                mSymbols.Add(new SymTypeArg(newType, symFile, expr.Token));
                                typeArgs.Add(expr.Token.Name);
                            }
                            // TBD: We need to do this because we need to maintain the
                            //      parameter order.  It might be better to keep an ordered
                            //      list of names in the symbol table.  We will need that
                            //      if we want to maintain field order.
                            newType.TypeArgs = typeArgs.ToArray();
                        }
                    }
                }
            }

            void AddFields()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var field in symFile.SyntaxFile.Fields)
                    {
                        var newField = new SymField(mSymbols.FindPath(field.NamePath), symFile, field.Name);
                        newField.Syntax = field;
                        newField.Comments = field.Comments;
                        mSymbols.Add(newField);
                    }
                }
            }

            void AddMethodGroups()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var method in symFile.SyntaxFile.Methods)
                    {
                        var parentSymbol = mSymbols.FindPath(method.NamePath);
                        if (!parentSymbol.Children.ContainsKey(method.Name))
                        {
                            var newMethod = new SymMethodGroup(parentSymbol, symFile, method.Name);
                            parentSymbol.Children[method.Name] = newMethod;
                            mSymbols[newMethod.FullName] = newMethod;
                        }
                    }
                }
            }

            void VerifyNoSameParentSymbols()
            {
                foreach (var symbol in mSymbols.Values)
                {
                    if (symbol.FullName != "" && symbol.Name == symbol.Parent.Name)
                        Reject(symbol.Token, "Name must not be same as parent symbol");
                }
            }

        }

        /// <summary>
        /// Step 2: Add methods and resolve type names.
        /// Requires symbols from external packages.
        /// TBD: Add way to include external package headers
        /// </summary>
        public void ResolveTypeNames()
        {
            ProcessUseStatements();
            ResolveFields();
            AddAndResolveMethodGroups();
            BuildTypeFieldInfo();
            return;

            void ProcessUseStatements()
            {
                foreach (var symFile in mFiles.Values)
                {
                    var useNamespaces = new List<string>();
                    foreach (var use in symFile.SyntaxFile.Using)
                    {
                        var symbol = mSymbols.FindPath(use.NamePath);
                        if (symbol == null || use.NamePath.Length == 0)
                            continue;  // Error marked by function

                        var lastToken = use.NamePath[use.NamePath.Length - 1];
                        if (!(symbol is SymNamespace))
                        {
                            Reject(lastToken, "Must be a namespace, not a " + symbol.Kind);
                            continue;
                        }

                        if (useNamespaces.Contains(symbol.FullName))
                        {
                            Reject(lastToken, "Already included in previous use statement");
                            continue;
                        }
                        useNamespaces.Add(symbol.FullName);
                    }
                    // TBD: Sort namespaces so packages are searched first
                    symFile.Use = useNamespaces.ToArray();
                }
            }

            void ResolveFields()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var field in symFile.SyntaxFile.Fields)
                    {
                        var symField = mSymbols.FindPath(field.NamePath).Children[field.Name] as SymField;
                        if (symField == null)
                        {
                            Reject(field.Name, "Compiler error"); // Shouldn't happen
                            continue;
                        }
                        // Skip enum and errors
                        if (field.TypeName == null || field.TypeName.Token.Name == "")
                        {
                            if (field.ParentScope.Keyword == "enum")
                                Warning(field.Name, "TBD: Process enum");
                            else
                                Reject(field.Name, "Expecting symbol to have an explicitly named type");
                            continue;
                        }
                        var fieldType = ResolveType(symField, field.TypeName, symFile);
                        if (fieldType != null)
                            symField.TypeName = fieldType.FullName;
                    }
                }
            }

            void AddAndResolveMethodGroups()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var func in symFile.SyntaxFile.Methods)
                    {
                        var group = mSymbols.FindPath(func.NamePath).Children[func.Name];
                        if (!(group is SymMethodGroup))
                        {
                            Reject(func.Name, "Duplicate symbol.  There is a " + group.Kind + " with the same name");
                            func.Name.AddInfo(group);
                        }
                        else
                        {
                            AddAndResolveMethod(group as SymMethodGroup, func, symFile);
                        }
                    }
                }
            }

            void AddAndResolveMethod(SymMethodGroup scope, SyntaxFunc func, SymFile file)
            {
                if (func.Params.Count != 4)
                {
                    Reject(func.Name, "TBD: Property"); // Should already be marked by parser
                    return;
                }
                var methodName = "_F" + mSymbols.Count;
                var method = new SymMethod(scope, methodName); // Need to resolve type later
                method.AddLocation(file, func.Name);
                method.Comments = func.Comments;
                
                var methodType = ResolveType(method, func.Params, file);
                if (methodType == null)
                {
                    Warning(func.Name, "Cannot resolve type");
                    return;
                }

                // TBD: Rename symbol
                //method.Name = methodType.Name;
                mSymbols.Add(method);
            }


            void BuildTypeFieldInfo()
            {
                var fields = new List<SymFieldInfo>();
                var constFields = new List<SymConstFieldInfo>();
                var staticFields = new List<SymFieldInfo>();

                foreach (var sym in mSymbols.Values)
                {
                    if (!(sym is SymType symType))
                        continue;

                    fields.Clear();
                    constFields.Clear();
                    staticFields.Clear();
                    foreach (var sym2Name in sym.Children.Values)
                    {
                        if (!(sym2Name is SymField symField))
                            continue;

                        var syntax = symField.Syntax;
                        if (Array.Exists(syntax.Qualifiers, t => t.Name == "const"))
                        {
                            var constField = new SymConstFieldInfo();
                            constField.Type = symField.TypeName;
                            constField.Name = symField.Token;
                            constFields.Add(constField);
                        }
                        else
                        {
                            var field = new SymFieldInfo();
                            field.Type = symField.TypeName;
                            field.Name = symField.Token;
                            if (Array.Exists(syntax.Qualifiers, t => t.Name == "static"))
                                staticFields.Add(field);
                            else
                                fields.Add(field);
                        }
                    }

                    var typeInfo = new SymTypeInfo();
                    typeInfo.Name = symType.FullName;
                    typeInfo.TypeArgs = symType.TypeArgs;
                    typeInfo.ConstFields = constFields.ToArray();
                    typeInfo.StaticFields = staticFields.ToArray();
                    typeInfo.Fields = fields.ToArray();
                    mSymTypeInfo[typeInfo.Name] = typeInfo;
                }
            }

        }

        /// <summary>
        /// Resolve a type.  Non-generic types are found in the symbol table
        /// and given a full name (e.g. 'int' -> 'Zufur.int').  Generic types
        /// must have all type arguments resolved as well.
        /// </summary>
        SymType ResolveType(Symbol scope, SyntaxExpr typeExpr, SymFile file)
        {
            // There will also be a syntax error
            if (typeExpr == null || typeExpr.Token.Name == "")
                return null;

            if (typeExpr.Token == "fun" || typeExpr.Token == "afun")
                return ResolveFuncType();

            if (sTypeAttributes.Contains(typeExpr.Token))
            {
                // TBD: Figure out what to do about the attributes.  For now skip them.
                Warning(typeExpr.Token, "Attribute not processed yet");
                if (typeExpr.Count == 1)
                    return ResolveType(scope, typeExpr[0], file);
                Reject(typeExpr.Token, "Index error");
                return null;
            }

            if (sTypeSymbols.Contains(typeExpr.Token))
                return ResolveGenericTypeSymbol();

            // Resolve regular symbol
            var symbol = mSymbols.FindUse(typeExpr.Token, scope, scope.File.Use);
            if (symbol == null)
                return null; // Error already marked

            // Mark symbol or error
            typeExpr.Token.AddInfo(symbol);
            var typeSymbol = symbol as SymType;
            if (typeSymbol == null)
            {
                Reject(typeExpr.Token, "The symbol is not a type, it is a " + symbol.Kind);
                return null;
            }
            return typeSymbol;

            SymType ResolveFuncType()
            {
                if (typeExpr.Count < 4)
                {
                    Reject(typeExpr.Token, "Syntax error");
                    return null;
                }

                // Generic type arguments
                foreach (var generic in typeExpr[0])
                    mSymbols.Add(new SymTypeArg(scope, file, generic.Token));

                var paramTypes = new List<string>();
                var returnTypes = new List<string>();
                var resolved1 = ResolveFuncParamTypes(typeExpr[1], paramTypes);
                var resolved2 = ResolveFuncParamTypes(typeExpr[2], returnTypes);
                if (!resolved1 || !resolved2)
                    return null;
                if (returnTypes.Count == 1 && returnTypes[0] == "void")
                    returnTypes.Clear();
                if (typeExpr[3].Token.Name != "") // error attribute
                    returnTypes.Add(typeExpr[3].Token.Name);

                var spec = new SymSpecializedType(typeExpr.Token, paramTypes.ToArray(), returnTypes.ToArray());

                // Return the one in the symbol table, if it exists
                if (mFuncTypes.TryGetValue(spec.FullName, out var specExists))
                    spec = specExists;
                else
                    mFuncTypes[spec.FullName] = spec;
                return spec;
            }

            // Resolve List<int>, etc.
            SymType ResolveGenericTypeSymbol()
            {
                if (typeExpr.Count == 0)
                {
                    Reject(typeExpr.Token, "Unexpected empty type argument list");
                    return null;
                }
                // Resolve type parameters
                var typeArgs = new List<string>();
                if (!ResolveTypeArgs(typeArgs))
                    return null;
                var typeParent = typeArgs[0];
                typeArgs.RemoveAt(0);

                // Generic type parameters (e.g. List<int>) or unary symbol (e.g. *int)
                SymSpecializedType spec;
                if (typeExpr.Token.Name == ParseZurf.VT_TYPE_ARG)
                    spec = new SymSpecializedType(typeParent, typeArgs.ToArray());
                else
                    spec = new SymSpecializedType(typeExpr.Token.Name, new string[] { typeParent });

                // Return the one in the symbol table, if it exists
                if (mSpecializedTypes.TryGetValue(spec.FullName, out var specExists))
                    spec = specExists;
                else
                    mSpecializedTypes[spec.FullName] = spec;
                return spec;
            }

            bool ResolveFuncParamTypes(SyntaxExpr paramExprs, List<string> paramTypes)
            {
                bool resolved = true;
                foreach (var pType in paramExprs)
                {
                    if (pType.Count == 0 || pType[0].Token == "" || pType[0].Token == "void" )
                    {
                        paramTypes.Add("void");
                        continue;
                    }

                    var sym = ResolveType(scope, pType[0], file);
                    if (sym != null)
                    {
                        paramTypes.Add(sym.FullName);
                        mSymbols.Add(new SymMethodArg(scope, file, pType.Token));
                    }
                    else
                    {
                        resolved = false;
                    }
                }
                return resolved;
            }

            bool ResolveTypeArgs(List<string> typeArgs)
            {
                var resolved = true;
                foreach (var typeArg in typeExpr)
                {
                    var sym = ResolveType(scope, typeArg, file);
                    if (sym != null)
                        typeArgs.Add(sym.FullName);
                    else
                        resolved = false;
                }
                return resolved;
            }
        }

        /// <summary>
        /// Step 3: Generate header file
        /// </summary>
        public void GenerateHeader()
        {
            foreach (var s in mSymbols.Values)
            {
                mAllSymbols.Add(s.Kind.ToUpper() + ": " + s.FullName + "  ->  " + s.Parent.Name);
                if (s is SymNamespace n)
                    mNamespaces.Add(n.FullName);
            }

            mAllSymbols.Sort((a, b) => a.CompareTo(b));
            mNamespaces.Sort((a, b) => a.CompareTo(b));
        }

        /// <summary>
        /// This may be called after all steps have been completed.
        /// </summary>
        public List<string> GenerateReport()
        {
            // Count errors
            var errors = new Dictionary<string, int>();
            int unknownErrors = 0;
            int totalErrors = 0;
            foreach (var file in mFiles.Values)
            {
                foreach (var token in file.SyntaxFile.Lexer)
                {
                    if (token.Error)
                    {
                        var foundError = false;
                        foreach (var error in token.GetInfos<TokenError>())
                        {
                            foundError = true;
                            var name = error.GetType().Name.ToString();
                            if (errors.ContainsKey(name))
                                errors[name] += 1;
                            else
                                errors[name] = 1;
                            totalErrors++;
                        }
                        if (!foundError)
                        {
                            unknownErrors++;
                            totalErrors++;
                        }
                    }
                }
                if (unknownErrors != 0)
                    errors["Unknown"] = unknownErrors;
            }

            // Report errors
            var headerFile = new List<string>();
            if (totalErrors == 0)
            {
                headerFile.Add("SUCCESS!  No Errors found");
            }
            else
            {
                headerFile.Add("FAIL!  " + totalErrors + " errors found!");
                foreach (var error in errors)
                {
                    headerFile.Add("    " + error.Key + ": " + error.Value);
                }
            }
            headerFile.Add("");

            // Count symbols
            int simpleTypes = 0;
            int genericTypes = 0;
            foreach (var sym in mSymbols.Values)
            {
                if (sym.GetType() == typeof(SymType))
                {
                    var t = sym as SymType;
                    if (mSymTypeInfo[t.FullName].TypeArgs.Length == 0)
                        simpleTypes++;
                    else
                        genericTypes++;
                }
            }

            headerFile.Add("SYMBOLS: " + mAllSymbols.Count);
            headerFile.Add("    Simple Types: " + simpleTypes);
            headerFile.Add("    Generic Types: " + genericTypes);
            headerFile.Add("    Generic Specializations: " + mSpecializedTypes.Count);
            headerFile.Add("");

            headerFile.Add("Generic specializations:");
            foreach (var sp in mSpecializedTypes.Values)
                headerFile.Add("    " + sp.FullName);
            headerFile.Add("");

            headerFile.Add("Method call types:");
            foreach (var sp in mFuncTypes.Values)
                headerFile.Add("    " + sp.FullName);
            headerFile.Add("");

            // Report types
            foreach (var sym in mSymbols.Values)
            {
                if (sym.GetType() != typeof(SymType))
                    continue;
                var type = sym as SymType;

                headerFile.Add("");
                headerFile.Add("TYPE " + type.TypeKeyword.ToUpper() + ": " + mSymTypeInfo[type.FullName].FullNameWithTypeArgs());
                if (!mSymTypeInfo.TryGetValue(type.FullName, out var info))
                {
                    headerFile.Add("    ERROR, unresolved info");
                    continue;
                }

                foreach (var field in info.ConstFields)
                    headerFile.Add("    const " + field.Name + " " + field.Type);
                foreach (var field in info.StaticFields)
                    headerFile.Add("    static " + field.Name + " " + field.Type);
                foreach (var field in info.Fields)
                    headerFile.Add("    field " + field.Name + " " + field.Type);

                foreach (var child1 in type.Children)
                {
                    var methodGroup = child1.Value as SymMethodGroup;
                    if (methodGroup != null)
                    {
                        foreach (var childMethod in methodGroup.Children)
                        {
                            var method = childMethod.Value as SymMethod;
                            if (method != null)
                                headerFile.Add("    method " + method.FullName);
                        }
                    }
                }
            }

            headerFile.Add("");
            headerFile.Add("******** ALL NAMESPACES ********");
            foreach (var ns in mNamespaces)
                headerFile.Add("    " + ns);
            headerFile.Add("");
            headerFile.Add("******** ALL SYMBOLS ********");
            foreach (var s in mAllSymbols)
                headerFile.Add("    " + s);


            headerFile.Add("");
            headerFile.Add("Header time = " + (int)(DateTime.Now - mStartTime).TotalMilliseconds + " ms");


            return headerFile;
        }


        // Does not reject if there is already an error there
        void Reject(Token token, string message)
        {
            if (!token.Error)
                token.AddError(new SilError(message));
        }

        // Does not add a warning if there is already an error there
        void Warning(Token token, string message)
        {
            if (!token.Error)
                token.AddWarning(new SilWarn(message));
        }

    }
}
