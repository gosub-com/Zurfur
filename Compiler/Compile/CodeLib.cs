using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Zurfur.Vm;

namespace Zurfur.Compiler;

/// <summary>
/// Utilities used when compiling code (not CompileHeader)
/// </summary>
static class CodeLib
{
    static WordSet s_derefRef = new WordSet("Zurfur.Unsafe.Ref`1");
    static WordSet s_derefPointers = new WordSet("Zurfur.Unsafe.RawPointer`1 Zurfur.Pointer`1");

    // Add all children with the given name (including specialized type)
    public static void AddFunctionsNamedInType(string name, Symbol inType, List<Symbol> symbols)
    {
        AddFunctionsNamedConcrete(name, inType, symbols);
        if (inType.IsSpecialized)
            AddFunctionsNamedConcrete(name, inType.Parent!, symbols);
    }

    // Add all children with the given name (primary or non-extension method)
    public static void AddFunctionsNamedConcrete(string name, Symbol inType, List<Symbol> symbols)
    {
        foreach (var child in inType.ChildrenNamed(name))
            if (child.IsFun)
                symbols.Add(child);
    }

    public static void AddFunctionsNamedInModule(string name, Symbol inModule, List<Symbol> symbols, bool methods)
    {
        foreach (var child in inModule.ChildrenNamed(name))
            if (child.IsFun && child.IsMethod == methods)
                symbols.Add(child);
    }

    // Add methods with first parameter of inType
    public static void AddMethodsInModuleWithType(string name, Symbol inModule, Symbol inType, List<Symbol> symbols)
    {
        // Ignore mut, etc., then just compare the non-specialized type.
        inType = inType.Concrete;
        foreach (var child in inModule.ChildrenNamed(name))
        {
            if (!child.IsFun || !child.IsMethod)
                continue;

            // Compare the non-specialized type
            //      e.g: List<#1> matches List<byte> so we get all functions

            // Static methods use static scope "virtual" parameter
            if (child.StaticScope != null && child.StaticScope.Concrete.FullName == inType.FullName)
                symbols.Add(child);

            // Non-static methods use first parameter
            var parameters = child.FunParamTypes;
            if (child.StaticScope == null && parameters.Length != 0 && parameters[0].Concrete.FullName == inType.FullName)
                symbols.Add(child);
        }
    }

    /// <summary>
    /// Check to see if the types match while inferring paramTypeArgs from argType.
    /// Infer newTypeArgs on success, otherwise return original typeArgs.
    /// NOTE: This can return TRUE even when typeArgs are not inferred
    /// typeArgs are not modified.
    /// </summary>
    public static (bool match, Symbol[] newTypeArgs) InferTypesMatch(Symbol argType, Symbol paramType, Symbol[] paramTypeArgs)
    {
        // Fast paths for non-matching concrete type or no type-args
        if (!paramType.IsGenericArg && argType.Concrete.FullName != paramType.Concrete.FullName)
            return (false, paramTypeArgs);
        if (paramTypeArgs.Length == 0 || !paramType.HasGenericArg)
            return (TypesMatch(argType, paramType), paramTypeArgs);

        // Infer types on unresolved parameters (non-destructively)
        var typeArgsInferred = paramTypeArgs.All(s => s.IsResolved) ? paramTypeArgs : paramTypeArgs.Clone2();
        var match = InferTypesMatchNoRestore(argType, paramType, typeArgsInferred);
        return (match, match ? typeArgsInferred : paramTypeArgs);
    }

    /// <summary>
    /// Check to see if the types match while inferring paramTypeArgs from argType.
    /// Does not restore type args on failure.
    /// </summary>
    static bool InferTypesMatchNoRestore(Symbol argType, Symbol paramType, Symbol[] paramTypeArgs)
    {
        // If it's a generic arg, use the given parameter type
        if (paramType.IsGenericArg)
        {
            var paramNum = paramType.GenericParamNum();
            Debug.Assert(paramNum < paramTypeArgs.Length);
            if (paramTypeArgs[paramNum].IsResolved)
            {
                // If types do not match, it's a contradiction, e.g. user calls f(0, "x") on f<T>(x T, y T).
                // TBD: Give better error message (this just fails with 'wrong number of type args')
                return TypesMatch(paramTypeArgs[paramNum], argType);
            }
            else
            {
                // Inferred a type argument
                paramTypeArgs[paramNum] = argType;
                return true;
            }
        }
        if (paramType.Concrete.FullName != argType.Concrete.FullName)
            return false;

        // If they are both the same generic type, check type parameters
        for (int i = 0; i < paramType.TypeArgs.Length; i++)
        {
            if (!InferTypesMatchNoRestore(argType.TypeArgs[i], paramType.TypeArgs[i], paramTypeArgs))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Check to see if the symbol types match, ignoring tuple names
    /// and references.
    /// </summary>
    public static bool TypesMatch(Symbol a, Symbol b)
    {
        a = DerefRef(a);
        b = DerefRef(b);

        if (a.FullName == b.FullName)
            return true;

        if (!a.IsSpecialized
                || !b.IsSpecialized
                || a.Parent!.FullName != b.Parent!.FullName
                || a.TypeArgs.Length != b.TypeArgs.Length)
            return false;

        for (int i = 0; i < a.TypeArgs.Length; i++)
            if (!TypesMatch(a.TypeArgs[i], b.TypeArgs[i]))
                return false;

        return true;
    }

    public static Symbol DerefRef(Symbol type) => Deref(type, s_derefRef);
    public static Symbol DerefPointers(Symbol type) => Deref(type, s_derefPointers);

    // Dereference the given type names
    public static Symbol Deref(Symbol type, WordSet typeNames)
    {
        // Auto-dereference pointers and references
        if (type.IsSpecialized
            && type.TypeArgs.Length != 0
            && typeNames.Contains(type.Parent!.FullName))
        {
            // Move up to non-generic concrete type
            // TBD: Preserve concrete type parameters
            type = type.TypeArgs[0];
        }
        return type;
    }


}
