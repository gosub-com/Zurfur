using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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

        // TBD: Still figuring out how to deal with these.
        Dictionary<string, SymSpecializedType> mSpecializedTypes = new Dictionary<string, SymSpecializedType>();
        Dictionary<string, SymSpecializedType> mFuncTypes = new Dictionary<string, SymSpecializedType>();
        SymType mUnresolvedType;

        public SymbolTable Symbols => mSymbols;

        /// <summary>
        /// Step 1: GenerateTypeDefinitions, requires nothing from any other package.
        /// Step 2: ResolveTypeNames, requires type definitions from all other packages.
        /// Step 3: GenerateHeader.
        /// </summary>
        public ZilGenHeader()
        {
            mUnresolvedType = new SymType(mSymbols.Root, "$UnresolvedType");
            mUnresolvedType.AddLocation(new SymFile("(unknown)", new SyntaxFile()), new Token("$UnresolvedType"));
            mSymbols.Add(mUnresolvedType);
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

            // Add the namespace, or return the one we already have
            Symbol AddNamespace(SymFile file, Token[] path)
            {
                var childrenSymbols = mSymbols.Root.Children;
                Symbol parentSymbol = mSymbols.Root;
                Symbol newNamespace = null;
                foreach (var token in path)
                {
                    if (childrenSymbols.TryGetValue(token.Name, out var childNamespace))
                    {
                        Debug.Assert(childNamespace is SymNamespace);
                        newNamespace = childNamespace;
                        newNamespace.AddLocation(file, token);
                    }
                    else
                    {
                        newNamespace = new SymNamespace(parentSymbol, file, token);
                        newNamespace.Token.AddInfo(newNamespace);
                        mSymbols.Add(newNamespace);
                    }

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
                        newType.Token.AddInfo(newType);
                        if (mSymbols.Add(newType))
                        {
                            // Add type arguments
                            int typeArgCount = 0;
                            foreach (var expr in type.TypeArgs)
                            {                                
                                var newTypeParam = new SymTypeParam(newType, symFile, expr.Token, typeArgCount++);
                                newTypeParam.Token.AddInfo(newTypeParam);
                                mSymbols.Add(newTypeParam);
                            }
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
                        newField.Qualifiers = Array.ConvertAll(field.Qualifiers, a => a.Name);
                        newField.Comments = field.Comments;
                        newField.Token.AddInfo(newField);
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
                            mSymbols.Add(newMethod);
                        }
                    }
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

                var method = new SymMethod(scope, "$TBD$"); // Name will be set after resolving types
                method.Qualifiers = Array.ConvertAll(func.Qualifiers, a => a.Name);
                method.AddLocation(file, func.Name);
                method.Comments = func.Comments;

                // Add type arguments
                var typeArgCount = 0;
                foreach (var expr in func.TypeArgs)
                {
                    var typeSym = new SymTypeParam(method, file, expr.Token, typeArgCount);
                    //mSymbols.Add(typeSym);
                    if (method.Children.TryGetValue(typeSym.Name, out var duplicateTypeSym))
                    {
                        Reject(typeSym.Token, $"Duplicate symbol. There is a {duplicateTypeSym.Kind} in this scope with the same name.");
                    }
                    else
                    {
                        method.Children[expr.Token.Name] = typeSym;
                        typeArgCount++;
                    }
                }

                if (func.ExtensionType != null && func.ExtensionType.Token != "")
                {
                    // Resolve extension method type name (first parameter is "$this_ex")
                    var methodParam = new SymMethodParam(method, file, new Token("$this_ex"), -1);
                    methodParam.TypeName = ResolveType(methodParam, func.ExtensionType, file);
                    method.Children[methodParam.Name] = methodParam; // Extension method parameter name "$this_ex" is unique
                }
                else if (!method.Qualifiers.Contains("static"))
                {
                    // Non static method (first parameter is "$this")
                    var methodParam = new SymMethodParam(method, file, new Token("$this"), -1);
                    if (scope.Parent is SymType tn)
                    {
                        methodParam.TypeName = tn;
                        method.Children[methodParam.Name] = methodParam;  // Method parameter "$this" is unique
                    }
                    else
                    {
                        Reject(func.Name, "Methods at the namespace level must be 'static' or an extension method");
                    }
                }

                // Resolve method parameters and returns (TBD: error/exit specifier)
                ResolveMethodParams(file, method, func.MethodSignature[0], false); // Parameters
                ResolveMethodParams(file, method, func.MethodSignature[1], true);  // Returns

                // Set the final function name: "<#>(t1,t2...)(r1,r2...)"
                //      # - Number of generic parameters
                //      t1,t2... - Parameter types
                //      r1,r2... - Return types
                var mpi = new  MethodParamInfo(method);
                var genericsCount = mpi.TypeParams.Length;
                method.Name = (genericsCount==0 ? "" : "<" + string.Join(",", mpi.TypeParams) + ">") 
                                + mpi.ParamTypeNames + mpi.ReturnTypeNames;

                // TBD: Move this to `ZilVerify`
                // Reject duplicate symbols, but allow overloading of non-generic functions.
                // Reject overloads with different return types
                // TBD: This needs to see through alias types.
                // TBD: Need to reject more (extension methods with same name
                //      as members, etc.)
                var duplicate = false;
                foreach (var sibling in scope.Children.Values)
                {
                    Debug.Assert(sibling is SymMethod);
                    if (!(sibling is SymMethod siblingMethod))
                        continue;
                    var siblingMpi = new MethodParamInfo(siblingMethod);
                    var genericOverload = siblingMpi.TypeParams.Length != 0 || mpi.TypeParams.Length != 0;
                    if (genericOverload)
                    {
                        //$"Duplicate symbol. There is a {duplicateParam.Kind} in this scope with the same name."
                        duplicate = true;
                        Reject(method.Token, "Duplicate symbol.  Functions with generic arguments may not be overloaded.");
                    }
                    else if (siblingMpi.ParamTypeNames == mpi.ParamTypeNames)
                    {
                        // TBD: Must see through alias types
                        duplicate = true;
                        Reject(method.Token, $"Overloaded method parameter types must be different, but both are the same: '{mpi.ParamTypeNames}'");
                    }
                    else if (siblingMpi.ReturnTypeNames != mpi.ReturnTypeNames)
                    {
                        // TBD: Also see through alias types
                        duplicate = true;
                        Reject(method.Token, $"Overloaded method return types must be the same, but are different: "
                                             + $"'{mpi.ReturnTypeNames}' vs. '{siblingMpi.ReturnTypeNames}'");
                    }
                }

                method.Token.AddInfo(method);
                if (!duplicate)
                    mSymbols.Add(method);
            }


            void ResolveMethodParams(SymFile file, SymMethod method, SyntaxExpr parameters, bool isReturn)
            {
                if (parameters is SyntaxError)
                    return;

                var paramCount = 0;
                foreach (var expr in parameters)
                {
                    if (expr is SyntaxError)
                        continue;

                    var name = expr.Token == "" ? new Token("$return") : expr.Token;
                    var methodParam = new SymMethodParam(method, file, name, paramCount++);
                    methodParam.IsReturn = isReturn;
                    methodParam.TypeName = ResolveType(methodParam, expr[0], file);

                    if (method.Children.TryGetValue(methodParam.Name, out var duplicateParam))
                        Reject(methodParam.Token, 
                            $"Duplicate symbol. There is a {duplicateParam.Kind} in this scope with the same name.");
                    else
                        method.Children[methodParam.Name] = methodParam;
                }
            }

        }


        public class MethodParamInfo
        {
            public string[] TypeParams;
            public string ParamTypeNames;
            public string ReturnTypeNames;
            public SymMethodParam[] Params;
            public SymMethodParam[] Returns;
            public MethodParamInfo(SymMethod method)
            {
                var tp = method.FindChildren<SymTypeParam>();
                tp.Sort((a, b) => a.Order.CompareTo(b.Order));
                TypeParams = tp.ConvertAll(a => a.Name).ToArray();

                var mp = method.FindChildren<SymMethodParam>();
                mp.Sort((a, b) => a.Order.CompareTo(b.Order));
                Params = mp.FindAll(a => !a.IsReturn).ToArray();
                Returns = mp.FindAll(a => a.IsReturn).ToArray();

                ParamTypeNames = "(" + string.Join(",", Array.ConvertAll(Params, a => a.TypeName.FullName)) + ")";
                ReturnTypeNames = "(" + string.Join(",", Array.ConvertAll(Returns, a => a.TypeName.FullName)) + ")";
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
                return ResolveTypeFunc();

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
                return ResolveTypeGenericSymbol();

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

            SymType ResolveTypeFunc()
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
                var resolved1 = ResolveTypeFuncParams(typeExpr[0], paramTypes);
                var resolved2 = ResolveTypeFuncParams(typeExpr[1], returnTypes);
                if (!resolved1 || !resolved2)
                    return mUnresolvedType;

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

            bool ResolveTypeFuncParams(SyntaxExpr paramExprs, List<string> paramTypes)
            {
                bool resolved = true;
                foreach (var pType in paramExprs)
                {
                    var sym = ResolveType(scope, pType[0], file);
                    if (sym != mUnresolvedType)
                    {
                        paramTypes.Add(sym.FullName);
                        var newMethodParam = new SymMethodParam(scope, file, pType.Token, 0);
                        newMethodParam.Token.AddInfo(newMethodParam);
                        mSymbols.Add(newMethodParam); // TBD: Fix
                    }
                    else
                    {
                        resolved = false;
                    }
                }
                return resolved;
            }

            // Resolve List<int>, etc.
            SymType ResolveTypeGenericSymbol()
            {
                if (typeExpr.Count == 0)
                {
                    Reject(typeExpr.Token, "Unexpected empty type argument list");
                    return mUnresolvedType;
                }
                // Resolve type parameters
                var typeParams = new List<string>();
                if (!ResolveTypeGenericParams(typeParams))
                    return mUnresolvedType;
                var typeParent = typeParams[0];
                typeParams.RemoveAt(0);

                // Generic type parameters (e.g. List<int>) or unary symbol (e.g. *int)
                SymSpecializedType spec;
                if (typeExpr.Token.Name == ParseZurf.VT_TYPE_ARG)
                    spec = new SymSpecializedType(typeParent, typeParams.ToArray());
                else
                    spec = new SymSpecializedType(typeExpr.Token.Name, new string[] { typeParent });

                // Return the one in the symbol table, if it exists
                if (mSpecializedTypes.TryGetValue(spec.FullName, out var specExists))
                    spec = specExists;
                else
                    mSpecializedTypes[spec.FullName] = spec;
                return spec;
            }


            bool ResolveTypeGenericParams(List<string> typeParams)
            {
                var resolved = true;
                foreach (var typeArg in typeExpr)
                {
                    var sym = ResolveType(scope, typeArg, file);
                    if (sym != mUnresolvedType)
                        typeParams.Add(sym.FullName);
                    else
                        resolved = false;
                }
                return resolved;
            }
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
