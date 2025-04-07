using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Zurfur.Vm;
using Gosub.Lex;

namespace Zurfur.Compiler;

/// <summary>
/// Utilities used when compiling the header and code
/// </summary>
static class Resolver
{
    /// <summary>
    /// Resolve a type.  Non-generic types are found in the symbol table
    /// and given a full name (e.g. 'int' -> 'Zufur.int').  Generic types
    /// must have all type arguments resolved as well.
    /// Return symbol is always a module, type, or type parameter.
    /// On error, the token is rejected and null is returned.
    /// </summary>
    public static Symbol? Resolve(
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
        Debug.Assert(!searchScope.IsSpecialized);

        if (typeExpr.Token.Name == ".")
            return ResolveDot();

        if (typeExpr.Token == ParseZurf.VT_TYPE_ARG)
            return ResolveGenericType();

        if (typeExpr.Token == "()")
            return table.CreateTuple(Array.Empty<Symbol>());
        if (typeExpr.Token == "(")
            return ResolveTupleType(typeExpr);

        if (typeExpr.Token == "fun" || typeExpr.Token == "afun")
            return ResolveLambdaFun();

        // Resolve regular symbol
        var symbol = isDot ? FindLocalType(typeExpr.Token, table, searchScope)
                           : FindGlobalType(typeExpr.Token, table, searchScope, useSymbols);
        if (symbol == null)
            return null; // Error already marked

        if (!symbol.IsAnyTypeOrModule)
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

        return symbol;

        // On error, the token is rejected and null is returned.
        Symbol? ResolveDot()
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
            if (!leftSymbol.IsAnyTypeOrModule || leftSymbol.IsTypeParam)
            {
                table.Reject(typeExpr.Token, "The left side of the '.' must evaluate "
                    + $"to a module or type, but it is a {leftSymbol.KindName}");
                return null;
            }

            // The right side of the "." is only a type name identifier, excluding generic parameters.
            var leftScope = leftSymbol.IsSpecialized ? leftSymbol.Parent! : leftSymbol;
            var rightSymbol = Resolve(typeExpr[1], table, true, leftScope, useSymbols, hasGenericParams);
            if (rightSymbol == null)
                return null;

            if (!rightSymbol.IsAnyTypeOrModule)
            {
                table.Reject(typeExpr[1].Token, "The right side of the '.' must evaluate "
                    + $"to a module or type, but it is a {rightSymbol.KindName}");
                return null;
            }

            // Generic argument
            if (rightSymbol.IsGenericArg)
            {
                var paramNum = rightSymbol.GenericParamNum();
                if (paramNum >= leftSymbol.TypeArgs.Length)
                {
                    table.Reject(typeExpr[1].Token, "Compiler Error: Generic type arg index out of range");
                    return null;
                }
                return leftSymbol.TypeArgs[paramNum];
            }

            if (leftSymbol.IsSpecialized)
                return table.CreateSpecializedType(rightSymbol, leftSymbol.TypeArgs);

            return rightSymbol;
        }

        // Resolve 'List<int>', 'Map<str,str>', *<int>, etc.
        // The token is VT_TYPE_ARG (i.e. '$') instead of '<'. 
        // On error, the token is rejected and null is returned.
        Symbol? ResolveGenericType()
        {
            // Resolve type parameters
            var typeName = ResolveTypeName();
            var typeParams = ResolveTypeArgs(typeExpr, table, searchScope, useSymbols);
            if (typeName == null || typeParams == null)
                return null;

            // Reject incorrect number of type arguments, for example, `List<int,int>`
            if (typeName.GenericParamCount() != typeParams.Count)
            {
                RejectTypeArgLeftDotRight(typeExpr, table,
                    $"Expecting {typeName.GenericParamCount()} generic parameter(s), but got {typeParams.Count}");
                if (!table.NoCompilerChecks)
                    return null;
            }

            // Add parent type parameters (i.e. Map<int,int>.KvPair -> Map.KeyVal<int,int>)
            if (typeName.IsSpecialized)
            {
                for (int i = 0; i < typeName.TypeArgs.Length; i++)
                    typeParams.Insert(i, typeName.TypeArgs[i]);
            }
            return table.CreateSpecializedType(typeName.Concrete, typeParams.ToArray());
        }


        // On error, the token is rejected and null is returned.
        Symbol? ResolveTypeName()
        {
            if (typeExpr.Count == 0)
            {
                table.Reject(typeExpr.Token, "Syntax error");
                return null;
            }

            Symbol? typeParent = null;
            if (SymTypes.UnaryTypeSymbols.TryGetValue(typeExpr[0].Token, out var unaryTypeName))
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

            if (typeParent != null && !typeParent.IsAnyType)
            {
                table.Reject(typeExpr[0].Token, $"Expecting a type, but got a {typeParent.KindName}");
                typeParent = null;
            }

            // Process type parameters
            return typeParent;
        }

        // Resolve a tuple: '(int,List<str>,f32)', etc.
        Symbol? ResolveTupleType(SyntaxExpr tupleExprs)
        {
            bool resolved = true;
            var typeParams = new List<Symbol>();
            var tupleSymbols = new List<Symbol>();
            foreach (var tupleExpr in tupleExprs)
            {
                if (tupleExpr.Count == 0)
                    return null;  // Syntax error is already rejected
                var tupleType = Resolve(tupleExpr[0], table, false, searchScope, useSymbols);
                if (tupleType == null)
                    resolved = false;
                else if (tupleType.IsAnyType)
                {
                    typeParams.Add(tupleType);

                    // Create tuple parameter symbol
                    if (tupleExpr.Token != "")
                    {
                        var funParam = new Symbol(SymKind.TupleParam, table.EmptyTuple, searchScope.Path, tupleExpr.Token);
                        tupleExpr.Token.AddInfo(funParam);
                        funParam.Type = tupleType;
                        tupleSymbols.Add(funParam);
                    }
                }
                else
                {
                    table.Reject(tupleExpr.Token, $"Expecting a type, but got a {tupleType.KindName}");
                    resolved = false;
                }
            }
            if (tupleSymbols.Count != 0 && tupleSymbols.Count != typeParams.Count)
            {
                foreach (var name in tupleSymbols)
                    table.Reject(name.Token, "Partially named tuple not allowed.  Name all of them, or none of them.");
                tupleSymbols.Clear();
            }
            if (resolved)
                return table.CreateTuple(typeParams.ToArray(),  tupleSymbols.ToArray());
            return null;
        }

        // Resolve "fun" or "afun" types.
        // Returns a specialized generic type, e.g fun(params)(returns) is $lambda<((params),(returns))>
        // On error, the token is rejected and null is returned.
        Symbol? ResolveLambdaFun()
        {
            if (typeExpr.Count < 3)
            {
                table.Reject(typeExpr.Token, "Syntax error");
                return null;
            }
            var paramTuple = ResolveTupleType(typeExpr[0]);
            var returnTuple = ResolveTupleType(typeExpr[1]);
            if (paramTuple == null || returnTuple == null)
                return null;

            return table.CreateLambda(paramTuple, returnTuple);
        }
    }

    /// <summary>
    /// Resolves just the type arguments, but not type name.
    /// e.g. `Map<int,str>` ignores `Map`, returns a list of [`int`,`str`].
    /// On error, returns NULL and rejects the token.
    /// </summary>
    public static List<Symbol>? ResolveTypeArgs(
        SyntaxExpr typeExpr, SymbolTable table, Symbol searchScope, UseSymbolsFile useSymbols)
    {
        bool resolved = true;
        List<Symbol> typeParams = new List<Symbol>();
        foreach (var typExprParam in typeExpr.Skip(1))
        {
            var sym = Resolve(typExprParam, table, false, searchScope, useSymbols);
            if (sym == null)
                resolved = false;
            else if (sym.IsAnyType)
                typeParams.Add(sym);
            else
            {
                table.Reject(typExprParam.Token, $"Expecting a type, but got a {sym.KindName}");
                resolved = false;
            }
        }

        return resolved ? typeParams : null;
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
    /// e.g: OuterType`1 => OuterType<#0>
    /// </summary>
    public static Symbol GetTypeWithGenericParameters(SymbolTable table, Symbol type)
    {
        Debug.Assert(type.IsType && !type.IsSpecialized);
        var numGenerics = type.GenericParamCount();
        if (numGenerics == 0)
            return type;

        var genericParams = new List<Symbol>();
        for (int i = 0; i < numGenerics; i++)
            genericParams.Add(table.GetGenericParam(i));

        return table.CreateSpecializedType(type, genericParams.ToArray());
    }

    /// <summary>
    /// Find a type or module at the current scope.
    /// When not found, the token is rejected and null is returned.
    /// </summary>
    static public Symbol? FindLocalType(Token name, SymbolTable table, Symbol scope)
    {
        if (!scope.TryGetPrimary(name, out var symbol))
        {
            table.Reject(name, $"'{name}' is not a member of '{scope}'");
            return null;
        }
        if (symbol!.IsAnyTypeOrModule)
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
    static public Symbol? FindGlobalType(Token name, SymbolTable table, Symbol scope, UseSymbolsFile use)
    {
        var symbol = FindTypeInScopeWalk(name.Name, scope);
        if (symbol != null)
        {
            return symbol;
        }

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
    static public Symbol? FindTypeInScopeWalk(string name, Symbol? scope)
    {
        while (scope != null && !scope.IsModule)
        {
            if (scope.TryGetPrimary(name, out var s1) && s1!.IsAnyTypeOrModule)
                return s1;
            scope = scope.Parent;
        }
        // This is a module
        if (scope != null && scope.TryGetPrimary(name, out var s2) && s2!.IsAnyTypeOrModule)
            return s2;

        return null;
    }

}
