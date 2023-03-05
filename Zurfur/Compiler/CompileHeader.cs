﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.Linq;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    public class ZilCompileError : TokenError
    {
        public ZilCompileError(string message) : base(message) { }
    }
    public class ZilWarn : TokenWarn
    {
        public ZilWarn(string message) : base(message) { }
    }

    class UseSymbolsFile
    {
        public Dictionary<string, List<Symbol>> UseSymbols = new Dictionary<string, List<Symbol>>();
        public void AddSymbol(string name, Symbol sym)
        {
            if (!UseSymbols.TryGetValue(name, out var symbols))
            {
                symbols = new List<Symbol>();
                UseSymbols[name] = symbols;
            }
            if (!symbols.Contains(sym))
                symbols.Add(sym);
        }

    }

    class UseSymbols
    {
        public Dictionary<string, UseSymbolsFile> Files = new Dictionary<string, UseSymbolsFile>();
    }

    class CompilerHeaderOutput
    {
        public UseSymbols Uses;
        public SymbolTable Table;
        public Dictionary<SyntaxScope, Symbol> SyntaxToSymbol;
    }

 
    static class CompileHeader
    {
        const string ZURFUR_PRELUDE = "void nil Nilable bool i8 byte i16 u16 i32 u32 int u64 f32 f64 str List Map Array Buffer Span";

        static public CompilerHeaderOutput GenerateHeader(
            Dictionary<string, SyntaxFile> syntaxFiles,
            bool noCompilerChecks)
        {
            var table = new SymbolTable();
            var syntaxToSymbol = new Dictionary<SyntaxScope, Symbol>();
            table.NoCompilerChecks = noCompilerChecks; // TBD: Move to compiler options class

            // Find a symbol for the type or module syntax
            AddModules();
            table.GenerateLookup();
            AddTypes();
            table.GenerateLookup();
            var useSymbols = ProcessUseStatements(false);
            ResolveFields();
            ResolveFunctions();
            ResolveTypeConstraints();
            AddConstructors();
            table.GenerateLookup();

            // Re-process use statements to retrieve functions
            return new CompilerHeaderOutput { Uses = ProcessUseStatements(true), Table = table, SyntaxToSymbol = syntaxToSymbol};

            void AddModules()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var ns in syntaxFile.Value.Modules)
                    {
                        var symbol = AddModule(ns.Value);
                        symbol.Comments += " " + ns.Value.Comments;
                    }
                }
            }

            Symbol AddModule(SyntaxScope m)
            {
                if (syntaxToSymbol.TryGetValue(m, out var s1))
                    return s1;
                Symbol parent = table.Root;
                if (m.Parent != null)
                    parent = AddModule(m.Parent);
                if (parent.TryGetPrimary(m.Name, out var s2))
                {
                    syntaxToSymbol[m] = s2;
                    return s2;
                }
                var newModule = new Symbol(SymKind.Module, parent, m.Name);
                // TBD: Take qualifiers from module definition (generate error if inconsistent)
                newModule.Qualifiers = SymQualifiers.Pub;
                m.Name.AddInfo(newModule);
                var ok = table.AddOrReject(newModule);
                Debug.Assert(ok);
                syntaxToSymbol[m] = newModule;
                return newModule;
            }

            UseSymbols ProcessUseStatements(bool addSymbolInfo)
            {
                var uses = new UseSymbols();
                foreach (var syntaxFile in syntaxFiles)
                {
                    var fileUseSymbols = new UseSymbolsFile();

                    // Add prelude to all files
                    if (table.Root.TryGetPrimary("Zurfur", out var zSym) && zSym.IsModule)
                        foreach (var name in ZURFUR_PRELUDE.Split(' '))
                            AddUseSymbolsFromModule(zSym, name, null, fileUseSymbols);

                    // Process use statements
                    foreach (var use in syntaxFile.Value.Using)
                    {
                        // Find the module
                        var module = table.FindTypeInPathOrReject(use.ModuleName);
                        if (module == null || use.ModuleName.Length == 0)
                            continue;  // Error marked by FindPathOrReject
                        var lastToken = use.ModuleName[use.ModuleName.Length - 1];
                        if (!module.IsModule)
                        {
                            Reject(lastToken, "Must be a module, not a " + module.KindName);
                            continue;
                        }
                        if (use.Symbols.Length == 0)
                        {
                            // Add just the module
                            fileUseSymbols.AddSymbol(lastToken, module);
                            continue;
                        }
                        // Add list of symbols from the module
                        foreach (var token in use.Symbols)
                        {
                            AddUseSymbolsFromModule(module, token.Name, addSymbolInfo ? token : null, fileUseSymbols);
                        }
                    }
                    uses.Files[syntaxFile.Key] = fileUseSymbols;
                }
                return uses;
            }

            void AddUseSymbolsFromModule(Symbol module, string name, Token token, UseSymbolsFile useSymbolsFile)
            {
                var symbols = GetUseSymbolsFromModule(module, name);
                if (symbols.Count == 0)
                {
                    if (token != null)
                        Reject(token, $"Symbol not found in '{module}'");
                    return;
                }
                foreach (var symbol in symbols)
                {
                    useSymbolsFile.AddSymbol(symbol.SimpleName, symbol);
                    if (token != null)
                    {
                        if (symbol.IsType || symbol.IsModule)
                            token.Type = eTokenType.TypeName;
                        token.AddInfo(symbol);
                    }

                }
            }

            // Get all the symbols with a given name (include operators if it's a type)
            List<Symbol> GetUseSymbolsFromModule(Symbol module, string name)
            {
                var symbols = new List<Symbol>();
                if (module.TryGetPrimary(name, out Symbol typeSym))
                    symbols.Add(typeSym);
                
                if (module.HasFunNamed(name))
                    foreach (var child in module.Children)
                        if (child.IsFun && child.Token == name)
                            symbols.Add(child);

                return symbols;
            }


            void AddTypes()
            {
                foreach (var syntaxFile in syntaxFiles.Values)
                {
                    foreach (var type in syntaxFile.Types)
                    {
                        // Add type
                        if (!syntaxToSymbol.TryGetValue(type.Parent, out var parent))
                            continue; // Syntax errors
                        var newType = new Symbol(SymKind.Type, parent, type.Name);
                        newType.Comments = type.Comments;
                        newType.SetQualifiers(type.Qualifiers);
                        newType.Token.AddInfo(newType);
                        if (table.AddOrReject(newType))
                            AddTypeParams(newType, type.TypeArgs);
                        Debug.Assert(!syntaxToSymbol.ContainsKey(type));
                        syntaxToSymbol[type] = newType;
                    }
                }
            }

            // Add a default constructor for each type, if it doesm't already exist
            void AddConstructors()
            {
                foreach (var newType in table.LookupSymbols)
                {
                    if (newType.Kind != SymKind.Type)
                        continue;

                    // Add constructor.  Same signature as the user would write.
                    // TBD: Simplify this, because it's identical to compiling:
                    //          "fun Type.new() extern"
                    //      Plus, some of this code is repeated in other places
                    var module = newType.Parent;
                    var constructor = new Symbol(SymKind.Fun, module, newType.Token, "new");
                    constructor.Qualifiers |= SymQualifiers.Static | SymQualifiers.Method | SymQualifiers.Extern;
                    var myParam = new Symbol(SymKind.FunParam, constructor, newType.Token, "my");
                    myParam.Type = Resolver.GetTypeWithGenericParameters(table, newType);
                    foreach (var genericParam in myParam.Type.TypeArgs)
                        table.AddOrReject(new Symbol(SymKind.TypeParam, constructor, newType.Token, genericParam.SimpleName));
                    table.AddOrReject(myParam);
                    var returnParam = new Symbol(SymKind.FunParam, constructor, newType.Token, "");
                    returnParam.Qualifiers |= SymQualifiers.ParamOut;
                    returnParam.Type = myParam.Type;
                    table.AddOrReject(returnParam);
                    CreateFunTypeAndName(table, constructor);
                    table.AddOrIgnore(constructor);
                }
            }

            void AddTypeParams(Symbol scope, IEnumerable<SyntaxExpr> typeArgs)
            {
                if (typeArgs == null)
                    return;
                foreach (var expr in typeArgs)
                {
                    var typeParam = new Symbol(SymKind.TypeParam, scope, expr.Token);
                    if (table.AddOrReject(typeParam))
                        expr.Token.AddInfo(typeParam);
                }
            }

            void ResolveFields()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var field in syntaxFile.Value.Fields)
                    {
                        if (!syntaxToSymbol.TryGetValue(field.Parent, out var symParent))
                        {
                            Reject(field.Name, $"Symbol not processed because the parent scope has an error");
                            continue;
                        }

                        // Create the field
                        var symField = new Symbol(SymKind.Field, symParent, field.Name);
                        symField.SetQualifiers(field.Qualifiers);
                        symField.Comments = field.Comments;
                        symField.Token.AddInfo(symField);
                        table.AddOrReject(symField);

                        if (symParent.Parent != null && symParent.IsEnum)
                        {
                            // Enum feilds have their parent enum type
                            symField.Type = symField.Parent;
                            continue;
                        }
                        // Skip errors, user probably typing
                        if (field.TypeName == null || field.TypeName.Token.Name == "")
                        {
                            Reject(field.Name, "Expecting symbol to have an explicitly named type");
                            continue;
                        }
                        symField.Type = ResolveTypeNameOrReject(symField.Parent, field.TypeName);
                        if (symField.TypeName == "" && !noCompilerChecks)
                            symField.Token.AddInfo(new VerifySuppressError());
                    }
                }
            }

            void ResolveTypeConstraints()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var type in syntaxFile.Value.Types)
                    {
                        if (!syntaxToSymbol.TryGetValue(type.Parent, out var module))
                            continue;  // Syntax error already marked
                        if (module.TryGetPrimary(type.Name, out var symbol) && symbol.IsType)
                            ResolveConstraints(symbol, type.Constraints);
                    }
                }
            }

            void ResolveConstraints(Symbol scope, SyntaxConstraint[] synConstraints)
            {
                if (synConstraints == null || synConstraints.Length == 0)
                    return;

                // Map of type parameters to constraints
                var symCon = new Dictionary<string, string[]>();
                foreach (var synConstraint in synConstraints)
                {
                    if (synConstraint == null || synConstraint.TypeName == null || synConstraint.TypeConstraints == null)
                        continue; // Syntax errors
                    var name = synConstraint.TypeName.Name;

                    // Find constraint type
                    Symbol constrainedType;
                    if (synConstraint.MyToken != null)
                    {
                        var myType = Resolver.ResolveMy(table, synConstraint.MyToken, scope);
                        if (myType == null)
                            continue;
                        myType.Concrete.TryGetPrimary(name, out constrainedType);
                    }
                    else
                    {
                        constrainedType = Resolver.FindTypeInScopeWalk(name, scope);
                    }

                    if (constrainedType == null)
                    {
                        Reject(synConstraint.TypeName, $"The symbol '{name}' is undefined");
                        continue;
                    }
                    if (!constrainedType.IsTypeParam)
                    {
                        Reject(synConstraint.TypeName, $"The symbol '{name}' is not a type parameter, it is a {constrainedType.KindName}");
                        continue;
                    }

                    var argName = $"#{constrainedType.GenericParamNum()}";
                    synConstraint.TypeName.AddInfo(constrainedType);

                    if (symCon.ContainsKey(name))
                    {
                        Reject(synConstraint.TypeName, $"Constraints for this type parameter were already defined.  Use '+' to add more");
                        continue;
                    }
                    var constrainers = new List<string>();
                    foreach (var c in synConstraint.TypeConstraints)
                    {
                        var sym = ResolveTypeNameOrReject(scope, c);
                        if (sym == null)
                            continue;  // Error already given
                        if (!sym.IsInterface)
                        {
                            // TBD: This should be in verification.
                            Resolver.RejectTypeArgLeftDotRight(c, table, $"Symbol is not an interface, it is a {sym.KindName}");
                            continue;
                        }
                        if (constrainers.Contains(sym.FullName))
                        {
                            Reject(c.Token, $"Duplicate constraint:  '{sym.FullName}'");
                            continue;
                        }
                        constrainers.Add(sym.FullName);
                    }
                    if (constrainers.Count != 0)
                        symCon[argName] = constrainers.ToArray();
                }
                scope.Constraints = symCon;
            }

            void ResolveFunctions()
            {
                foreach (var syntaxFile in syntaxFiles)
                    foreach (var synFunc in syntaxFile.Value.Functions)
                        ResolveFunction(synFunc);
            }

            // Scope is where the function is defined (a module or type)
            void ResolveFunction(SyntaxFunc synFunc)
            {
                // Get module containing function
                if (!syntaxToSymbol.TryGetValue(synFunc.Parent, out var scope))
                    return; // Syntax errors

                Debug.Assert(scope.IsModule || scope.IsType);
                if (synFunc.FunctionSignature.Count != 3)
                {
                    Reject(synFunc.Name, "Syntax error or compiler error");
                    return;
                }
                var useSymbolsFile = useSymbols.Files[synFunc.Token.Path];

                // Generate the function
                var function = new Symbol(SymKind.Fun, scope, synFunc.Name);
                function.SetQualifiers(synFunc.Qualifiers);
                function.Comments = synFunc.Comments;
                var myParam = ResolveMyParam(function, synFunc);
                AddTypeParams(function, synFunc.TypeArgs);
                table.AddOrReject(myParam);
                Resolver.ResolveFunParams(synFunc.FunctionSignature[0], table, function, function, useSymbolsFile, false);
                Resolver.ResolveFunParams(synFunc.FunctionSignature[1], table, function, function, useSymbolsFile, true);
                SetNewFunction(function, synFunc, myParam);
                CreateFunTypeAndName(table, function);

                function.Token.AddInfo(function);
                if (synFunc.Parent.Name.Error)
                {
                    // NOTE: Since the symbol is not stored, the function will not be compiled.
                    // TBD: Consider changing this so user can get feedback on errors.
                    Warning(synFunc.Token, $"Function not processed because '{synFunc.Parent.Name}' has an error");
                    return;
                }

                table.AddOrReject(function);

                ResolveConstraints(function, synFunc.Constraints);

                Debug.Assert(!syntaxToSymbol.ContainsKey(synFunc));
                syntaxToSymbol[synFunc] = function;

            }

            // Resolve `my` parameter.  Add the implicit generic types, if any.
            Symbol ResolveMyParam(Symbol method, SyntaxFunc func)
            {
                var extType = func.ExtensionType;
                bool isStatic = method.IsStatic || extType == null || extType.Token == "";
                var myParam = new Symbol(SymKind.FunParam, method, func.Name, isStatic ? "My" : "my");
                if (extType != null && extType.Token == "mut")
                {
                    myParam.SetQualifier("mut");
                    extType = extType[0];
                }

                if (method.Parent.IsInterface)
                {
                    // Interface method
                    myParam.Type = Resolver.GetTypeWithGenericParameters(table, method.Parent);
                    method.Qualifiers |= SymQualifiers.Method;
                    if (func.ExtensionType != null)
                        Reject(func.ExtensionType.Token, "Extension method not allowed on interface functions");
                }
                else if (extType == null || extType.Token == "")
                {
                    // Static global
                    myParam.Type = method.Parent;
                    if (method.Qualifiers.HasFlag(SymQualifiers.Static))
                        Reject(method.Token, "'static' not allowed at module level");
                    method.Qualifiers |= SymQualifiers.Static;
                }
                else
                {
                    // Extension method
                    if (extType.Token != ParseZurf.VT_TYPE_ARG)
                    {
                        // Generic parameters not supplied, add them if necessary
                        var myType = Resolver.FindGlobalType(extType.Token, table, method,
                                        useSymbols.Files[extType.Token.Path]);
                        if (myType != null)
                        {
                            myParam.Type = Resolver.GetTypeWithGenericParameters(table, myType);
                            foreach (var genericParam in myParam.Type.TypeArgs)
                                table.AddOrReject(new Symbol(SymKind.TypeParam, method, func.Name, genericParam.SimpleName));
                            extType.Token.AddInfo(myParam.Type);
                        }
                    }
                    else
                    {
                        // Generic parameters are supplied
                        myParam.Type = ResolveTypeNameOrReject(myParam, extType);
                    }
                    if (myParam.Type != null)
                        method.Qualifiers |= SymQualifiers.Method;
                }

                if (myParam.TypeName == "" && !noCompilerChecks)
                    myParam.Token.AddInfo(new VerifySuppressError());
                return myParam;
            }

            // Give `new` function correct return type
            void SetNewFunction(Symbol function, SyntaxFunc synFunc, Symbol myParam)
            {
                if (synFunc.Name != "new")
                    return;

                function.Qualifiers |= SymQualifiers.Static;
                foreach (var parameter in synFunc.FunctionSignature[1])
                    if (parameter[0].Token != "")
                        Reject(parameter[0].Token, "'new' function must not have return parameters");
                if (myParam == null)
                {
                    Reject(function.Token, "'new' must be an extension method");
                    return;
                }
                var returnParam = new Symbol(SymKind.FunParam, function, synFunc.Name, "");
                returnParam.Qualifiers |= SymQualifiers.ParamOut;
                returnParam.Type = myParam.Type;
                table.AddOrReject(returnParam);
            }

            // Create function type from parameters/returns and finalize function name
            void CreateFunTypeAndName(SymbolTable table, Symbol function)
            {
                var parameters = GetFunParamsTuple(function, false);
                var returns = GetFunParamsTuple(function, true);
                function.Type = table.CreateTuple(new Symbol[] { parameters, returns });
                function.FinalizeFullName();
            }

            // Get parameters or returns as a tuple.
            Symbol GetFunParamsTuple(Symbol symbol, bool returns)
            {
                var parameters = symbol.ChildrenFilter(SymKind.FunParam)
                    .Where(child => child.Type != null && returns == child.ParamOut).ToList();

                parameters.Sort((a, b) => a.Order.CompareTo(b.Order));
                var paramTypes = parameters.Select(p => p.Type).ToArray();
                var paramNames = parameters.Select(p => p.FullName).ToArray();

                return table.CreateTuple(paramTypes, paramNames);
            }

            Symbol ResolveTypeNameOrReject(Symbol scope, SyntaxExpr typeExpr)
            {
                // There will also be a syntax error
                if (typeExpr == null || typeExpr.Token.Name == "")
                    return null;
                var symbol = Resolver.Resolve(typeExpr, table, false, scope, useSymbols.Files[typeExpr.Token.Path]);
                if (symbol == null)
                    return null;

                if (symbol.IsAnyType  || table.NoCompilerChecks)
                    return symbol;

                Resolver.RejectTypeArgLeftDotRight(typeExpr, table, "The symbol is not a type, it is a " + symbol.KindName);
                return null;
            }

            void Reject(Token token, string message)
            {
                table.Reject(token, message);
            }
        }


        // Does not add a warning if there is already an error there
        static void Warning(Token token, string message)
        {
            if (!token.Error)
                token.AddWarning(new ZilWarn(message));
        }

    }
}
