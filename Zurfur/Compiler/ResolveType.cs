﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    static class Resolver
    {
        static WordMap<string> sUnaryTypeSymbols = new WordMap<string>()
        {
            {"*", "Zurfur.RawPointer`1" },
            {"^", "Zurfur.Pointer`1" },
            {"ref", "Zurfur.Ref`1" },
            {"?", "Zurfur.Nullable`1"},
            {"[", "Zurfur.Span`1" },
            {"!", "Zurfur.Result`1" }
        };

        /// <summary>
        /// Resolve a type.  Non-generic types are found in the symbol table
        /// and given a full name (e.g. 'int' -> 'Zufur.int').  Generic types
        /// must have all type arguments resolved as well.
        /// Return symbol is always a module, type, or type parameter.
        /// On error, the token is rejected and null is returned.
        /// </summary>
        public static Symbol Resolve(
                SyntaxExpr typeExpr,
                SymbolTable table,
                bool isDot,
                Symbol searchScope,
                UseSymbolsFile useSymbols,
                bool hasGenericParams = false)
        {
            // There will also be a syntax error
            if (typeExpr == null || typeExpr.Token.Name == "")
                return null;
            Debug.Assert(!searchScope.IsSpecializedType);

            if (typeExpr.Token.Name == ".")
                return ResolveDot();

            if (typeExpr.Token == "fun" || typeExpr.Token == "afun")
                return ResolveLambdaFun();

            if (typeExpr.Token == ParseZurf.VT_TYPE_ARG)
                return ResolveGenericType();

            // Resolve regular symbol
            bool foundInScope = false;
            var symbol = isDot ? FindLocalType(typeExpr.Token, table, searchScope)
                               : FindGlobalType(typeExpr.Token, table, searchScope, useSymbols, out foundInScope);
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
                symbol = GetTypeWithGenericParameters(table, symbol, symbol.Parent.GenericParamTotal());

            return symbol;

            // On error, the token is rejected and null is returned.
            Symbol ResolveDot()
            {
                var leftSymbol = Resolve(typeExpr[0], table, isDot, searchScope, useSymbols);
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
                var rightSymbol = Resolve(typeExpr[1], table, true, leftScope, useSymbols, hasGenericParams);
                if (rightSymbol == null)
                    return null;

                if (!rightSymbol.IsAnyType)
                {
                    table.Reject(typeExpr[1].Token, $"The right side of the '.' must evaluate to a module or type, but it is a {rightSymbol.KindName}");
                    return null;
                }

                if (leftSymbol.IsSpecializedType)
                    return table.FindOrCreateSpecializedType(rightSymbol, ((SymSpecializedType)leftSymbol).Params);

                return rightSymbol;
            }


            // Resolve "fun" or "afun" types.
            // On error, the token is rejected and null is returned.
            Symbol ResolveLambdaFun()
            {
                // TBD: Revisit how to do this
                return null;

                //if (typeExpr.Count < 3)
                //{
                //    table.Reject(typeExpr.Token, "Syntax error");
                //    return null;
                //}
                //// Create an anonymous type to hold the function type.
                //// NOTE: This is not finished, just temporary for now.
                //var funParams = new Symbol(SymKind.Type, table.AnonymousTypes, "");
                //ResolveMethodParams(typeExpr[0], table, funParams, searchScope, useSymbols, false);
                //ResolveMethodParams(typeExpr[1], table, funParams, searchScope, useSymbols, true);

                //funParams.SetLookupName(GetFunctionName("$fun", funParams));
                //return table.FindOrAddAnonymousType(funParams);
            }


            // Resolve 'List<int>', 'Map<str,str>', *<int>, etc.
            // Token is '$' for VT_TYPE_ARG.
            // On error, the token is rejected and null is returned.
            Symbol ResolveGenericType()
            {
                // Resolve type parameters
                var typeName = ResolveTypeName();
                var typeParams = ResolveTypeArgs(typeExpr, table, searchScope, useSymbols);
                if (typeName == null || typeParams == null)
                    return null;

                // Reject incorrect number of type arguments, for example, `List<int,int>`
                var typeNameConcrete = typeName.IsSpecializedType ? typeName.Parent : typeName;
                if (typeNameConcrete.GenericParamCount() != typeParams.Count)
                {
                    RejectTypeArgLeftDotRight(typeExpr, table,
                        $"Expecting {typeNameConcrete.GenericParamCount()} generic parameter(s), but got {typeParams.Count}");
                    if (!table.NoCompilerChecks)
                        return null;
                }

                // Add parent type parameters (i.e. Map<int,int>.KvPair -> Map.KeyVal<int,int>)
                if (typeName is SymSpecializedType pt)
                {
                    for (int i = 0; i < pt.Params.Length; i++)
                        typeParams.Insert(i, pt.Params[i]);
                }
                return table.FindOrCreateSpecializedType(typeNameConcrete, typeParams.ToArray());
            }


            // On error, the token is rejected and null is returned.
            Symbol ResolveTypeName()
            {
                if (typeExpr.Count == 0)
                {
                    table.Reject(typeExpr.Token, "Syntax error");
                    return null;
                }

                Symbol typeParent = null;
                if (sUnaryTypeSymbols.TryGetValue(typeExpr[0].Token, out var unaryTypeName))
                {
                    typeParent = table.Lookup(unaryTypeName);
                    if (typeParent == null)
                        table.Reject(typeExpr[0].Token, $"Base library doesn't contain '{unaryTypeName}'");
                }
                else
                {
                    // Parameter list, eg: typeParent<T1,T2,...>
                    typeParent = Resolve(typeExpr[0], table, false, searchScope, useSymbols, true);
                }

                if (typeParent != null && !typeParent.IsAnyTypeNotModule)
                {
                    table.Reject(typeExpr[0].Token, $"Expecting a type, but got a {typeParent.KindName}");
                    typeParent = null;
                }

                // Process type parameters
                return typeParent;
            }
        }

        // Get the function name: "`#(t1,t2...)(r1,r2...)"
        //      # - Number of generic parameters
        //      t1,t2... - Parameter types
        //      r1,r2... - Return types
        public static string GetFunctionName(string simpleName, Symbol method)
        {
            var genericsCount = method.GenericParamCount();
            var mp = method.ChildrenFilter(SymKind.MethodParam).Where(child => !child.ParamOut).ToList();
            var mpr = method.ChildrenFilter(SymKind.MethodParam).Where(child => child.ParamOut).ToList();
            mp.Sort((a, b) => a.Order.CompareTo(b.Order));
            mpr.Sort((a, b) => a.Order.CompareTo(b.Order));
            var functionName = simpleName
                        + (genericsCount == 0 ? "" : "`" + genericsCount)
                        + "(" + string.Join(",", mp.ConvertAll(a => a.TypeName)) + ")"
                        + "(" + string.Join(",", mpr.ConvertAll(a => a.TypeName)) + ")";
            return functionName;
        }


        /// <summary>
        /// Resolves just the type arguments, but not type name.
        /// e.g. `Map<int,str>` ignores `Map`, returns a list of [`int`,`str`].
        /// On error, returns NULL and rejects the token.
        /// </summary>
        public static List<Symbol> ResolveTypeArgs(
            SyntaxExpr typeExpr, SymbolTable table, Symbol searchScope, UseSymbolsFile useScope)
        {
            bool resolved = true;
            List<Symbol> typeParams = new List<Symbol>();
            foreach (var typExprParam in typeExpr.Skip(1))
            {
                var sym = Resolve(typExprParam, table, false, searchScope, useScope);
                if (sym == null)
                    resolved = false;
                else if (sym.IsAnyTypeNotModule)
                    typeParams.Add(sym);
                else
                {
                    table.Reject(typExprParam.Token, $"Expecting a type, but got a {sym.KindName}");
                    resolved = false;
                }
            }

            return resolved ? typeParams : null;
        }

        public static void ResolveMethodParams(
            SyntaxExpr parameters,
            SymbolTable table,
            Symbol method,
            Symbol searchScope,
            UseSymbolsFile useSymbols,
            bool isReturn)
        {
            if (parameters is SyntaxError)
                return;

            foreach (var expr in parameters)
            {
                if (expr is SyntaxError)
                    continue;
                Debug.Assert(expr.Count >= 3);

                var token = expr.Token == "" ? expr[1].Token : expr.Token;
                var name = expr.Token == "" ? "$0" : expr.Token.Name;
                var methodParam = new Symbol(SymKind.MethodParam, method, token, name);
                methodParam.ParamOut = isReturn;
                methodParam.Type = Resolve(expr[0], table, false, searchScope, useSymbols);
                if (methodParam.Type == null)
                    continue; // Unresolved symbol

                if (!(methodParam.Type.IsAnyTypeNotModule || table.NoCompilerChecks))
                    RejectTypeArgLeftDotRight(expr[0], table, 
                        $"The symbol is not a type, it is a {methodParam.Type.KindName}");
                expr.Token.AddInfo(methodParam);

                // Add qualifiers
                foreach (var qualifier in expr[2])
                    methodParam.SetQualifier(qualifier.Token);

                table.AddOrReject(methodParam);
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
        /// Find or create a type with outer generic parameters for this concrete type.
        /// e.g: OuterType`1.InnerType => OuterType`1.InnerType<#0>
        /// </summary>
        public static Symbol GetTypeWithGenericParameters(SymbolTable table, Symbol type, int numGenerics)
        {
            Debug.Assert(type.IsType);
            if (numGenerics == 0)
                return type;

            var genericParams = new List<Symbol>();
            for (int i = 0; i < numGenerics; i++)
                genericParams.Add(table.GetGenericParam(i));

            return table.FindOrCreateSpecializedType(type, genericParams.ToArray());
        }

        /// <summary>
        /// Find a type or module at the current scope.
        /// When not found, the token is rejected and null is returned.
        /// </summary>
        static public Symbol FindLocalType(Token name, SymbolTable table, Symbol scope)
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
        /// On error (undefined or duplicate), the token is rejected and
        /// null is returned.
        /// 
        /// TBD: If symbol is unique in this package, but duplicated in an
        /// external package, is that an error?  Yes for now.
        /// </summary>
        static public Symbol FindGlobalType(Token name, SymbolTable table, Symbol scope, UseSymbolsFile use, out bool foundInScope)
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
        /// Errors do not reject the symbol.
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
