using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    public class ZilError : TokenError
    {
        public ZilError(string message) : base(message) { }
    }
    public class ZilWarn : TokenWarn
    {
        public ZilWarn(string message) : base(message) { }
    }

    class ZilGenHeader
    {
        static WordSet sTypeSymbols = new WordSet("? * ^ [ " + ParseZurf.VT_TYPE_ARG);
        static WordSet sTypeAttributes = new WordSet("in out mut ro ref");

        Dictionary<string, SymFile> mFiles = new Dictionary<string, SymFile>();
        SymbolTable mSymbols = new SymbolTable();
        Dictionary<string, SymSpecializedType> mSpecializedTypes = new Dictionary<string, SymSpecializedType>();
        Dictionary<string, SymSpecializedType> mFuncTypes = new Dictionary<string, SymSpecializedType>();
        DateTime mStartTime = DateTime.Now;

        SymNamespace mRootNamespace;
        SymType mUnresolvedType;

        /// <summary>
        /// Step 1: GenerateTypeDefinitions, requires nothing from any other package.
        /// Step 2: ResolveTypeNames, requires type definitions from all other packages.
        /// Step 3: GenerateHeader.
        /// </summary>
        public ZilGenHeader()
        {
            var preRoot = new SymNamespace(null, new SymFile("", new SyntaxFile()), new Token(""));
            mRootNamespace = new SymNamespace(preRoot, new SymFile("", new SyntaxFile()), new Token(""));
            mSymbols[""] = mRootNamespace;
            mUnresolvedType = new SymType(mRootNamespace, "$UnresolvedType");
            mUnresolvedType.SetChildInfo();
            mSymbols["$UnresolvedType"] = mUnresolvedType;

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
                var childrenSymbols = mRootNamespace.Children;
                Symbol parentSymbol = mRootNamespace;
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
                        if (mSymbols.Add(newType))
                        {
                            // Add type arguments
                            int typeArgCount = 0;
                            foreach (var expr in type.TypeArgs)
                                mSymbols.Add(new SymTypeArg(newType, symFile, expr.Token, typeArgCount++));
                        }
                        newType.SetChildInfo();
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
            ResolveMethodGroups();
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
                            if (field.ParentScope !=null && field.ParentScope.Keyword == "enum")
                                Warning(field.Name, "TBD: Process enum");
                            else
                                Reject(field.Name, "Expecting symbol to have an explicitly named type");
                            continue;
                        }
                        symField.TypeName = ResolveType(symField, field.TypeName, symFile);
                    }
                }
            }

            void ResolveMethodGroups()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var func in symFile.SyntaxFile.Methods)
                    {
                        var group = mSymbols.FindPath(func.NamePath).Children[func.Name];
                        if (!(group is SymMethodGroup))
                        {
                            Reject(func.Name, "Duplicate symbol.  There is a " + group.Kind + $" with the same name as '{group.FullName}'");
                            func.Name.AddInfo(group);
                        }
                        else
                        {
                            ResolveMethod(group as SymMethodGroup, func, symFile);
                        }
                    }
                }
            }

            void ResolveMethod(SymMethodGroup scope, SyntaxFunc func, SymFile file)
            {
                if (func.IsProperty)
                {
                    Warning(func.Name, "TBD: Property not processed yet");
                    return;
                }
                if (func.MethodSignature.Count != 3)
                {
                    Reject(func.Name, "Syntax error or compiler error");
                    return;
                }

                // Name methods uniquely in the method group.  The next level
                // down will contain the unique signature.  We could put it
                // here, except then would have to rename later.
                //      Namespace.Class.FunctionName.$1.Signature
                // We can name them $1, $2, $3 now, then add signature later.
                string methodName;
                if (func.ExtensionType == null || func.ExtensionType.Token == "")
                {
                    methodName = "$" + (scope.Children.Count + 1);
                }
                else
                {
                    // Extension methods do the same thing, except like this:
                    //      Namespace.Class.FunctionName.$ext1.Signature
                    // The first parameter is the type of class we are extending
                    methodName = "$extension" + (scope.Children.Count+1);
                }
                
                var method = new SymMethod(scope, methodName); // Need to resolve type later
                method.AddLocation(file, func.Name);
                method.Comments = func.Comments;

                // Add type arguments
                var typeArgCount = 0;
                foreach (var expr in func.TypeArgs)
                    mSymbols.Add(new SymTypeArg(method, file, expr.Token, typeArgCount++));

                // Resolve extension method type name.
                // The first parameter will be this type and named "this"
                if (func.ExtensionType != null && func.ExtensionType.Token != "")
                {
                    var methodArg = new SymMethodArg(method, file, new Token("$this"), -1);
                    methodArg.TypeName = ResolveType(methodArg, func.ExtensionType, file);
                    mSymbols.Add(methodArg);
                }

                // Resolve method parameters and returns (TBD: error/exit specifier)
                ResolveMethodParams(file, method, func.MethodSignature[0], false);
                ResolveMethodParams(file, method, func.MethodSignature[1], true);

                if (func.Name == "AlphaLengthWise")
                {

                }
                method.SetChildInfo();
                method.SetName(method.ParamTypeNames);
                //method.SetChildInfo();



                // Since function names are unique (e.g. "$1", etc) verify duplicate here
                /*foreach (var sym in method.Parent.Children.Values)
                    if (sym is SymMethod sibling && sibling.ParamTypeNames == method.ParamTypeNames)
                    {
                        mSymbols.DuplicateSymbol(method, sym, true);
                        break;
                    }*/


                mSymbols.Add(method);
            }

            void ResolveMethodParams(SymFile file, SymMethod method, SyntaxExpr parameters, bool isReturn)
            {
                var paramCount = 0;

                foreach (var expr in parameters)
                {
                    var name = expr.Token == "" ? new Token("$return") : expr.Token;
                    var methodArg = new SymMethodArg(method, file, name, paramCount++);
                    methodArg.IsReturn = isReturn;
                    if (expr.Count == 0 || expr[0].Token == "")
                    {
                        continue;  // Empty return is void
                    }
                    else
                    {
                        methodArg.TypeName = ResolveType(methodArg, expr[0], file);
                        if (methodArg.TypeName.ToString() == "Zurfur.void")
                            continue; // Special case: Skip void
                    }
                    mSymbols.Add(methodArg);
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
                return mUnresolvedType;

            if (typeExpr.Token == "fun" || typeExpr.Token == "afun")
                return ResolveFuncType();

            if (sTypeAttributes.Contains(typeExpr.Token))
            {
                // TBD: Figure out what to do about the attributes.  For now skip them.
                //Warning(typeExpr.Token, "Attribute not processed yet");
                if (typeExpr.Count == 1)
                    return ResolveType(scope, typeExpr[0], file);
                Reject(typeExpr.Token, "Index error");
                return mUnresolvedType;
            }

            if (sTypeSymbols.Contains(typeExpr.Token))
                return ResolveGenericTypeSymbol();

            // Resolve regular symbol
            var symbol = mSymbols.FindUse(typeExpr.Token, scope, scope.File.Use);
            if (symbol == null)
                return mUnresolvedType; // Error already marked

            // Mark symbol or error
            typeExpr.Token.AddInfo(symbol);
            var typeSymbol = symbol as SymType;
            if (typeSymbol == null)
            {
                Reject(typeExpr.Token, "The symbol is not a type, it is a " + symbol.Kind);
                return mUnresolvedType;
            }
            return typeSymbol;

            SymType ResolveFuncType()
            {
                if (typeExpr.Count < 3)
                {
                    Reject(typeExpr.Token, "Syntax error or compiler error");
                    return mUnresolvedType;
                }

                // Generic type arguments: TBD
                //foreach (var generic in typeExpr[0])
                //    mSymbols.Add(new SymTypeArg(scope, file, generic.Token, 0));

                var paramTypes = new List<string>();
                var returnTypes = new List<string>();
                var resolved1 = ResolveFuncParamTypes(typeExpr[0], paramTypes);
                var resolved2 = ResolveFuncParamTypes(typeExpr[1], returnTypes);
                if (!resolved1 || !resolved2)
                    return mUnresolvedType;
                if (returnTypes.Count == 1 && returnTypes[0] == "void")
                    returnTypes.Clear();
                if (typeExpr[2].Token.Name != "") // error attribute
                    returnTypes.Add(typeExpr[2].Token.Name);

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
                    return mUnresolvedType;
                }
                // Resolve type parameters
                var typeArgs = new List<string>();
                if (!ResolveTypeArgs(typeArgs))
                    return mUnresolvedType;
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
                    if (sym != mUnresolvedType)
                    {
                        paramTypes.Add(sym.FullName);
                        mSymbols.Add(new SymMethodArg(scope, file, pType.Token, 0)); // TBD: Fix
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
                    if (sym != mUnresolvedType)
                        typeArgs.Add(sym.FullName);
                    else
                        resolved = false;
                }
                return resolved;
            }
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
                    if (t.TypeArgs.Length == 0)
                        simpleTypes++;
                    else
                        genericTypes++;
                }
            }

            // Get namespaces and all symbols
            List<string> namespaces = new List<string>();
            List<string> allSymbols = new List<string>();
            foreach (var s in mSymbols.Values)
            {
                allSymbols.Add(s.Kind.ToUpper() + ": " + s.FullName + "  ->  " + s.Parent.Name);
                if (s is SymNamespace n)
                    namespaces.Add(n.FullName);
            }
            allSymbols.Sort((a, b) => a.CompareTo(b));
            namespaces.Sort((a, b) => a.CompareTo(b));


            headerFile.Add("SYMBOLS: " + allSymbols.Count);
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

            headerFile.Add("");
            headerFile.Add("Namespaces:");
            foreach (var ns in namespaces)
                headerFile.Add("    " + ns);
            headerFile.Add("");


            // Report types
            foreach (var sym in mSymbols.Values)
            {
                if (sym.GetType() != typeof(SymType))
                    continue;
                var type = sym as SymType;

                headerFile.Add("");
                headerFile.Add("TYPE " + type.TypeKeyword.ToUpper() + ": " + type.FullName + type.TypeArgNames());
                var constFields = new List<SymField>();
                var staticFields = new List<SymField>();
                var fields = new List<SymField>();
                foreach (var field in sym.FindChildren<SymField>())
                {
                    if (Array.Exists(field.Syntax.Qualifiers, t => t.Name == "const"))
                        constFields.Add(field);
                    else if (Array.Exists(field.Syntax.Qualifiers, t => t.Name == "static"))
                        staticFields.Add(field);
                    else
                        fields.Add(field);
                }

                foreach (var field in constFields)
                    headerFile.Add("    const " + field.Name + " " + field.TypeName + " = " + field.Syntax.Initializer);
                foreach (var field in staticFields)
                    headerFile.Add("    static " + field.Name + " " + field.TypeName + " = " + field.Syntax.Initializer);
                foreach (var field in fields)
                    headerFile.Add("    field " + field.Name + " " + field.TypeName + " = " + field.Syntax.Initializer);

                foreach (var methodGroup in sym.FindChildren<SymMethodGroup>())
                    foreach (var method in methodGroup.FindChildren<SymMethod>())
                        headerFile.Add("    method " + methodGroup.Name + method.TypeArgNames() + method.ParamTypeNames);

            }

            headerFile.Add("");
            headerFile.Add("Header time = " + (int)(DateTime.Now - mStartTime).TotalMilliseconds + " ms");

            return headerFile;
        }

        // Does not reject if there is already an error there
        void Reject(Token token, string message)
        {
            if (!token.Error)
                token.AddError(new ZilError(message));
        }

        // Does not add a warning if there is already an error there
        void Warning(Token token, string message)
        {
            if (!token.Error)
                token.AddWarning(new ZilWarn(message));
        }

    }
}
