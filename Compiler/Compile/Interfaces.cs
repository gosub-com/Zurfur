using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Zurfur.Compiler;
using Zurfur.Vm;
using static Zurfur.Compiler.CodeLib;

namespace Zurfur.Compiler;

/// <summary>
/// Convert concrete types to interface types while inferring type args
/// </summary>
class Interfaces
{
    record struct IfaceFunInfo(Symbol IfaceFun, List<Symbol> Candidates);


    Dictionary<string, InterfaceInfo> _interfaces = new();
    //SymbolTable _table;

    public Interfaces(SymbolTable symbolTable)
    {
        //_table = symbolTable;
    }

    // If possible, convert concrete type to an interface.
    // If not possible, generate a list of what's missing.
    // On success, return newly inferred typeArgs (return old typeArgs on fail)
    // typeArgs is not modified
    public InterfaceInfo ConvertToInterfaceInfo(SymbolTable table, Symbol concrete, Symbol iface, Symbol[] typeArgs)
    {
        // Memoize interface conversion along with type args per call
        var name = $"{concrete}->{iface}:[{string.Join(",", typeArgs.Select(s => s.FullName))}]";
        if (_interfaces.TryGetValue(name, out var memInfo))
            return memInfo;

        _interfaces[name] = new InterfaceInfo(concrete, iface, [], CallCompatible.GeneratingNow, typeArgs);
        var info = ConvertToInterfaceInfoMemoized(table, concrete, iface, typeArgs);
        _interfaces[name] = info;
        return info;
    }


    // If possible, convert concrete type to an interface.
    // If not possible, generate a list of what's missing.
    // On success, return newly inferred typeArgs (return old typeArgs on fail)
    // typeArgs is not modified
    InterfaceInfo ConvertToInterfaceInfoMemoized(SymbolTable table, Symbol concrete, Symbol iface, Symbol[] typeArgs)
    {
        // Generic arguments must be resolved
        Debug.Assert(iface.GenericParamCount() == 0 || iface.IsSpecialized);
        Debug.Assert(concrete.GenericParamCount() == 0 || concrete.IsSpecialized);
        Debug.Assert(iface.IsInterface);

        // Check identity interface
        if (TypesMatch(concrete, iface))
        {
            var ifaceIdentity = new InterfaceInfo(concrete, iface, [], CallCompatible.Compatible, typeArgs);
            return ifaceIdentity;
        }

        // Fail interface-to-interface conversion
        if (concrete.IsInterface)
        {
            var ifaceFail = new InterfaceInfo(concrete, iface, [], CallCompatible.InterfaceToInterfaceConversionNotSupported, typeArgs);
            return ifaceFail;
        }

        // Keep typeArgs immutable (i.e. clone it if it could be changed)
        var hadUnresolvedTypeArgs = typeArgs.Any(s => s.IsUnresolved);
        var typeArgsInferred = hadUnresolvedTypeArgs ? typeArgs.Clone2() : typeArgs;

        // Get interface functions and implementing candidates
        var ifaceFunCandidates = new List<IfaceFunInfo>();
        foreach (var ifaceFun in iface.Concrete.Children.OrderBy(s => s.SimpleName))
        {
            if (!ifaceFun.IsFun)
                continue; // Skip type parameters
            if (ifaceFun.IsStatic)
                continue;  // TBD: Deal with static

            // Find functions in the concrete type or its module
            var concreteFuns = new List<Symbol>();
            AddFunctionsNamedInType(ifaceFun.SimpleName, concrete, concreteFuns);
            AddFunctionsInModuleWithType(ifaceFun.SimpleName, concrete.ParentModule, concrete, concreteFuns);

            // Specialize interface and functions.
            // NOTE: Specialized interfaces don't currently carry
            //       the specialized function signatures.
            Symbol ifaceFunSpecialized = ifaceFun;
            if (iface.IsSpecialized)
                ifaceFunSpecialized = table.CreateSpecializedType(ifaceFun, iface.TypeArgs);
            if (concrete.IsSpecialized)
                for (var i = 0; i < concreteFuns.Count; i++)
                    concreteFuns[i] = table.CreateSpecializedType(concreteFuns[i], concrete.TypeArgs);

            ifaceFunCandidates.Add(new(ifaceFunSpecialized, concreteFuns));
        }

        // Concrete type doesn't implement function at all,
        // give user concise info on which functions are missing.
        if (ifaceFunCandidates.Any(f => f.Candidates.Count == 0))
        {
            return new InterfaceInfo(concrete, table.ReplaceGenericTypeParams(iface, typeArgs),
                ifaceFunCandidates.Select(s => s.IfaceFun).ToList(), CallCompatible.InterfaceNotImplementedByType, typeArgs);
        }

        // Find list of implemented/failed functions
        var failedFuns = new List<Symbol>();        // From interface
        var implementedFuns = new List<Symbol>();   // From concrete
        List<Symbol>? typeArgsAmbiguous = null;
        foreach (var ifaceFunInfo in ifaceFunCandidates)
        {
            // Find functions in the concrete type or its module
            var concreteFuns = ifaceFunInfo.Candidates;
            Symbol ifaceFun = ifaceFunInfo.IfaceFun;

            // Find interface with exact matching parametets
            var noMatch = true;
            foreach (var concreteFun in concreteFuns)
            {
                if (!concreteFun.IsFun)
                    continue;  // TBD: Match interface getter/setter with concrete type

                if (concreteFun.IsStatic)
                    continue; // TBD: Deal with static

                // NOTE: First parameter is the interface
                var ifaceParams = ifaceFun.FunParamTypes;
                var funParams = concreteFun.FunParamTypes;
                Debug.Assert(funParams.Length != 0);
                Debug.Assert(funParams[0].Concrete.FullName == concrete.Concrete.FullName);
                Debug.Assert(ifaceParams.Length != 0 && ifaceParams[0].Concrete.FullName == iface.Concrete.FullName);

                var (match, inferred) = AreInterfaceParamsCompatible(table, ifaceFun, concreteFun, typeArgs);

                // Resolve inferred types and detect first ambiguous
                for (int i = 0; i < inferred.Length; i++)
                {
                    if (typeArgsInferred[i].IsUnresolved)
                    {
                        typeArgsInferred[i] = inferred[i];
                    }
                    else if (inferred[i].IsResolved
                             && inferred[i].FullName != typeArgsInferred[i].FullName)
                    {
                        if (typeArgsAmbiguous == null)
                            typeArgsAmbiguous = [
                                table.ReplaceGenericTypeParams(iface, typeArgsInferred),
                                    table.ReplaceGenericTypeParams(iface, inferred)];
                    }
                }

                if (match)
                {
                    implementedFuns.Add(concreteFun);
                    noMatch = false;
                }
            }
            if (noMatch)
                failedFuns.Add(ifaceFun);
        }

        // Non-implemented functions
        if (failedFuns.Count != 0)
        {
            return new InterfaceInfo(concrete, table.ReplaceGenericTypeParams(iface, typeArgs),
                failedFuns, CallCompatible.InterfaceNotImplementedByType, typeArgs);
        }

        if (typeArgsAmbiguous != null)
        {
            return new InterfaceInfo(concrete, table.ReplaceGenericTypeParams(iface, typeArgs),
                typeArgsAmbiguous, CallCompatible.TypeArgsAmbiguousFromInterfaceFun, typeArgs);
        }

        if (typeArgsInferred.Any(s => s.IsUnresolved))
        {
            return new InterfaceInfo(concrete, table.ReplaceGenericTypeParams(iface, typeArgs),
                [table.ReplaceGenericTypeParams(iface, typeArgsInferred)], CallCompatible.TypeArgsNotInferrableFromInterfaceParameter, typeArgs);
        }

        // Implemented and resolved
        var ifacePassParameterized = table.ReplaceGenericTypeParams(iface, typeArgsInferred);

        return new InterfaceInfo(concrete, ifacePassParameterized, implementedFuns, CallCompatible.Compatible, typeArgsInferred);
    }


    // Match parameters to arguments and infer unresolved type args.
    // typeArgs is not modified
    // TBD: Review similarity to AreFunParamsCompatible
    (bool match, Symbol[] inferred) AreInterfaceParamsCompatible(
        SymbolTable table, Symbol ifaceFun, Symbol concreteFun, Symbol[] typeArgs)
    {
        var ifaceParams = ifaceFun.FunParamTypes;
        var funParams = concreteFun.FunParamTypes;

        // Keep typeArgs immutable (i.e. clone it if it could be changed)
        var typeArgsInferred = typeArgs;
        if (typeArgs.Any(s => s.IsUnresolved))
            typeArgsInferred = typeArgsInferred.Clone2();

        if (funParams.Length != ifaceParams.Length)
            return (false, typeArgs);

        // Verify parameters
        // NOTE: Ignore the first parameter because it is the interface
        for (int i = 1; i < funParams.Length; i++)
        {
            var compat = IsInterfaceParamConvertable(table, funParams[i], ifaceParams[i], typeArgs, typeArgsInferred);
            if (compat != CallCompatible.Compatible)
                return (false, typeArgs);
        }

        // Verify returns
        var ifaceReturns = ifaceFun.FunReturnTypes;
        var funReturns = concreteFun.FunReturnTypes;
        if (ifaceReturns.Length != funReturns.Length)
            return (false, typeArgs);
        for (int i = 0; i < funReturns.Length; i++)
        {
            var compat = IsInterfaceParamConvertable(table, funReturns[i], ifaceReturns[i], typeArgs, typeArgsInferred);
            if (compat != CallCompatible.Compatible)
                return (false, typeArgs);
        }

        return (true, typeArgsInferred);
    }


    // Can the given argument be converted to the interface parameter type?
    CallCompatible IsInterfaceParamConvertable(SymbolTable table, Symbol argType, Symbol paramType, Symbol[] typeArgs, Symbol[] inferTypeArgs)
    {
        // TBD: Figure out ref and mutability
        //argType = DerefRef(argType);
        //paramType = DerefRef(paramType);

        var (match, inferred) = InferTypesMatch(argType, paramType, typeArgs);
        if (match)
        {
            // Infer interface type args
            for (int i = 0; i < typeArgs.Length; i++)
            {
                if (inferTypeArgs[i].IsUnresolved && inferred[i].IsResolved)
                    inferTypeArgs[i] = inferred[i];
                else if (inferTypeArgs[i].FullName != inferred[i].FullName)
                    return CallCompatible.TypeArgsAmbiguousFromInterfaceParameter;
            }
            return CallCompatible.Compatible;
        }

        // Implicit conversion to interface type?
        if (!paramType.IsInterface)
            return CallCompatible.IncompatibleParameterTypes;
        if (argType.IsInterface && paramType.IsInterface)
            return CallCompatible.InterfaceToInterfaceConversionNotSupported;

        Debug.Assert(paramType.GenericParamCount() == 0 || paramType.IsSpecialized);
        var ifaceConversion = ConvertToInterfaceInfo(table, argType, paramType, typeArgs);

        // Invalid interface conversion
        if (ifaceConversion.Compatibility != CallCompatible.Compatible
                && ifaceConversion.Compatibility != CallCompatible.GeneratingNow)
            return ifaceConversion.Compatibility;

        // Converting the current interface
        if (ifaceConversion.Compatibility == CallCompatible.GeneratingNow)
        {
            // When all type params are resolved, we are good to go
            // because the subsitution below should always succeed
            if (ifaceConversion.TypeArgs?.All(ta => ta.IsResolved) ?? true)
            {
                // ***TBD***: Still working on this, all should be good
                //return CallCompatible.Compatible;
            }
            else
            {

                // ***TBD***: Still working on this, this is not good yet
                // Infer unresolved type parameters.  Substitute
                // concrete for interface, and infer the type args.
                Debug.Assert(false);
                //return CallCompatible.Compatible;
            }
        }


        // Interface conversion successful

        // Infer interface type args, TBD: Merge with duplicate code above
        for (int i = 0; i < typeArgs.Length; i++)
        {
            if (inferTypeArgs[i].IsUnresolved && typeArgs[i].IsResolved)
                inferTypeArgs[i] = typeArgs[i];
            else if (inferTypeArgs[i].FullName != typeArgs[i].FullName)
                return CallCompatible.TypeArgsAmbiguousFromInterfaceParameter;
        }
        return CallCompatible.Compatible;
    }


}
