using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    public class ZilHeaderError : TokenError
    {
        public ZilHeaderError(string message) : base(message) { }
    }
    public class ZilWarn : TokenWarn
    {
        public ZilWarn(string message) : base(message) { }
    }

    class ZilGenHeader
    {
        static WordSet sTypeAttributes = new WordSet("in out mut ro ref");

        SymbolTable mSymbols = new SymbolTable();
        Dictionary<string, SymType> sUnaryTypeSymbols = new Dictionary<string, SymType>();

        // TBD: Still figuring out how to deal with these.
        Dictionary<string, SymParameterizedType> mSpecializedTypes = new Dictionary<string, SymParameterizedType>();

        public SymbolTable Symbols => mSymbols;

        public ZilGenHeader()
        {
            sUnaryTypeSymbols["*"] = new SymType(mSymbols.Root, "$ptr");
            sUnaryTypeSymbols["^"] = new SymType(mSymbols.Root, "$ref");
            sUnaryTypeSymbols["["] = new SymType(mSymbols.Root, "$span");
            sUnaryTypeSymbols["?"] = new SymType(mSymbols.Root, "$nullable");
            sUnaryTypeSymbols["fun"] = new SymType(mSymbols.Root, "$fun");
            sUnaryTypeSymbols["afun"] = new SymType(mSymbols.Root, "$afun");
            foreach (var v in sUnaryTypeSymbols.Values)
                mSymbols.AddOrReject(v);
            mSymbols.AddOrReject(new SymTypeParam(sUnaryTypeSymbols["*"], "", new Token("T")));
        }


        public void GenerateHeader(Dictionary<string, SyntaxFile> syntaxFiles)
        {
            // Does not require symbols from external packages
            Dictionary<string, string[]> fileUses = new Dictionary<string, string[]>();
            AddNamespaces();
            mSymbols.GenerateLookup();
            AddTypes();
            AddFields();
            AddMethodGroups();

            // This requires symbols from external packages
            ProcessUseStatements();
            ResolveFieldTypes();
            ResolveMethodGroups();
            mSymbols.GenerateLookup();
            mSymbols.AddSpecializations(mSpecializedTypes);
            return;

            void AddNamespaces()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var ns in syntaxFile.Value.Namespaces)
                    {
                        var symbol = AddNamespace(syntaxFile.Key, ns.Value.Path);
                        symbol.Comments += " " + ns.Value.Comments;
                    }
                }
            }

            // Add the namespace, or return the one we already have
            Symbol AddNamespace(string file, Token[] path)
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
                        mSymbols.AddOrReject(newNamespace);
                    }

                    parentSymbol = newNamespace;
                    childrenSymbols = newNamespace.Children;
                }
                return newNamespace;
            }

            void AddTypes()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var type in syntaxFile.Value.Types)
                    {
                        var newType = new SymType(mSymbols.FindPath(type.NamePath), syntaxFile.Key, type.Name);
                        newType.Comments = type.Comments;
                        newType.Token.AddInfo(newType);
                        if (mSymbols.AddOrReject(newType))
                        {
                            // Add type arguments
                            foreach (var expr in type.TypeArgs)
                            {                                
                                var newTypeParam = new SymTypeParam(newType, syntaxFile.Key, expr.Token);
                                newTypeParam.Token.AddInfo(newTypeParam);
                                mSymbols.AddOrReject(newTypeParam);
                            }
                        }
                    }
                }
            }

            void AddFields()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var field in syntaxFile.Value.Fields)
                    {
                        var newField = new SymField(mSymbols.FindPath(field.NamePath), syntaxFile.Key, field.Name);
                        newField.Qualifiers = Array.ConvertAll(field.Qualifiers, a => a.Name);
                        newField.Comments = field.Comments;
                        newField.Token.AddInfo(newField);
                        var scopeParent = field.NamePath[field.NamePath.Length-1];
                        if (scopeParent.Error)
                        {
                            Warning(newField.Token, $"Symbol not processed because '{scopeParent}' has an error");
                            continue;
                        }
                        mSymbols.AddOrReject(newField);
                    }
                }
            }

            void AddMethodGroups()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var method in syntaxFile.Value.Methods)
                    {
                        var parentSymbol = mSymbols.FindPath(method.NamePath);
                        if (!parentSymbol.Children.ContainsKey(method.Name))
                        {
                            var newMethod = new SymMethodGroup(parentSymbol, syntaxFile.Key, method.Name);
                            mSymbols.AddOrReject(newMethod);
                        }
                    }
                }
            }

            void ProcessUseStatements()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    var useNamespaces = new List<string>();
                    foreach (var use in syntaxFile.Value.Using)
                    {
                        var symbol = mSymbols.FindPathOrReject(use.NamePath);
                        if (symbol == null || use.NamePath.Length == 0)
                            continue;  // Error marked by function

                        var lastToken = use.NamePath[use.NamePath.Length - 1];
                        if (!(symbol is SymNamespace))
                        {
                            Reject(lastToken, "Must be a namespace, not a " + symbol.Kind);
                            continue;
                        }

                        if (useNamespaces.Contains(symbol.GetFullName()))
                        {
                            Reject(lastToken, "Already included in previous use statement");
                            continue;
                        }
                        useNamespaces.Add(symbol.GetFullName());
                    }
                    fileUses[syntaxFile.Key] = useNamespaces.ToArray();
                }
            }

            void ResolveFieldTypes()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var field in syntaxFile.Value.Fields)
                    {
                        if (field.NamePath[field.NamePath.Length - 1].Error)
                            continue; // Warning given by AddFields

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
                        symField.TypeName = ResolveTypeOrReject(symField, field.TypeName, syntaxFile.Key);
                    }
                }
            }

            void ResolveMethodGroups()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var func in syntaxFile.Value.Methods)
                    {
                        var group = mSymbols.FindPath(func.NamePath).Children[func.Name];
                        if (!(group is SymMethodGroup))
                        {
                            Reject(func.Name, "Duplicate symbol.  There is a " + group.Kind + $" with the same name as '{group.GetFullName()}'");
                            func.Name.AddInfo(group);
                        }
                        else
                        {
                            ResolveMethodTypes(group as SymMethodGroup, func, syntaxFile.Key);
                        }
                    }
                }
            }

            void ResolveMethodTypes(SymMethodGroup scope, SyntaxFunc func, string file)
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

                // Give each function a unique name
                var typeParams = func.TypeArgs.Count == 0 ? "" : "``" + func.TypeArgs.Count;
                var method = new SymMethod(scope, $"{typeParams}#{scope.Children.Count}");
                method.Qualifiers = Array.ConvertAll(func.Qualifiers, a => a.Name);
                method.AddLocation(file, func.Name);
                method.Comments = func.Comments;

                // Add type arguments
                foreach (var expr in func.TypeArgs)
                {
                    var typeSym = new SymTypeParam(method, file, expr.Token);
                    mSymbols.AddOrReject(typeSym);
                }

                if (func.ExtensionType != null && func.ExtensionType.Token != "")
                {
                    // Resolve extension method type name (first parameter is "$this_ex")
                    var methodParam = new SymMethodParam(method, file, new Token("$this_ex"));
                    methodParam.TypeName = ResolveTypeOrReject(methodParam, func.ExtensionType, file);
                    mSymbols.AddOrReject(methodParam); // Extension method parameter name "$this_ex" is unique
                }
                else if (!method.Qualifiers.Contains("static")) // TBD: Check if in type instead of namespace
                {
                    // Non static method (first parameter is "$this")
                    var methodParam = new SymMethodParam(method, file, new Token("$this"));
                    if (scope.Parent is SymType tn)
                    {
                        methodParam.TypeName = tn.ToString();
                        mSymbols.AddOrReject(methodParam);  // Method parameter "$this" is unique
                    }
                }

                // Resolve method parameters and returns (TBD: error/exit specifier)
                ResolveMethodParams(file, method, func.MethodSignature[0], false); // Parameters
                ResolveMethodParams(file, method, func.MethodSignature[1], true);  // Returns

                method.Token.AddInfo(method);
                var scopeParent = func.NamePath[func.NamePath.Length - 1];
                if (scopeParent.Error)
                {
                    Warning(func.Token, $"Symbol not processed because '{scopeParent}' has an error");
                    return;
                }


                // Set the final function name: "<#>(t1,t2...)(r1,r2...)"
                //      # - Number of generic parameters
                //      t1,t2... - Parameter types
                //      r1,r2... - Return types
                var mpi = new  MethodParamInfo(method);
                var genericsCount = mpi.TypeParams.Length;

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
                        duplicate = true;
                        Reject(method.Token, "Duplicate symbol.  Functions with generic arguments may not be overloaded.");
                    }
                    else if (siblingMpi.ParamTypeNames == mpi.ParamTypeNames)
                    {
                        // TBD: Must see through alias types.  Also need to protect against implicit operator overloading.
                        duplicate = true;
                        Reject(method.Token, $"Overloaded method parameter types must be different, but both are the same: '{mpi.ParamTypeNames}'");
                    }
                }

                if (!duplicate)
                    mSymbols.AddOrReject(method);
            }

            void ResolveMethodParams(string file, SymMethod method, SyntaxExpr parameters, bool isReturn)
            {
                if (parameters is SyntaxError)
                    return;

                foreach (var expr in parameters)
                {
                    if (expr is SyntaxError)
                        continue;

                    var name = expr.Token == "" ? new Token("$return") : expr.Token;
                    var methodParam = new SymMethodParam(method, file, name);
                    methodParam.IsReturn = isReturn;
                    methodParam.TypeName = ResolveTypeOrReject(methodParam, expr[0], file);
                    mSymbols.AddOrReject(methodParam);
                }
            }

            string ResolveTypeOrReject(Symbol scope, SyntaxExpr typeExpr, string file)
            {
                var symbol = ResolveTypeScopeOrReject(typeExpr, true, scope, fileUses[file], file);
                if (symbol == null)
                    return "$UnresolvedType";

                if (symbol is SymType || symbol is SymTypeParam || symbol is SymParameterizedType)
                    return symbol.GetFullName();

                Reject(typeExpr.Token, "The symbol is not a type, it is a " + symbol.Kind);
                return "$UnresolvedType";
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

                ParamTypeNames = "(" + string.Join(",", Array.ConvertAll(Params, a => a.TypeName)) + ")";
                ReturnTypeNames = "(" + string.Join(",", Array.ConvertAll(Returns, a => a.TypeName)) + ")";
            }
        }


        /// <summary>
        /// Resolve a type.  Non-generic types are found in the symbol table
        /// and given a full name (e.g. 'int' -> 'Zufur.int').  Generic types
        /// must have all type arguments resolved as well.
        /// Return symbol is always a namespace, type, or type parameter.
        /// Null is returned for error, and the error is marked.
        /// </summary>
        Symbol ResolveTypeScopeOrReject(SyntaxExpr typeExpr, bool top, Symbol scope, string []useScope, string file)
        {
            // There will also be a syntax error
            if (typeExpr == null || typeExpr.Token.Name == "")
                return null;

            if (typeExpr.Token.Name == ".")
            {
                var leftSymbol = ResolveTypeScopeOrReject(typeExpr[0], top, scope, useScope, file);
                if (leftSymbol == null)
                    return null;
                if (typeExpr.Count != 2)
                {
                    // Probably user is still typing
                    Reject(typeExpr.Token, $"Syntax error");
                    return null;
                }
                if (!(leftSymbol is SymNamespace) && !(leftSymbol is SymType) && !(leftSymbol is SymParameterizedType))
                {
                    Reject(typeExpr.Token, $"The left side of the '.' must evaluate to a namespace or type, but it is a {leftSymbol.Kind}");
                    return null;
                }

                // TBD: Specialized type needs to belong to this symbol table with correct parent
                var rightSymbol = ResolveTypeScopeOrReject(typeExpr[1], false, leftSymbol, useScope, file);
                return rightSymbol;
            }

            if (sTypeAttributes.Contains(typeExpr.Token))
            {
                // TBD: Figure out what to do about the attributes.  For now skip them.
                //Warning(typeExpr.Token, "Attribute not processed yet");
                if (typeExpr.Count == 1)
                    return ResolveTypeScopeOrReject(typeExpr[0], top, scope, useScope, file);
                Reject(typeExpr.Token, "Index error");
                return null;
            }

            if (typeExpr.Token == "fun" || typeExpr.Token == "afun")
                return ResolveTypeFunOrReject();

            if (sUnaryTypeSymbols.ContainsKey(typeExpr.Token) || typeExpr.Token == ParseZurf.VT_TYPE_ARG)
                return ResolveTypeGenericSymbolOrReject();

            // Resolve regular symbol
            var symbol = top ? mSymbols.FindInScopeOrUseOrReject(typeExpr.Token, scope, useScope)
                             : mSymbols.FindInScopeOrReject(typeExpr.Token, scope);
            if (symbol == null)
                return null; // Error already marked

            // Mark symbol or error
            typeExpr.Token.AddInfo(symbol);
            if (symbol is SymNamespace || symbol is SymType || symbol is SymTypeParam)
                return symbol;

            Reject(typeExpr.Token, "The symbol is not a type, it is a " + symbol.Kind);
            return null;

            // Resolve "fun" or "afun" types
            Symbol ResolveTypeFunOrReject()
            {
                if (typeExpr.Count < 3)
                {
                    Reject(typeExpr.Token, "Syntax error");
                    return null;
                }

                // Generic type arguments: TBD
                //foreach (var generic in typeExpr[0])
                //    mSymbols.Add(new SymTypeArg(scope, file, generic.Token, 0));

                var paramTypes = new List<Symbol>();
                var returnTypes = new List<Symbol>();
                var resolved1 = ResolveTypeFunParamsOrReject(typeExpr[0], paramTypes);
                var resolved2 = ResolveTypeFunParamsOrReject(typeExpr[1], returnTypes);
                if (!resolved1 || !resolved2)
                    return null;

                // TBD: Figure out what to do with this (error/exit attribute)
                //if (typeExpr[2].Token.Name != "") // error attribute
                //    returnTypes.Add(typeExpr[2].Token.Name);

                var typeParent = sUnaryTypeSymbols[typeExpr.Token.Name];
                var spec = new SymParameterizedType(typeParent, paramTypes.ToArray(), returnTypes.ToArray());

                // Return the one in the symbol table, if it exists
                if (mSpecializedTypes.TryGetValue(spec.GetFullName(), out var specExists))
                    spec = specExists;
                else
                    mSpecializedTypes[spec.GetFullName()] = spec;
                return spec;
            }

            // Resolve "fun" or "afun" parameter types
            bool ResolveTypeFunParamsOrReject(SyntaxExpr paramExprs, List<Symbol> paramTypes)
            {
                bool resolved = true;
                foreach (var pType in paramExprs)
                {
                    if (pType is SyntaxError)
                        continue;
                    var sym = ResolveTypeScopeOrReject(pType[0], true, scope, useScope, file);
                    if (sym == null)
                    {
                        resolved = false;
                        continue;
                    }
                    if (sym is SymType || sym is SymTypeParam || sym is SymParameterizedType)
                    {
                        paramTypes.Add(sym);
                        var newMethodParam = new SymMethodParam(scope, file, pType.Token);
                        newMethodParam.Token.AddInfo(newMethodParam);
                        mSymbols.AddOrReject(newMethodParam); // TBD: Fix
                    }
                    else
                    {
                        Reject(sym.Token, $"Expecting a type, but got a {sym.Kind}");
                        resolved = false;
                    }
                }
                return resolved;
            }

            // Resolve List<int>, etc.
            Symbol ResolveTypeGenericSymbolOrReject()
            {
                if (typeExpr.Count == 0)
                {
                    Reject(typeExpr.Token, "Unexpected empty type argument list");
                    return null;
                }
                // Resolve type parameters
                var typeParams = new List<Symbol>();
                if (!ResolveTypeGenericParamsOrReject(typeParams))
                    return null;

                // Process special unary type operators here
                Symbol typeParent;
                if (typeExpr.Token == ParseZurf.VT_TYPE_ARG)
                {
                    // Type argument list: F<t1,t2...>
                    typeParent = typeParams[0];
                    typeParams.RemoveAt(0);
                }
                else
                {
                    // Unary type: '*type', etc.
                    typeParent = sUnaryTypeSymbols[typeExpr.Token];
                }

                var sym = new SymParameterizedType(typeParent, typeParams.ToArray());

                // Return the one in the symbol table, if it exists
                if (mSpecializedTypes.TryGetValue(sym.GetFullName(), out var specExists))
                    sym = specExists;
                else
                    mSpecializedTypes[sym.GetFullName()] = sym;
                return sym;
            }


            bool ResolveTypeGenericParamsOrReject(List<Symbol> typeParams)
            {
                var resolved = true;
                foreach (var typeArg in typeExpr)
                {
                    var sym = ResolveTypeScopeOrReject(typeArg, true, scope, useScope, file);
                    if (sym == null)
                    {
                        resolved = false;
                        continue;
                    }
                    if (sym is SymType || sym is SymTypeParam || sym is SymParameterizedType)
                    {
                        typeParams.Add(sym);
                    }
                    else
                    {
                        Reject(typeArg.Token, $"Expecting a type, but got a {sym.Kind}");
                        resolved = false;
                    }
                }
                return resolved;
            }
        }

        // Does not reject if there is already an error there
        void Reject(Token token, string message)
        {
            if (!token.Error)
                token.AddError(new ZilHeaderError(message));
        }

        // Does not add a warning if there is already an error there
        void Warning(Token token, string message)
        {
            if (!token.Error)
                token.AddWarning(new ZilWarn(message));
        }

    }
}
