using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Zurfur.Lex;
using Zurfur.Jit;

namespace Zurfur.Compiler
{
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
        public UseSymbols Uses = new();
        public SymbolTable Table = new();
        public Dictionary<SyntaxScope, Symbol> SyntaxToSymbol = new();
    }

 
    static class CompileHeader
    {
        const string ZURFUR_PRELUDE = "void bool i8 byte i16 u16 i32 u32 int u64 f32 float str "
            + "Box Nil Maybe List Map Array Buffer Span require assert";
        
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
                    syntaxToSymbol[m] = s2!;
                    return s2!;
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
                    if (table.Root.TryGetPrimary("Zurfur", out var zSym) && zSym!.IsModule)
                        foreach (var name in ZURFUR_PRELUDE.Split(' '))
                            AddUseSymbolsFromModule(zSym, name, null, fileUseSymbols);

                    // Process use statements
                    foreach (var use in syntaxFile.Value.Using)
                    {
                        // Find the module
                        var module = FindTypeInPathOrReject(table, use.ModuleName);
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

            /// <summary>
            /// Returns the symbol at the given path in the package.
            /// Returns null and marks an error if not found.
            /// </summary>
            Symbol? FindTypeInPathOrReject(SymbolTable table, Token[] path)
            {
                var symbol = table.Root;
                foreach (var name in path)
                {
                    if (!symbol.TryGetPrimary(name, out var child))
                    {
                        Reject(name, "Module or type name not found");
                        return null;
                    }
                    symbol = child!;
                }
                return symbol;
            }

            void AddUseSymbolsFromModule(Symbol module, string name, Token? token, UseSymbolsFile useSymbolsFile)
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

                // Add functions with the given name
                symbols.AddRange(
                    module.ChildrenNamed(name).Where(child => child.IsFun && child.Token == name));

                // Add the type and its operators
                if (module.TryGetPrimary(name, out Symbol? typeSym) && typeSym != null)
                {
                    symbols.Add(typeSym);

                    if (typeSym.IsType)
                    {
                        // Add operators with a parameter of this type
                        foreach (var opName in CompileCode.OpNames.Values)
                            foreach (var child in module.ChildrenNamed(opName))
                                if (child.IsFun && child.FunParamTypes.Any(
                                        paramType => paramType.Concrete.FullName == typeSym.Concrete.FullName))
                                    symbols.Add(child);

                        // Add interfaces for this type
                        // NOTE: Don't need to add concrete methods here
                        //       because it's done in CompileCode.FindInType.
                        foreach (var child in module.Children)
                        {
                            // Add any method matching the interface
                            if (child.IsFun && child.IsMethod && !child.IsStatic 
                                    && child.FunParamTypes.Length != 0
                                    && child.FunParamTypes[0].IsInterface
                                    && child.FunParamTypes[0].Concrete.FullName == typeSym.FullName)
                                symbols.Add(child);
                        }

                    }

                }

                return symbols;
            }


            void AddTypes()
            {
                foreach (var syntaxFile in syntaxFiles.Values)
                {
                    foreach (var type in syntaxFile.Types)
                    {
                        // Add type
                        if (!syntaxToSymbol.TryGetValue(type.Parent!, out var parent))
                            continue; // Syntax errors
                        var newType = new Symbol(SymKind.Type, parent, type.Name);
                        newType.Comments = type.Comments;
                        newType.SetQualifiers(type.Qualifiers);
                        newType.Token.AddInfo(newType);
                        AddTypeParams(newType, type.TypeArgs);
                        table.AddOrReject(newType);
                            
                        Debug.Assert(!syntaxToSymbol.ContainsKey(type));
                        syntaxToSymbol[type] = newType;
                    }
                }
            }
            
            // Add a default constructor for each type, if it dosen't already exist
            void AddConstructors()
            {
                foreach (var type in table.LookupSymbols)
                {
                    if (type.Kind != SymKind.Type || type.IsInterface)
                        continue;

                    if (type.FullName == SymTypes.RawPointer
                            || type.FullName == SymTypes.Ref
                            || type.FullName == SymTypes.Pointer
                            || type.FullName == SymTypes.Nil)
                        continue;

                    // Skip if user wrote a default new function
                    var typeName = Resolver.GetTypeWithGenericParameters(table, type).FullName;
                    if (type.Parent!.ChildrenNamed("new")
                            .Where(f => f.FunParamTypes.Length == 1 && f.FunParamTypes[0].FullName == typeName)
                            .Count() != 0)
                        continue;

                    // Add constructor.  Same signature as the user would write.
                    // TBD: Simplify this, because it's identical to compiling:
                    //          "[static] fun Type.new() extern"
                    //      Plus, some of this code is repeated in other places
                    var constructor = new Symbol(SymKind.Fun, type.Parent, type.Token, "new");
                    constructor.Qualifiers |= SymQualifiers.My | SymQualifiers.Method | SymQualifiers.Extern;
                    var constructorType = Resolver.GetTypeWithGenericParameters(table, type);
                    //constructorType.Qualifiers |= SymQualifiers.My;
                    foreach (var genericParam in constructorType.TypeArgs)
                        table.AddOrReject(new Symbol(SymKind.TypeParam, constructor, type.Token, genericParam.SimpleName));
                    SetGenericParamSymbols(constructor);

                    constructor.Type = table.CreateTuple(new[] { 
                        table.CreateTuple(new[] { constructorType }), 
                        table.CreateTuple(new[] { constructorType }) });

                    table.AddOrReject(constructor);
                }
            }

            void SetGenericParamSymbols(Symbol s)
            {
                s.GenericParamSymbols = s.Children.Where(s => s.Kind == SymKind.TypeParam)
                    .OrderBy(s => s.GenericParamNum()).ToArray();
            }

            void AddTypeParams(Symbol type, SyntaxExpr ?typeArgs)
            {
                if (typeArgs == null || typeArgs.Count == 0)
                    return;
                var typeParamSymbols = new List<Symbol>();
                foreach (var expr in typeArgs)
                {
                    var typeParam = new Symbol(SymKind.TypeParam, type, expr.Token);
                    if (table.AddOrReject(typeParam))
                    {
                        expr.Token.AddInfo(typeParam);
                        typeParamSymbols.Add(typeParam);
                    }
                }
                type.GenericParamSymbols = typeParamSymbols.ToArray();
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
                        symField.Type = ResolveTypeNameOrReject(symField.Parent!, field.TypeName);
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
                        if (module.TryGetPrimary(type.Name, out var symbol) && symbol!.IsType)
                            ResolveConstraints(symbol, type.Constraints);
                    }
                }
            }

            void ResolveConstraints(Symbol scope, SyntaxConstraint[] synConstraints)
            {
                if (synConstraints == null || synConstraints.Length == 0)
                    return;

                // Map of type parameters to constraints
                var symCon = new Dictionary<string, Symbol[]>();
                foreach (var synConstraint in synConstraints)
                {
                    if (synConstraint == null || synConstraint.TypeName == null || synConstraint.TypeConstraints == null)
                        continue; // Syntax errors

                    // Find constraint type
                    var name = synConstraint.TypeName.Name;
                    var constrainedType = Resolver.FindTypeInScopeWalk(name, scope);
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
                    var constrainers = new List<Symbol>();
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
                        if (constrainers.Contains(sym))
                        {
                            Reject(c.Token, $"Duplicate constraint:  '{sym.FullName}'");
                            continue;
                        }
                        constrainers.Add(sym);
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
                if (!syntaxToSymbol.TryGetValue(synFunc.Parent!, out var scope))
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
                AddTypeParams(function, synFunc.TypeArgs);
                var myParam = ResolveMyParam(function, synFunc);
                var parameters = ResolveFunParams(synFunc.FunctionSignature[0], table, function, function, useSymbolsFile, false);
                var returns = ResolveFunParams(synFunc.FunctionSignature[1], table, function, function, useSymbolsFile, true);
                var newReturn = ResolveNewReturn(function, myParam, synFunc);

                if (myParam != null)
                    parameters.Insert(0, myParam);
                if (newReturn != null)
                    returns.Add(newReturn);

                function.Type = table.CreateTuple(new Symbol[] {
                    table.CreateTuple(parameters.Select(a => a.Type!).ToArray(), parameters.ToArray()),
                    table.CreateTuple(returns.Select(a => a.Type!).ToArray(), returns.ToArray()) });

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

            Symbol? ResolveMyParam(Symbol method, SyntaxFunc func)
            {
                var methodParent = method.Parent!;
                var extType = func.ExtensionType;
                if (methodParent.IsInterface)
                {
                    if (extType != null)
                        Reject(extType.Token, "Interface methods may not have a receiver type");
                    if (func.TypeArgs != null && func.TypeArgs.Count >= 1)
                        Reject(func.TypeArgs[0].Token, "Interface methods may not have type parameters");

                    // Interface method
                    bool isStatic = method.IsStatic || !methodParent.IsInterface;
                    var myParam = new Symbol(SymKind.FunParam, method, func.Name, isStatic ? "My" : "my");
                    myParam.Type = Resolver.GetTypeWithGenericParameters(table, methodParent);
                    if (myParam.Type == null)
                        return null;
                    method.Qualifiers |= SymQualifiers.My | SymQualifiers.Method;
                    myParam.Qualifiers |= SymQualifiers.My;
                    if (!noCompilerChecks)
                        myParam.Token.AddInfo(new VerifySuppressError());
                    return myParam;
                }

                if (extType != null)
                {
                    var myParam = new Symbol(SymKind.FunParam, method, func.Name, method.IsStatic ? "My" : "my");
                    if (extType != null && extType.Token == "mut")
                    {
                        myParam.SetQualifier("mut");
                        extType = extType[0];
                    }

                    if (extType.Token == ParseZurf.VT_TYPE_ARG)
                    {
                        // Generic parameters are supplied
                        myParam.Type = ResolveTypeNameOrReject(myParam, extType);
                        if (myParam.Type == null)
                            return null;
                    }
                    else
                    {
                        var recieverType = Resolver.FindGlobalType(extType.Token, table, method,
                                        useSymbols.Files[extType.Token.Path]);
                        if (recieverType == null)
                            return null;
                        if (recieverType.IsModule || recieverType.IsTypeParam)
                        {
                            Reject(extType.Token, $"Receiver type cannot be {recieverType.Kind}");
                            return null;
                        }

                        myParam.Type = Resolver.GetTypeWithGenericParameters(table, recieverType);
                        if (myParam.Type == null)
                            return null;

                        // If the type has generic parameters, the function must have at least as many
                        if (myParam.Type.GenericParamCount() > method.GenericParamCount())
                        {
                            Reject(method.Token, $"'{method.Token}' must have at least {myParam.Type.GenericParamCount()} "
                                + $"generic parameter(s) because '{extType.Token}' takes that many generic parameter(s)");
                            return null;
                        }
                    }
                    extType.Token.AddInfo(myParam.Type);
                    method.Qualifiers |= SymQualifiers.Method | SymQualifiers.My;
                    myParam.Qualifiers |= SymQualifiers.My;
                    if (!noCompilerChecks)
                        myParam.Token.AddInfo(new VerifySuppressError());
                    return myParam;
                }

                return null;
            }

            Symbol? ResolveNewReturn(Symbol function, Symbol? myParam, SyntaxFunc synFunc)
            {
                if (myParam == null || synFunc.Name != "new")
                    return null;

                foreach (var parameter in synFunc.FunctionSignature[1])
                    if (parameter[0].Token != "")
                        Reject(parameter[0].Token, "'new' function must not have return parameters");
                var returnParam = new Symbol(SymKind.FunParam, function, synFunc.Name, "");
                returnParam.Type = myParam.Type;
                function.Qualifiers &= ~SymQualifiers.Static;
                function.Qualifiers |= SymQualifiers.Method | SymQualifiers.My;
                return returnParam;
            }

            static List<Symbol> ResolveFunParams(
                SyntaxExpr parameters,
                SymbolTable table,
                Symbol function,
                Symbol searchScope,
                UseSymbolsFile useSymbols,
                bool isReturn)
            {
                var paramSyms = new List<Symbol>();
                if (parameters is SyntaxError)
                    return paramSyms;

                foreach (var expr in parameters)
                {
                    if (expr is SyntaxError)
                        continue;
                    Debug.Assert(expr.Count >= 3);

                    // This makes a single return parameter unnamed, "".
                    // All other parameters become named tuples.
                    var paramType = Resolver.Resolve(expr[0], table, false, searchScope, useSymbols);
                    if (paramType == null)
                        continue; // Unresolved symbol

                    if (!(paramType.IsAnyType || table.NoCompilerChecks))
                        Resolver.RejectTypeArgLeftDotRight(expr[0], table,
                            $"The symbol is not a type, it is a {paramType.KindName}");

                    // Create function parameter symbol
                    var funParam = new Symbol(SymKind.FunParam, function, expr.Token, expr.Token.Name);
                    expr.Token.AddInfo(funParam);
                    foreach (var qualifier in expr[2])
                        funParam.SetQualifier(qualifier.Token);

                    funParam.Type = paramType;
                    paramSyms.Add(funParam);
                }
                return paramSyms;
            }

            Symbol? ResolveTypeNameOrReject(Symbol scope, SyntaxExpr typeExpr)
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
