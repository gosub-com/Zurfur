using System;
using System.CodeDom;
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
        const string ZURFUR_PRELUDE = "void nil bool i8 byte i16 u16 i32 u32 int u64 f32 f64 object str List Map Array Buffer Span";
        static WordSet sOperatorFunctionNames = new WordSet(
            "_opAdd _opSub _opNeg _opMul _opDiv _opRem _opRange _opIn _opEq _opEqNan _opCmp _opCmpNan " 
            + "_opBitShl _opBitShr _opBitAnd _opBitOr _opBitXor _opBitNot _opIndex");

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
                var newModule = new SymModule(parent, m.Name);
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
                    if (table.Root.TryGetPrimary("Zurfur", out var zSym) && zSym is SymModule zMod)
                        foreach (var name in ZURFUR_PRELUDE.Split(' '))
                            AddUseSymbolsFromModule(zMod, name, null, fileUseSymbols);

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
                            AddUseSymbolsFromModule((SymModule)module, token.Name, addSymbolInfo ? token : null, fileUseSymbols);
                        }
                    }
                    uses.Files[syntaxFile.Key] = fileUseSymbols;
                }
                return uses;
            }

            void AddUseSymbolsFromModule(SymModule module, string name, Token token, UseSymbolsFile useSymbolsFile)
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
            List<Symbol> GetUseSymbolsFromModule(SymModule module, string name)
            {
                var symbols = new List<Symbol>();
                if (module.TryGetPrimary(name, out Symbol typeSym))
                {
                    symbols.Add(typeSym);

                    // Add operators for this type
                    if (typeSym.IsType)
                    {
                        foreach (var op in module.Children)
                        {
                            if (op.IsFun && sOperatorFunctionNames.Contains(op.SimpleName))
                            {
                                foreach (var child in op.ChildrenFilter(SymKind.FunParam).Where(child => !child.ParamOut))
                                {
                                    if (child.Type != null && child.Type.Unspecial().FullName == typeSym.FullName)
                                    {
                                        symbols.Add(op);
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                
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
                    var constrainedType = Resolver.FindTypeInScopeWalk(name, scope);
                    if (constrainedType == null)
                    {
                        Reject(synConstraint.TypeName, $"The symbol '{name}' is undefined in the local scope");
                        continue;
                    }

                    string argName;
                    if (constrainedType.IsTypeParam)
                    {
                        argName = "#" + constrainedType.GenericParamNum();
                        synConstraint.TypeName.AddInfo(constrainedType);
                    }
                    else
                    {
                        Reject(synConstraint.TypeName, $"The symbol '{name}' is not a type parameter, it is a {constrainedType.KindName}");
                        continue;
                    }

                    if (symCon.ContainsKey(name))
                    {
                        Reject(synConstraint.TypeName, $"Constraints for this type parameter were already defined.  Use '+' to add more");
                        continue;
                    }
                    var constrainers = new List<string>();
                    foreach (var c in synConstraint.TypeConstraints)
                    {
                        var sym = ResolveTypeNameOrReject(constrainedType.SimpleName == "This" 
                                                            ? scope : constrainedType, c);
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

                // Give each function a unique name (final name calculated below)
                var function = new SymFun(scope, synFunc.Name, $"$LOADING...${scope.ChildrenCount}");
                function.SetQualifiers(synFunc.Qualifiers);
                function.Comments = synFunc.Comments;
                AddExtensionMethodGenerics(function, synFunc);
                AddTypeParams(function, synFunc.TypeArgs);
                var myParam = AddMyParam(function, synFunc);
                Resolver.ResolveFunParams(synFunc.FunctionSignature[0], table, function, function, useSymbolsFile, false);
                Resolver.ResolveFunParams(synFunc.FunctionSignature[1], table, function, function, useSymbolsFile, true);
                SetNewFunction(function, synFunc, myParam);

                // Create parameters/returns tuple
                var parameters = GetFunParamsTuple(function, false);
                var returns = GetFunParamsTuple(function, true);
                var tupleBaseType = table.GetTupleBaseType(2);
                function.Type = table.CreateSpecializedType(tupleBaseType, new Symbol[] { parameters, returns });

                // Set the function name F(type1,type2)(returnType)
                var genericsCount = function.GenericParamCount();
                var name = synFunc.Name + (genericsCount == 0 ? "" : "`" + genericsCount)
                    + "(" + string.Join<Symbol>(",", parameters.TypeArgs) + ")"
                    + "(" + string.Join<Symbol>(",", returns.TypeArgs) + ")";
                function.SetLookupName(name);

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

            // For now, extension methods with generic receivers
            // allow only 1 level deep with all type parameters matching:
            //      List<int>.f(x)        // Ok, no generic types
            //      List<T>.f(x)          // Ok, 1 level, matching generic
            //      Map<TKey,TValue>.f(x) // Ok, 1 level, all matching generic
            //      Map<Key,int>.f(x)     // No, not all matching genrics
            //      Span<List<T>>.f(x)    // No, multi-level not accepted
            //
            // TBD: Allow Map<K,V>.Pair, etc.
            void AddExtensionMethodGenerics(Symbol function, SyntaxFunc f)
            {
                if (f.ExtensionType == null)
                    return;

                var extensionType = f.ExtensionType;
                function.Qualifiers |= SymQualifiers.Extension;

                // Skip qualifiers. 
                while (extensionType.Token == "mut" && extensionType.Count != 0)
                    extensionType = extensionType[0];

                // TBD: Follow "."
                if (extensionType.Token != ParseZurf.VT_TYPE_ARG || extensionType.Count < 2)
                    return;

                var typeName = extensionType[0].Token;
                var typeSymbol = Resolver.FindGlobalType(typeName, table, function, useSymbols.Files[typeName.Path], out var inScope);
                if (typeSymbol == null)
                    return;

                var genericMatch = true;
                Token firstMatchedType = null;
                for (int i = 1; i < extensionType.Count; i++)
                {
                    var paramName = extensionType[i].Token;
                    if (!typeSymbol.TryGetPrimary(paramName, out var matchGeneric))
                    {
                        genericMatch = false;
                        continue;
                    }
                    if (matchGeneric.Order != i - 1)
                    {
                        Reject(paramName, $"Generic parameter '{paramName}' found at wrong position of '{typeName}'");
                        genericMatch = false;
                        break;
                    }
                    firstMatchedType = paramName;
                }
                if (genericMatch)
                    AddTypeParams(function, extensionType.Skip(1));
                else if (firstMatchedType != null)
                    Reject(firstMatchedType, $"Type parameter '{firstMatchedType}' found in '{typeName}', but other type parameters don't match.");
            }
            

            // Add `my` parameter for extension methods and member functions.
            Symbol AddMyParam(Symbol method, SyntaxFunc func)
            {
                // Interface method syntax
                if (method.Parent.IsInterface)
                {
                    //Debug.Assert(func.ExtensionType == null && method.Parent.IsType);
                    var ifaceMethodParam = new Symbol(SymKind.FunParam, method, func.Name, "my");
                    ifaceMethodParam.Type = Resolver.GetTypeWithGenericParameters(
                                                table, method.Parent, method.GenericParamTotal());
                    table.AddOrReject(ifaceMethodParam);
                    return ifaceMethodParam;
                }

                var extType = func.ExtensionType;
                if (extType == null || extType.Token == "")
                    return null;

                // Extension method syntax
                var methodParam = new Symbol(SymKind.FunParam, method, func.Name, "my");
                if (extType.Token == "mut")
                {
                    methodParam.SetQualifier("mut");
                    extType = extType[0];
                }
                methodParam.Type = ResolveTypeNameOrReject(methodParam, extType);
                method.Qualifiers |= SymQualifiers.Extension;
                if (methodParam.TypeName == "" && !noCompilerChecks)
                    methodParam.Token.AddInfo(new VerifySuppressError());
                table.AddOrReject(methodParam);
                return methodParam;
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
                var returnParam = new Symbol(SymKind.FunParam, function, synFunc.Name, "$0");
                returnParam.Qualifiers |= SymQualifiers.ParamOut;
                returnParam.Type = myParam.Type;
                table.AddOrReject(returnParam);
            }

            // Get parameters or returns as a tuple.
            Symbol GetFunParamsTuple(Symbol symbol, bool returns)
            {
                var parameters = symbol.ChildrenFilter(SymKind.FunParam)
                    .Where(child => child.Type != null && returns == child.ParamOut).ToList();

                var tupleParent = table.GetTupleBaseType(parameters.Count);

                parameters.Sort((a, b) => a.Order.CompareTo(b.Order));
                var paramTypes = parameters.Select(p => p.Type).ToArray();
                var paramNames = parameters.Select(p => p.FullName).ToArray();

                return table.CreateSpecializedType(tupleParent, paramTypes, paramNames);
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
