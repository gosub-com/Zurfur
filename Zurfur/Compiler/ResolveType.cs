using System;
using System.Collections.Generic;
using System.Diagnostics;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    static class ResolveType
    {
        /// <summary>
        /// Resolve a type.  Non-generic types are found in the symbol table
        /// and given a full name (e.g. 'int' -> 'Zufur.int').  Generic types
        /// must have all type arguments resolved as well.
        /// Return symbol is always a module, type, or type parameter.
        /// Null is returned for error, and the error is marked.
        /// </summary>
        public static Symbol ResolveTypeOrReject(SyntaxExpr typeExpr,
                                   SymbolTable table,
                                   bool isDot,
                                   Symbol scope,
                                   UseSymbolsFile useScope,
                                   bool hasGenericParams = false)
        {
            // There will also be a syntax error
            if (typeExpr == null || typeExpr.Token.Name == "")
                return null;
            Debug.Assert(!scope.IsSpecializedType);

            if (typeExpr.Token.Name == ".")
                return ResolveDotOrReject();

            if (typeExpr.Token == "fun" || typeExpr.Token == "afun")
                return ResolveLambdaFunOrReject();

            if (table.UnaryTypeSymbols.ContainsKey(typeExpr.Token) || typeExpr.Token == ParseZurf.VT_TYPE_ARG)
                return ResolveGenericTypeOrReject();

            // Resolve regular symbol
            bool foundInScope = false;
            var symbol = isDot ? FindLocalTypeOrReject(typeExpr.Token, table, scope)
                               : FindGlobalTypeOrReject(typeExpr.Token, table, scope, useScope, out foundInScope);
            if (symbol == null)
                return null; // Error already marked

            if (!symbol.IsAnyType || symbol.IsSpecializedType)
            {
                table.Reject(typeExpr.Token, "The symbol is not a type, it is a " + symbol.KindName);
                return table.NoCompilerChecks ? symbol : null;
            }

            if (!hasGenericParams && symbol.GenericParamCount() != 0)
            {
                table.Reject(typeExpr.Token, $"Expecting {symbol.GenericParamCount()} generic parameter(s), but got 0");
                if (!table.NoCompilerChecks)
                    return null;
            }

            typeExpr.Token.AddInfo(symbol);

            // Type parameter
            if (symbol.IsTypeParam)
            {
                return table.GetGenericParam(symbol.GenericParamNum());
            }

            // Type inference: Add implied types to inner types found in this scope
            // e.g: InnerType => OuterType<T>.InnerType
            if (!isDot && symbol.IsType && foundInScope)
                symbol = AddOuterGenericParameters(table, symbol, symbol.Parent);

            return symbol;

            Symbol ResolveDotOrReject()
            {
                var leftSymbol = ResolveTypeOrReject(typeExpr[0], table, isDot, scope, useScope);
                if (leftSymbol == null)
                    return null;
                if (typeExpr.Count != 2)
                {
                    // Probably user is still typing
                    table.Reject(typeExpr.Token, $"Syntax error");
                    return null;
                }
                if (!leftSymbol.IsAnyType || leftSymbol.IsTypeParam)
                {
                    table.Reject(typeExpr.Token, $"The left side of the '.' must evaluate to a module or type, but it is a {leftSymbol.KindName}");
                    return null;
                }

                // The right side of the "." is only a type name identifier, excluding generic parameters.
                var leftScope = leftSymbol.IsSpecializedType ? leftSymbol.Parent : leftSymbol;
                var rightSymbol = ResolveTypeOrReject(typeExpr[1], table, true, leftScope, useScope, hasGenericParams);
                if (rightSymbol == null)
                    return null;

                if (!rightSymbol.IsAnyType)
                {
                    table.Reject(typeExpr[1].Token, $"The right side of the '.' must evaluate to a module or type, but it is a {rightSymbol.KindName}");
                    return null;
                }

                if (leftSymbol.IsSpecializedType)
                    return table.GetSpecializedType(rightSymbol, ((SymSpecializedType)leftSymbol).Params);

                return rightSymbol;
            }


            // Resolve "fun" or "afun" types
            Symbol ResolveLambdaFunOrReject()
            {
                if (typeExpr.Count < 3)
                {
                    table.Reject(typeExpr.Token, "Syntax error");
                    return null;
                }

                // Generic type arguments: TBD
                //foreach (var generic in typeExpr[0])
                //    mSymbols.Add(new SymTypeArg(scope, generic.Token, 0));

                // NOTE: This holds more info about the function, such as the
                // variable name.  For now, we are going to throw away this
                // extra info, but it might be nice to keep around to show
                // the user.  So for now, these type definitions are identical
                // and consolidated in the symbol table:
                //      @a fun(a int)int   ==   @ fun(int)int
                //      @b fun(b int)int   ==   @ fun(int)int
                // NOTE 2: This scope is only used to resolve the function
                //         parameters, then it is thrown away.
                var funParamsScope = new SymMethod(scope, new Token("$unused"), "$unused");

                var paramTypes = new List<Symbol>();
                var returnTypes = new List<Symbol>();
                var resolved1 = ResolveTypeFunParamsOrReject(funParamsScope, typeExpr[0], paramTypes, false);
                var resolved2 = ResolveTypeFunParamsOrReject(funParamsScope, typeExpr[1], returnTypes, true);
                Debug.Assert(!funParamsScope.Token.Error);
                if (!resolved1 || !resolved2)
                    return null;

                // TBD: Figure out what to do with this (error/exit attribute)
                //if (typeExpr[2].Token.Name != "") // error attribute
                //    returnTypes.Add(typeExpr[2].Token.Name);

                // Add generic "$fun#" symbol to root, where # is the number of generic arguments
                var numGenerics = paramTypes.Count + returnTypes.Count;
                var name = "$" + typeExpr.Token.Name + numGenerics;
                Symbol genericFunType = table.FindOrAddIntrinsicType(name, numGenerics);

                var concreteType = genericFunType;
                return table.GetSpecializedType(concreteType, paramTypes.ToArray(), returnTypes.ToArray());
            }

            // Resolve "fun" or "afun" parameter types. 
            bool ResolveTypeFunParamsOrReject(Symbol paramScope,
                                              SyntaxExpr paramExprs,
                                              List<Symbol> paramTypes,
                                              bool isReturn)
            {
                bool resolved = true;
                foreach (var pType in paramExprs)
                {
                    if (pType is SyntaxError)
                        continue;
                    var sym = ResolveTypeOrReject(pType[0], table, false, paramScope, useScope);
                    if (sym == null)
                    {
                        resolved = false;
                        continue;
                    }
                    if (sym.IsAnyTypeNotModule)
                    {
                        paramTypes.Add(sym);
                        var newMethodParam = new SymMethodParam(paramScope, pType.Token);
                        newMethodParam.ParamOut = isReturn;
                        newMethodParam.Token.AddInfo(newMethodParam);
                        table.AddOrReject(newMethodParam); // TBD: Fix
                    }
                    else
                    {
                        table.Reject(sym.Token, $"Expecting a type, but got a {sym.KindName}");
                        resolved = false;
                    }
                }
                return resolved;
            }

            // Resolve 'List<int>', 'Map<str,str>', *<int>, etc.
            // First token is '$' for 'List<int>', but can also
            // be any unary type symbol ('*', '?', '^', etc)
            Symbol ResolveGenericTypeOrReject()
            {
                // Resolve type parameters
                if (!ResolveTypeGenericParamsOrReject(out var typeParent, out var typeParams))
                    return null;

                if (typeParams.Count == 0)
                {
                    table.Reject(typeExpr.Token, "Syntax error, unexpected empty type argument list");
                    return null;
                }

                // Reject incorrect number of type arguments, for example, `List<int,int>`
                var concreteType = typeParent.IsSpecializedType ? typeParent.Parent : typeParent;
                if (concreteType.GenericParamCount() != typeParams.Count)
                {
                    RejectTypeArgLeftDotRight(typeExpr, table,
                        $"Expecting {concreteType.GenericParamCount()} generic parameter(s), but got {typeParams.Count}");
                    if (!table.NoCompilerChecks)
                        return null;
                }

                if (typeParent is SymSpecializedType pt)
                {
                    Debug.Assert(pt.Returns.Length == 0); // TBD: Fix for functions
                    for (int i = 0; i < pt.Params.Length; i++)
                        typeParams.Insert(i, pt.Params[i]);
                }

                return table.GetSpecializedType(concreteType, typeParams.ToArray());
            }


            bool ResolveTypeGenericParamsOrReject(out Symbol typeParent, out List<Symbol> typeParams)
            {
                if (typeExpr.Count == 0)
                {
                    table.Reject(typeExpr.Token, "Syntax error");
                    typeParent = null;
                    typeParams = null;
                    return false;
                }

                var resolved = true;
                int typeParamIndex = 0;
                if (typeExpr.Token != ParseZurf.VT_TYPE_ARG)
                {
                    // Unary type symbol symbol
                    typeParent = table.UnaryTypeSymbols[typeExpr.Token];
                }
                else
                {
                    // Parameter list, eg: typeParent<T1,T2,...>
                    typeParamIndex = 1;
                    typeParent = ResolveTypeOrReject(typeExpr[0], table, false, scope, useScope, true);
                    if (typeParent == null)
                        resolved = false;
                    else if (!typeParent.IsAnyTypeNotModule)
                    {
                        table.Reject(typeExpr[typeParamIndex].Token, $"Expecting a type, but got a {typeParent.KindName}");
                        resolved = false;
                    }
                }

                // Process type parameters
                typeParams = new List<Symbol>();
                for (; typeParamIndex < typeExpr.Count; typeParamIndex++)
                {
                    var sym = ResolveTypeOrReject(typeExpr[typeParamIndex], table, false, scope, useScope);
                    if (sym == null)
                        resolved = false;
                    else if (sym.IsAnyTypeNotModule)
                        typeParams.Add(sym);
                    else
                    {
                        table.Reject(typeExpr[typeParamIndex].Token, $"Expecting a type, but got a {sym.KindName}");
                        resolved = false;
                    }
                }
                return resolved;
            }
        }

        /// <summary>
        /// Reject the symbol that actually caused the problem.
        /// For type args, it's on the left.  For dots, it's on the right.
        /// </summary>
        static public void RejectTypeArgLeftDotRight(SyntaxExpr expr, SymbolTable table, string errorMessage)
        {
            table.Reject(FindTypeArgLeftDotRight(expr), errorMessage);
        }

        /// <summary>
        /// Find the type name identifier.
        /// For type args, it's on the left.  For dots, it's on the right.
        /// e.g. Zurfur.List<byte> = `List`
        /// </summary>
        static public Token FindTypeArgLeftDotRight(SyntaxExpr expr)
        {
            bool walking;
            do
            {
                walking = false;
                if (expr.Token == ParseZurf.VT_TYPE_ARG && expr.Count >= 1 && expr[0].Token.Name != "")
                {
                    walking = true;
                    expr = expr[0];
                }
                if (expr.Token == "." && expr.Count >= 2 && expr[1].Token != "")
                {
                    walking = true;
                    expr = expr[1];
                }
            } while (walking);

            return expr.Token;
        }

        /// <summary>
        /// Add outer generic parameters to the concrete type
        /// e.g: OuterType`1.InnerType => OuterType`1.InnerType<#0>
        /// </summary>
        public static Symbol AddOuterGenericParameters(SymbolTable table, Symbol symbol, Symbol outerScope)
        {
            Debug.Assert(symbol.IsType);
            var genericParamCount = outerScope.GenericParamTotal();
            if (genericParamCount == 0)
                return symbol;

            var genericParams = new List<Symbol>();
            for (int i = 0; i < genericParamCount; i++)
                genericParams.Add(table.GetGenericParam(i));

            return table.GetSpecializedType(symbol, genericParams.ToArray());
        }

        /// <summary>
        /// Find a type or module at the current scope.
        /// Return null if not found.
        /// </summary>
        static public Symbol FindLocalTypeOrReject(Token name, SymbolTable table, Symbol scope)
        {
            if (!scope.TryGetPrimary(name, out var symbol))
            {
                table.Reject(name, $"'{name}' is not a member of '{scope}'");
                return null;
            }
            if (symbol.IsAnyType)
                return symbol;
            table.Reject(name, $"'{name}' is not a type, it is a '{symbol.KindName}'");
            return null;
        }

        /// <summary>
        /// Find a type or module in the current scope.
        /// If it's not found, scan use statements for all occurences. 
        /// Marks an error if undefined or duplicate.  Returns null on error.
        /// TBD: If symbol is unique in this package, but duplicated in an
        /// external package, is that an error?  Yes for now.
        /// </summary>
        static public Symbol FindGlobalTypeOrReject(Token name, SymbolTable table, Symbol scope, UseSymbolsFile use, out bool foundInScope)
        {
            var symbol = FindTypeInScopeWalk(name.Name, scope);
            if (symbol != null)
            {
                foundInScope = true;
                return symbol;
            }
            foundInScope = false;

            // Look for types in 'use' symbols
            var symbols = new List<Symbol>(); // TBD: Be kind to GC
            if (use.UseSymbols.TryGetValue(name.Name, out var useSymbols))
                foreach (var sym in useSymbols)
                    if (sym.IsType || sym.IsModule)
                        symbols.Add(sym);

            if (symbols.Count == 0)
            {
                table.Reject(name, "Undefined type name");
                return null;
            }
            if (symbols.Count > 1)
            {
                table.Reject(name, $"Multiple types found: '{symbols[0]}' and '{symbols[1]}'");
                return null;
            }
            return symbols[0];
        }

        /// <summary>
        /// Find the type or module in the scope, or null if not found.
        /// Scope includes all parent types and just one parent module
        /// (e.g. `Zurfur.Io` does not include `Zurfur`)
        /// </summary>
        static public Symbol FindTypeInScopeWalk(string name, Symbol scope)
        {
            while (!scope.IsModule)
            {
                if (scope.TryGetPrimary(name, out var s1) && s1.IsAnyType)
                    return s1;
                scope = scope.Parent;
            }
            // This is a module
            if (scope.TryGetPrimary(name, out var s2) && s2.IsAnyType)
                return s2;

            return null;
        }

    }
}
