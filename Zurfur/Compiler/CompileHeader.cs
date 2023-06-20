using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        const string ZURFUR_PRELUDE = "Void Bool I8 Byte I16 U16 I32 U32 Int U64 F32 F64 Str "
            + "Nil Maybe List Map Array Buffer Span";

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
                        AddTypeParams(newType, type.TypeArgs);
                        SetGenericParamNames(newType);
                        table.AddOrReject(newType);
                        newType.FinalizeFullName();
                            
                        Debug.Assert(!syntaxToSymbol.ContainsKey(type));
                        syntaxToSymbol[type] = newType;
                    }
                }
            }
            
            void SetGenericParamNames(Symbol s)
            {
                // Set generic parameter names
                s.GenericParamNames = s.ChildrenFilter(SymKind.TypeParam)
                    .OrderBy(s => s.Order).Select(s => s.SimpleName).ToArray();
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
                    //          "[static] fun Type.new() extern"
                    //      Plus, some of this code is repeated in other places
                    var module = newType.Parent;
                    var constructor = new Symbol(SymKind.Fun, module, newType.Token, "new");
                    constructor.Qualifiers |= SymQualifiers.Static | SymQualifiers.Method | SymQualifiers.Extern;
                    constructor.ReceiverType = Resolver.GetTypeWithGenericParameters(table, newType);
                    foreach (var genericParam in constructor.ReceiverType.TypeArgs)
                        table.AddOrReject(new Symbol(SymKind.TypeParam, constructor, newType.Token, genericParam.SimpleName));
                    SetGenericParamNames(constructor);
                    var returnParam = new Symbol(SymKind.FunParam, constructor, newType.Token, "");
                    returnParam.Qualifiers |= SymQualifiers.ParamOut;
                    returnParam.Type = constructor.ReceiverType;
                    table.AddOrReject(returnParam);
                    Resolver.CreateFunTypeAndName(table, constructor);
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
                AddTypeParams(function, synFunc.TypeArgs);
                SetGenericParamNames(function);
                ResolveReciever(function, synFunc);
                Resolver.ResolveFunParams(synFunc.FunctionSignature[0], table, function, function, useSymbolsFile, false);
                Resolver.ResolveFunParams(synFunc.FunctionSignature[1], table, function, function, useSymbolsFile, true);
                Resolver.CreateFunTypeAndName(table, function);

                function.Token.AddInfo(function);
                if (synFunc.Parent.Name.Error)
                {
                    // NOTE: Since the symbol is not stored, the function will not be compiled.
                    // TBD: Consider changing this so user can get feedback on errors.
                    Warning(synFunc.Token, $"Function not processed because '{synFunc.Parent.Name}' has an error");
                    return;
                }
                table.AddOrReject(function);
                function.FinalizeFullName();

                ResolveConstraints(function, synFunc.Constraints);

                Debug.Assert(!syntaxToSymbol.ContainsKey(synFunc));
                syntaxToSymbol[synFunc] = function;

            }


            void ResolveReciever(Symbol method, SyntaxFunc func)
            {
                var extType = func.ExtensionType;
                
                if (method.Parent.IsInterface)
                {
                    // Interface method
                    method.Qualifiers |= SymQualifiers.Method;
                    if (func.ExtensionType != null)
                        Reject(func.ExtensionType.Token, "Interface methods cannot have a receiver type");
                    if (func.TypeArgs != null && func.TypeArgs.Count >= 1)
                        Reject(func.TypeArgs[0].Token, "Interface methods cannot have type parameters");
                    if (!method.IsStatic)
                    {
                        var myParamIntf = GetMutableMyParam(method, func, ref extType);
                        myParamIntf.Type = Resolver.GetTypeWithGenericParameters(table, method.Parent);
                        table.AddOrReject(myParamIntf);
                    }
                    return;
                }

                if (extType == null || extType.Token == "")
                {
                    // Static global, excluding interface
                    if (method.Qualifiers.HasFlag(SymQualifiers.Static))
                        Reject(method.Token, "'static' not allowed at module level");
                    method.Qualifiers |= SymQualifiers.Static;
                    return;
                }

                // Extension method
                var myParam = GetMutableMyParam(method, func, ref extType);
                if (extType.Token != ParseZurf.VT_TYPE_ARG)
                {
                    // Just type name without generic parameters
                    var recieverType = Resolver.FindGlobalType(extType.Token, table, method,
                                    useSymbols.Files[extType.Token.Path]);
                    if (recieverType == null)
                        return;
                    if (recieverType.IsModule)
                    {
                        Reject(extType.Token, "Receiver type cannot be module");
                        return;
                    }

                    myParam.Type = Resolver.GetTypeWithGenericParameters(table, recieverType);
                    extType.Token.AddInfo(myParam.Type);

                    // If the type has generic parameters, the function must have at least as many
                    if (myParam.Type.GenericParamCount() > method.GenericParamCount())
                    {
                        Reject(method.Token, $"'{method.Token}' must have at least {myParam.Type.GenericParamCount()} "
                            + $"generic parameter(s) because '{extType.Token}' takes that many generic parameter(s)");
                        return;
                    }
                }
                else
                {
                    // Generic parameters are supplied
                    myParam.Type = ResolveTypeNameOrReject(myParam, extType);
                }

                if (myParam.Type != null && func.Name == "new")
                {
                    foreach (var parameter in func.FunctionSignature[1])
                        if (parameter[0].Token != "")
                            Reject(parameter[0].Token, "'new' function must not have return parameters");
                    var returnParam = new Symbol(SymKind.FunParam, method, func.Name, "");
                    returnParam.Qualifiers |= SymQualifiers.ParamOut;
                    returnParam.Type = myParam.Type;
                    table.AddOrReject(returnParam);
                    method.Qualifiers |= SymQualifiers.Static;
                }


                if (myParam.Type != null && !method.IsStatic)
                {
                    table.AddOrReject(myParam);
                    method.Qualifiers |= SymQualifiers.Method;
                }

                if (myParam.Type != null && !noCompilerChecks)
                    myParam.Token.AddInfo(new VerifySuppressError());

                method.ReceiverType = myParam.Type;
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

        private static Symbol GetMutableMyParam(Symbol method, SyntaxFunc func, ref SyntaxExpr extType)
        {
            var myParam = new Symbol(SymKind.FunParam, method, func.Name, "my");
            if (extType != null && extType.Token == "mut")
            {
                myParam.SetQualifier("mut");
                extType = extType[0];
            }

            return myParam;
        }


        // Does not add a warning if there is already an error there
        static void Warning(Token token, string message)
        {
            if (!token.Error)
                token.AddWarning(new ZilWarn(message));
        }

    }
}
