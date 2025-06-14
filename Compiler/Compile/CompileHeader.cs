﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using Gosub.Lex;
using Zurfur.Vm;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Zurfur.Compiler;

class UseSymbolsFile
{
    public Dictionary<string, List<Symbol>> UseSymbols = new();
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
    public Dictionary<string, UseSymbolsFile> Files = new();
}

class CompilerHeaderOutput
{
    public UseSymbols Uses = new();
    public SymbolTable Table = new();
    public Dictionary<SyntaxScope, Symbol> SyntaxToSymbol = new();
}


static class CompileHeader
{
    const string ZURFUR_PRELUDE = "Void Bool I8 Byte I16 U16 I32 U32 Int U64 F32 Float Str "
        + "Box Nil Maybe List Map Array Buffer Span assert _for";
    
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
                    var symbol = AddModule(ns.Value, syntaxFile.Key);
                    symbol.Comments += " " + ns.Value.Comments;
                }
            }
        }

        Symbol AddModule(SyntaxScope m, string path)
        {
            if (syntaxToSymbol.TryGetValue(m, out var s1))
                return s1;
            Symbol parent = table.Root;
            if (m.Parent != null)
                parent = AddModule(m.Parent, path);
            if (parent.TryGetPrimary(m.Name, out var s2))
            {
                syntaxToSymbol[m] = s2!;
                return s2!;
            }
            var newModule = new Symbol(SymKind.Module, parent, path, m.Name);

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
                        token.Type = TokenType.TypeName;
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
                    var newType = new Symbol(SymKind.Type, parent, syntaxFile.Lexer.Path, type.Name);
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
                var constructor = new Symbol(SymKind.Fun, type.Parent, type.Path, type.Token, "new");
                constructor.Qualifiers |= SymQualifiers.Method | SymQualifiers.Extern;
                var constructorType = Resolver.GetTypeWithGenericParameters(table, type);
                //constructorType.Qualifiers |= SymQualifiers.My;
                foreach (var genericParam in constructorType.TypeArgs)
                    table.AddOrReject(new Symbol(SymKind.TypeParam, constructor, type.Path, type.Token, genericParam.SimpleName));

                SetGenericParamSymbols(constructor);

                constructor.Type = table.CreateTuple([ 
                    table.CreateTuple([constructorType]), table.CreateTuple([constructorType]) ]);

                table.AddOrReject(constructor);
            }
        }

        void SetGenericParamSymbols(Symbol s)
        {
            s.GenericParamSymbols = s.Children.Where(s => s.Kind == SymKind.TypeParam)
                .OrderBy(s => s.GenericParamNum()).ToArray();
        }

        // Add generic type arguments to type.GenericParamSymbols (and set the symbol's parent to `type`)
        void AddTypeParams(Symbol type, SyntaxExpr ?typeArgs)
        {
            if (typeArgs == null || typeArgs.Count == 0)
                return;

            var typeParamSymbols = new List<Symbol>(type.GenericParamSymbols);
            foreach (var expr in typeArgs)
            {
                var typeParam = new Symbol(SymKind.TypeParam, type, type.Path, expr.Token);
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
                    var symField = new Symbol(SymKind.Field, symParent, syntaxFile.Key, field.Name);
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
                    symField.Type = ResolveTypeNameOrReject(symField.Parent!, field.TypeName, syntaxFile.Key);
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
                        ResolveConstraints(symbol, type.Constraints, syntaxFile.Key);
                }
            }
        }

        void ResolveConstraints(Symbol scope, SyntaxConstraint[] synConstraints, string path)
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
                    var sym = ResolveTypeNameOrReject(scope, c, path);
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
                    ResolveFunction(synFunc, syntaxFile.Key);
        }

        // Scope is where the function is defined (a module or type)
        void ResolveFunction(SyntaxFunc synFunc, string path)
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

            // Generate the function
            var useSymbolsFile = useSymbols.Files[path];
            var function = new Symbol(SymKind.Fun, scope, path, synFunc.Name);
            function.SetQualifiers(synFunc.Qualifiers);
            function.Comments = synFunc.Comments;

            AddTypeParams(function, synFunc.TypeParams);
            if ( (function.Parent?.IsInterface??false) && synFunc.TypeParams != null && synFunc.TypeParams.Count >= 1)
                Reject(synFunc.TypeParams[0].Token, "Interface methods may not have type parameters");

            var receiverParam = ResolveReceiver(synFunc, table, useSymbolsFile, function);
            var parametersEnumerator = receiverParam == null ? synFunc.FunctionSignature[0] : synFunc.FunctionSignature[0].Skip(1);
            var parameters = ResolveFunParams(parametersEnumerator, table, function, function, useSymbolsFile);
            var returns = ResolveFunParams(synFunc.FunctionSignature[1], table, function, function, useSymbolsFile);
            var newReturn = ResolveNewReturn(function, receiverParam, synFunc);


            // Insert receiver and return params
            if (receiverParam != null)
                parameters.Insert(0, receiverParam);
            if (newReturn != null)
                returns.Add(newReturn);

            function.Type = table.CreateTuple([
                table.CreateTuple(parameters.Select(a => a.Type!).ToArray(), parameters.ToArray()),
                table.CreateTuple(returns.Select(a => a.Type!).ToArray(), returns.ToArray()) ]);

            function.Token.AddInfo(function);

            if (synFunc.Parent?.Name.Error ?? false)
            {
                // NOTE: Since the symbol is not stored, the function will not be compiled.
                // TBD: Consider changing this so user can get feedback on errors.
                Warning(synFunc.Token, $"Function not processed because '{synFunc.Parent.Name}' has an error");
                return;
            }
            table.AddOrReject(function);

            ResolveConstraints(function, synFunc.Constraints, path);

            Debug.Assert(!syntaxToSymbol.ContainsKey(synFunc));
            syntaxToSymbol[synFunc] = function;
        }

        Symbol? ResolveReceiver(SyntaxFunc synFunc, SymbolTable table, UseSymbolsFile useSymbolsFile, Symbol function)
        {
            var functionParent = function.Parent;
            ArgumentNullException.ThrowIfNull(functionParent, "function.Parent cannot be null");

            // Functions in types may not include an explicit receiver type
            if (!functionParent.IsModule && synFunc.ReceiverTypeName != null)
            {
                Reject(synFunc.ReceiverTypeName, "Functions declared in a type may not have an explicit receiver type");
                return null;
            }

            // Resolve receiver type name (if supplied)
            Symbol? receiverType = null;
            if (synFunc.ReceiverTypeName != null)
            {
                receiverType = Resolver.FindGlobalType(synFunc.ReceiverTypeName, table, function, useSymbolsFile);
                if (receiverType == null)
                    return null; // Error marked above

                synFunc.ReceiverTypeName.AddInfo(receiverType);
                if (!receiverType.IsType && !receiverType.IsTypeParam)
                {
                    table.Reject(synFunc.ReceiverTypeName, $"The symbol is not a type, it is a {receiverType.KindName}");
                    return null;
                }
            }

            // Static function - requires a receiver parameter
            if (function.IsStatic)
            {
                function.Qualifiers |= SymQualifiers.Static | SymQualifiers.Method;

                // Static functions in types don't need anything more
                if (!functionParent.IsModule)
                    return null;

                if (receiverType == null)
                {
                    Reject(synFunc.Name, "Module level static functions must specify a receiver type");
                    return null;
                }

                function.StaticScope = receiverType;
                return null;
            }

            // Add receiver parameter
            if (functionParent.IsModule)
            {
                if (synFunc.ReceiverTypeName == null || receiverType == null)
                    return null; // No receiver type specified

                return ResolveMyParam(synFunc, receiverType, table, useSymbolsFile, function);
            }
            else
            {
                return ResolveMyParam(synFunc, functionParent, table, useSymbolsFile, function);
            }
        }

        // Resolve an explicit "my" param, which is allowed to omit the type name
        Symbol? ResolveMyParam(SyntaxFunc synFunc, Symbol receiverType, SymbolTable table, UseSymbolsFile useSymbolsFile, Symbol function)
        {
            // Parameter syntax tree: variable name[type, initializer, qualifiers]
            if (synFunc.FunctionSignature.Count == 0
                || synFunc.FunctionSignature[0].Count == 0
                || synFunc.FunctionSignature[0][0].Count != 3)
            {
                Reject(synFunc.Name, "Expecting an explicit receiver parameter");
                return null;
            }

            // Resolve receiver parameter
            var parameter = synFunc.FunctionSignature[0][0];
            Symbol myParam;
            if (parameter[0].Token == "")
            {
                if (receiverType.IsTypeParam)
                {
                    Reject(parameter.Token, "Generic receiver type must use explicit type");
                    return null;
                }
                // Receiver parameter type not supplied, generate one from the receiver type
                myParam = new Symbol(SymKind.FunParam, function, function.Path, parameter.Token);
                myParam.Type = Resolver.GetTypeWithGenericParameters(table, receiverType);

                // Reject on unused generic parameters, eg: List.myFunc(my)
                if (receiverType.GenericParamCount() != 0 && function.GenericParamCount() == 0)
                    table.Reject(synFunc.Name, "Function must include type parameters when using implicit parameter type");

                // TBD: Verify function has same number of generic type parameters as receiver type
                //if (synFunc.TypeParams != null && synFunc.TypeParams.Count != receiverType.GenericParamCount())
                //    Reject(synFunc.ReceiverTypeName, $"Expecting {receiverType.GenericParamCount()} type parameters (or none)");
            }
            else
            {
                // Parameter parameter type is supplied, use it instead of the given receiver type
                var manualReceiverType = ResolveFunParam(parameter, table, function, function, useSymbolsFile);
                if (manualReceiverType == null || manualReceiverType.Type == null)
                    return null;
                myParam = manualReceiverType;

                // When receiver parameter type is supplied, it must exactly match the receiver type (or be a type parameter)
                if (myParam.Type.Concrete.FullName != receiverType.Concrete.FullName && !receiverType.IsTypeParam)
                    Resolver.RejectTypeArgLeftDotRight(parameter[0], table, $"Expecting receiver type '{synFunc.ReceiverTypeName}'");
                else if (receiverType.Concrete.GenericParamCount() == 0 && !receiverType.IsTypeParam)
                    Resolver.RejectTypeArgLeftDotRight(parameter[0], table, $"No need to supply the receiver parameter type");
                // else if (...)
                //     TBD: Reject on exact matching generic parameters, e.g: fun List<T>.myFunc(my List<T>)

            }
            function.Qualifiers |= SymQualifiers.Method;
            parameter.Token.AddInfo(myParam);
            parameter.Token.Type = TokenType.ReservedVar;

            return myParam;
        }


        Symbol? ResolveNewReturn(Symbol function, Symbol? myParam, SyntaxFunc synFunc)
        {
            if (myParam == null || synFunc.Name != "new")
                return null;

            foreach (var parameter in synFunc.FunctionSignature[1])
                if (parameter[0].Token != "")
                    Reject(parameter[0].Token, "'new' function must not have return parameters");
            var returnParam = new Symbol(SymKind.FunParam, function, function.Path, synFunc.Name, "");
            returnParam.Type = myParam.Type;
            function.Qualifiers &= ~SymQualifiers.Static;
            function.Qualifiers |= SymQualifiers.Method;
            return returnParam;
        }

        static List<Symbol> ResolveFunParams(
            IEnumerable<SyntaxExpr> parameters,
            SymbolTable table,
            Symbol function,
            Symbol searchScope,
            UseSymbolsFile useSymbols)
        {
            var paramSyms = new List<Symbol>();
            if (parameters is SyntaxError)
                return paramSyms;

            foreach (var expr in parameters)
            {
                var symbol = ResolveFunParam(expr, table, function, searchScope, useSymbols);
                if (symbol != null)
                    paramSyms.Add(symbol);
            }
            return paramSyms;
        }

        // Syntax tree:  varable name[type, initializer, qualifiers]
        static Symbol? ResolveFunParam(
            SyntaxExpr? expr,
            SymbolTable table, 
            Symbol function, 
            Symbol searchScope, 
            UseSymbolsFile useSymbols)
        {
            if (expr == null || expr is SyntaxError)
                return null;
            Debug.Assert(expr.Count >= 3);

            if (expr[0].Token == "")
            {
                table.Reject(expr.Token, "Expecting a type name");
                return null;
            }

            // Resolve parameter type
            var paramType = Resolver.Resolve(expr[0], table, false, searchScope, useSymbols);
            if (paramType == null)
                return null; // Unresolved symbol

            if (!(paramType.IsAnyType || table.NoCompilerChecks))
                Resolver.RejectTypeArgLeftDotRight(expr[0], table, $"The symbol is not a type, it is a {paramType.KindName}");

            // Create function parameter symbol
            // NOTE: Un-named return parameters have an empty symbol name
            var funParam = new Symbol(SymKind.FunParam, function, function.Path, expr.Token, expr.Token.Name);

            // Return parametrs without names
            expr.Token.AddInfo(funParam);
            foreach (var qualifier in expr[2])
                funParam.SetQualifier(qualifier.Token);

            funParam.Type = paramType;
            return funParam;
        }


        Symbol? ResolveTypeNameOrReject(Symbol scope, SyntaxExpr typeExpr, string path)
        {
            // There will also be a syntax error
            if (typeExpr == null || typeExpr.Token.Name == "")
                return null;

            var symbol = Resolver.Resolve(typeExpr, table, false, scope, useSymbols.Files[path]);
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
