﻿using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    public class ZilHeaderError : TokenError
    {
        public ZilHeaderError(string message) : base(message) { }
    }
    public class ZilWarn : TokenWarn
    {
        public ZilWarn(string message) : base(message) { }
    }

    class FileUseSymbols
    {
        public Dictionary<string, List<SymModule>> UseSymbols = new Dictionary<string, List<SymModule>>();
        public void AddSymbol(string symbol, SymModule module)
        {
            if (!UseSymbols.TryGetValue(symbol, out var mod))
            {
                mod = new List<SymModule>();
                UseSymbols[symbol] = mod;
            }
            if (!mod.Contains(module))
                mod.Add(module);
        }
    }

    class CompileHeader
    {
        const string BUILT_IN_TYPES = "* ^ [ ? ref own mut ro";
        const string ZURFUR_PRELUDE = "void bool i8 u8 byte i16 u16 i32 u32 i64 int u64 f32 f64 xint object str List Map Array Buffer Span";

        bool mNoCompilerChecks;
        SymbolTable mSymbols = new SymbolTable();
        Dictionary<string, Symbol> mUnaryTypeSymbols = new Dictionary<string, Symbol>();

        public SymbolTable Symbols => mSymbols;

        public CompileHeader()
        {
            // Find built in generic types
            foreach (var genericType in BUILT_IN_TYPES.Split(' '))
                mUnaryTypeSymbols[genericType] = mSymbols.Root.Children[genericType];
        }

        public bool NoCompilerChecks
        {
            get { return mNoCompilerChecks; }
            set 
            {
                mNoCompilerChecks = value;
                mSymbols.NoCompilerChecks = value;
            }
        }

        public void GenerateHeader(Dictionary<string, SyntaxFile> syntaxFiles)
        {
            // Find a symbol for the type or module syntax
            var syntaxScopeToSymbol = new Dictionary<SyntaxScope, Symbol>();

            AddModules();
            mSymbols.GenerateLookup();
            var fileUses = ProcessUseStatements();
            AddTypes();
            ResolveImpls();
            mSymbols.GenerateLookup();
            ResolveFields();
            ResolveMethods();
            ResolveTypeConstraints();
            mSymbols.GenerateLookup();
            CheckUseStatements();
            return;

            void AddModules()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var ns in syntaxFile.Value.Modules)
                    {
                        //var symbol = AddModule(ns.Value.Path);
                        var symbol = AddModule(ns.Value);
                        symbol.Comments += " " + ns.Value.Comments;
                    }
                }
            }

            Symbol AddModule(SyntaxScope m)
            {
                if (syntaxScopeToSymbol.TryGetValue(m, out var s1))
                    return s1;
                Symbol parent = mSymbols.Root;
                if (m.Parent != null)
                    parent = AddModule(m.Parent);
                if (parent.Children.TryGetValue(m.Name, out var s2))
                {
                    syntaxScopeToSymbol[m] = s2;
                    return s2;
                }
                var newModule = new SymModule(parent, m.Name);
                // TBD: Take qualifiers from module definition (generate error if inconsistent)
                newModule.Qualifiers = SymQualifiers.Pub;
                m.Name.AddInfo(newModule);
                var ok = mSymbols.AddOrReject(newModule);
                Debug.Assert(ok);
                syntaxScopeToSymbol[m] = newModule;
                return newModule;
            }

            Dictionary<string, FileUseSymbols> ProcessUseStatements()
            {
                var uses = new Dictionary<string, FileUseSymbols>();
                foreach (var syntaxFile in syntaxFiles)
                {
                    var fileUseSymbols = new FileUseSymbols();

                    // Add prelude
                    if (mSymbols.Root.Children.ContainsKey("Zurfur"))
                    {
                        var zurfurModule = (SymModule)mSymbols.Root.Children["Zurfur"];
                        foreach (var name in ZURFUR_PRELUDE.Split(' '))
                        {
                            fileUseSymbols.AddSymbol(name, zurfurModule);
                        }
                    }

                    // Process use statements
                    foreach (var use in syntaxFile.Value.Using)
                    {
                        var module = mSymbols.FindPathOrReject(use.ModuleName);
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
                            fileUseSymbols.AddSymbol(lastToken, (SymModule)module.Parent);
                        }
                        else
                        {
                            foreach (var token in use.Symbols)
                                fileUseSymbols.AddSymbol(token, (SymModule)module);
                        }

                    }
                    uses[syntaxFile.Key] = fileUseSymbols;
                }
                return uses;
            }

            void CheckUseStatements()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var use in syntaxFile.Value.Using)
                    {
                        var module = mSymbols.FindPathOrReject(use.ModuleName);
                        if (module == null || use.ModuleName.Length == 0)
                            continue;  // Error marked above
                        if (!module.IsModule)
                            continue; // Error marked above

                        foreach (var token in use.Symbols)
                        {
                            if (module.Children.TryGetValue(token, out var symbol))
                            {
                                if (symbol.IsAnyType)
                                    token.Type = eTokenType.TypeName;
                            }
                            else
                            {
                                Reject(token, $"Symbol not found in the module '{module.FullName}'");
                            }
                        }
                    }
                }
            }

            void AddTypes()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var type in syntaxFile.Value.Types)
                    {
                        if (type.ImplInterface != null || type.ImplType != null)
                        {
                            continue; // Process impl blocks later
                        }

                        if (!syntaxScopeToSymbol.TryGetValue(type.Parent, out var parent))
                            continue; // Syntax errors
                        var newType = new SymType(parent, syntaxFile.Key, type.Name);
                        newType.Comments = type.Comments;
                        newType.SetQualifiers(type.Qualifiers);
                        newType.Token.AddInfo(newType);
                        if (mSymbols.AddOrReject(newType))
                            AddTypeParams(newType, type.TypeArgs, syntaxFile.Key);
                        syntaxScopeToSymbol[type] = newType;
                    }
                }
            }

            void AddTypeParams(Symbol scope, IEnumerable<SyntaxExpr> typeArgs, string file)
            {
                if (typeArgs == null)
                    return;
                foreach (var expr in typeArgs)
                {
                    var typeParam = new SymTypeParam(scope, file, expr.Token);
                    if (mSymbols.AddOrReject(typeParam))
                        expr.Token.AddInfo(typeParam);
                }
            }

            // Impls go into a method group called $impl, and are
            // functions that take the interface and type as parameters.
            void ResolveImpls()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var type in syntaxFile.Value.Types)
                    {
                        var implType = type.GetImplType();
                        if (implType == null)
                            continue;
                        if (!syntaxScopeToSymbol.TryGetValue(type.Parent, out var scope))
                        {
                            Reject(implType.Token, $"Compiler error, couldn't find parent scope of '{type.Parent}'");
                            continue;
                        }
                        ResolveImpl(scope, implType, syntaxFile.Key);
                    }
                }
            }

            void ResolveImpl(Symbol scope, SyntaxType implSyntax, string file)
            {
                // Only process the impl block once
                if (syntaxScopeToSymbol.TryGetValue(implSyntax, out var tryFindImplMethodBlock))
                    return;

                var implTypeName = ResolveTypeNameOrReject(scope, implSyntax.ImplType, file);
                var implInterfaceName = ResolveTypeNameOrReject(scope, implSyntax.ImplInterface, file);
                if (implTypeName == "" || implInterfaceName == "")
                {
                    Reject(implSyntax.Name, "Error in impl block");
                    return;
                }

                var implType = new SymImplDef(scope, file, implSyntax.Keyword,
                                        $"$impl<{implInterfaceName},{implTypeName}>");
                if (scope.Children.ContainsKey(implType.Name))
                {
                    Reject(implSyntax.Name, "Duplicate impl block");
                    return;
                }
                var ok2 = mSymbols.AddOrReject(implType);
                Debug.Assert(ok2);
                var p1 = new SymMethodParam(implType, file, implSyntax.Keyword, "$interface");
                p1.TypeName = implInterfaceName;
                mSymbols.AddOrReject(p1);
                var p2 = new SymMethodParam(implType, file, implSyntax.Keyword, "$this");
                p2.TypeName = implTypeName;
                mSymbols.AddOrReject(p2);
                syntaxScopeToSymbol[implSyntax] = implType;
                implSyntax.Keyword.AddInfo(implType);
            }

            void ResolveFields()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var field in syntaxFile.Value.Fields)
                    {
                        // TBD: Impl's must not accept regular fields and const
                        //      fields should be moved to be similar to methods.
                        //      Maybe convert const fields to get methods

                        if (field.Parent.GetImplType() != null)
                        {
                            // TBD: Need to process const impl
                            // Convert to get method
                            Warning(field.Token, "Const fields not yet converted to get method");
                            continue;
                        }

                        if (!syntaxScopeToSymbol.TryGetValue(field.Parent, out var symParent))
                        {
                            Reject(field.Name, $"Symbol not processed because the parent scope has an error");
                            continue;
                        }

                        // Create the field
                        var symField = new SymField(symParent, syntaxFile.Key, field.Name);
                        symField.SetQualifiers(field.Qualifiers);
                        symField.Comments = field.Comments;
                        symField.Token.AddInfo(symField);
                        mSymbols.AddOrReject(symField);

                        if (field.Parent != null && field.Parent.Keyword == "enum")
                        {
                            // Enum feilds have their parent enum type
                            symField.TypeName = symField.Parent.FullName;
                            continue;
                        }
                        // Skip errors, user probably typing
                        if (field.TypeName == null || field.TypeName.Token.Name == "")
                        {
                            Reject(field.Name, "Expecting symbol to have an explicitly named type");
                            continue;
                        }
                        symField.TypeName = ResolveTypeNameOrReject(symField, field.TypeName, syntaxFile.Key);
                        if (symField.TypeName == "" && !NoCompilerChecks)
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
                        if (!syntaxScopeToSymbol.TryGetValue(type.Parent, out var module))
                            continue;  // Syntax error already marked
                        if (!module.Children.TryGetValue(type.Name, out var symbol))
                            continue; // Syntax error already marked
                        if (symbol.IsType)
                            ResolveConstraints(symbol, type.Constraints, syntaxFile.Key);
                    }
                }
            }

            void ResolveConstraints(Symbol scope, SyntaxConstraint[] synConstraints, string file)
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
                    var constrainedType = FindTypeInScope(name, scope);
                    if (constrainedType == null)
                    {
                        Reject(synConstraint.TypeName, $"The symbol '{name}' is undefined in the local scope");
                        continue;
                    }

                    string argName;
                    if (constrainedType.Name == "This")
                    {
                        argName = "#This";
                    }
                    else if (constrainedType.IsTypeParam)
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
                        var tn = ResolveTypeNameOrReject(constrainedType.Name == "This" 
                                                            ? scope : constrainedType, c, file);
                        if (tn == "")
                            continue;  // Error already given
                        var sym = mSymbols.Lookup(tn);
                        if (!sym.IsInterface)
                        {
                            // TBD: This should be in verification.
                            RejectTypeArgLeftDotRight(c, $"Symbol is not an interface, it is a {sym.KindName}");
                            continue;
                        }
                        if (constrainers.Contains(tn))
                        {
                            Reject(c.Token, $"Duplicate constraint:  '{tn}'");
                            continue;
                        }
                        constrainers.Add(tn);
                    }
                    if (constrainers.Count != 0)
                        symCon[argName] = constrainers.ToArray();
                }
                scope.Constraints = symCon;
            }

            void ResolveMethods()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var synFunc in syntaxFile.Value.Methods)
                    {
                        // Get containing scope, can be (module, type, or $impl)
                        if (!syntaxScopeToSymbol.TryGetValue(synFunc.Parent, out var scope))
                            continue; // Syntax errors
                        ResolveMethod(scope, synFunc, syntaxFile.Key);
                    }
                }
            }

            // Scope is where the function is defined (a module, type, or $impl)
            void ResolveMethod(Symbol scope, SyntaxFunc synFunc, string file)
            {
                Debug.Assert(scope.IsModule || scope.IsType || scope.IsImplDef);
                if (synFunc.MethodSignature.Count != 3)
                {
                    Reject(synFunc.Name, "Syntax error or compiler error");
                    return;
                }

                // Check for field or something with the same name
                if (scope.Children.TryGetValue(synFunc.Name, out var nameAtScope)
                    && !nameAtScope.IsMethodGroup)
                {
                    Reject(synFunc.Name, $"There is already a {nameAtScope.KindName} with that name in '{scope.FullName}'");
                    return;
                }

                // Lift the method up to the module level (out of type or impl)
                // TBD: Keep track of type args
                var moduleScope = scope;

                // TBD: Maybe put all methods at module level
                //while (!moduleScope.IsModule)
                //    moduleScope = moduleScope.Parent;

                // Find or create method group in this module
                if (!moduleScope.Children.TryGetValue(synFunc.Name, out var methodGroup))
                {
                    methodGroup = new SymMethodGroup(moduleScope, file, synFunc.Name);
                    var ok = mSymbols.AddOrReject(methodGroup);
                    Debug.Assert(ok);
                }
                if (!methodGroup.IsMethodGroup)
                {
                    Reject(synFunc.Name, $"There is already a {methodGroup.KindName} with that name in '{moduleScope}'");
                    return;
                }


                // Give each function a unique name (final name calculated below)
                var method = new SymMethod(methodGroup, file, synFunc.Name, $"$LOADING...${methodGroup.Children.Count}");
                method.SetQualifiers(synFunc.Qualifiers);
                method.Comments = synFunc.Comments;

                AddTypeParams(method, synFunc.TypeArgs, file);
                if (synFunc.Parent.GetImplType() != null)
                    AddImplParams(method, synFunc, file);
                else
                    AddThisParam(method, synFunc, file);
                ResolveMethodParams(file, method, synFunc.MethodSignature[0], false); // Parameters
                ResolveMethodParams(file, method, synFunc.MethodSignature[1], true);  // Returns

                method.Token.AddInfo(method);
                if (synFunc.Parent.Name.Error)
                {
                    // NOTE: Since the symbol is not stored, the method will not be compiled.
                    // TBD: Consider changing this so user can get feedback on errors.
                    Warning(synFunc.Token, $"Method not processed because '{synFunc.Parent.Name}' has an error");
                    return;
                }

                // Set the final function name: "`#(t1,t2...)(r1,r2...)"
                //      # - Number of generic parameters
                //      t1,t2... - Parameter types
                //      r1,r2... - Return types
                var genericsCount = method.GenericParamCount();
                var mp = method.Children.Values.Where(child => child.Kind == SymKind.MethodParam && !child.ParamOut).ToList();
                var mpr = method.Children.Values.Where(child => child.Kind == SymKind.MethodParam && child.ParamOut).ToList();
                mp.Sort((a, b) => a.Order.CompareTo(b.Order));
                mpr.Sort((a, b) => a.Order.CompareTo(b.Order));
                var methodName = (genericsCount == 0 ? "" : "`" + genericsCount)
                            + "(" + string.Join(",", mp.ConvertAll(a => a.TypeName)) + ")"
                            + "(" + string.Join(",", mpr.ConvertAll(a => a.TypeName)) + ")";
                method.SetName(methodName);
                mSymbols.AddOrReject(method);

                ResolveConstraints(method, synFunc.Constraints, file);
            }

            // Add $interface and $this parameters to functions defined
            // in impl blocks.
            void AddImplParams(Symbol method, SyntaxFunc func, string file)
            {
                if (!syntaxScopeToSymbol.TryGetValue(func.Parent, out var implBlock))
                {
                    Reject(func.Name, "Error in impl block");
                    return;
                }

                var p1 = new SymMethodParam(method, file, func.Name, "$interface");
                p1.TypeName = implBlock.Children["$interface"].TypeName;
                mSymbols.AddOrReject(p1);

                var p2 = new SymMethodParam(method, file, func.Name, "$this");
                p2.TypeName = implBlock.Children["$this"].TypeName;
                mSymbols.AddOrReject(p2);
            }

            // Add $this parameter for extension methods and member functions.
            // NOTE: Even static methods get $this, but it is used as a
            //       type name and not passed as a parameter
            void AddThisParam(Symbol method, SyntaxFunc func, string file)
            {
                var isExtension = func.ExtensionType != null && func.ExtensionType.Token != "";
                if (isExtension || method.Parent.Parent.IsType)
                {
                    // First parameter is "$this"
                    var methodParam = new SymMethodParam(method, file, func.Name, "$this");
                    if (isExtension)
                    {
                        methodParam.TypeName = ResolveTypeNameOrReject(methodParam, func.ExtensionType, file);
                        method.Qualifiers |= SymQualifiers.Extension;
                    }
                    else
                    {
                        methodParam.TypeName = AddOuterGenericParameters(method.Parent.Parent, method.Parent.Parent).ToString();
                    }
                    if (methodParam.TypeName == "" && !NoCompilerChecks)
                        methodParam.Token.AddInfo(new VerifySuppressError());
                    mSymbols.AddOrReject(methodParam);
                }
            }

            void ResolveMethodParams(string file, Symbol method, SyntaxExpr parameters, bool isReturn)
            {
                if (parameters is SyntaxError)
                    return;

                foreach (var expr in parameters)
                {
                    if (expr is SyntaxError)
                        continue;
                    Debug.Assert(expr.Count == 2);

                    var token = expr.Token == "" ? expr[1].Token : expr.Token;
                    var name = expr.Token == "" ? "$return" : expr.Token.Name;
                    var methodParam = new SymMethodParam(method, file, token, name);
                    methodParam.ParamOut = isReturn;
                    methodParam.TypeName = ResolveTypeNameOrReject(methodParam, expr[0], file);
                    if (methodParam.TypeName == "" && !NoCompilerChecks)
                        methodParam.Token.AddInfo(new VerifySuppressError());
                    if (methodParam.TypeName != "" && expr.Token.Name != "")
                        expr.Token.AddInfo(methodParam);
                    mSymbols.AddOrReject(methodParam);
                }
            }

            string ResolveTypeNameOrReject(Symbol scope, SyntaxExpr typeExpr, string file)
            {
                var symbol = ResolveTypeOrReject(typeExpr, false, scope, fileUses[file], file);
                if (symbol == null)
                    return "";

                if (symbol.IsAnyTypeNotModule  || NoCompilerChecks)
                    return symbol.FullName;

                RejectTypeArgLeftDotRight(typeExpr, "The symbol is not a type, it is a " + symbol.KindName);
                return "";
            }

        }

        /// <summary>
        /// Resolve a type.  Non-generic types are found in the symbol table
        /// and given a full name (e.g. 'int' -> 'Zufur.int').  Generic types
        /// must have all type arguments resolved as well.
        /// Return symbol is always a module, type, or type parameter.
        /// Null is returned for error, and the error is marked.
        /// </summary>
        Symbol ResolveTypeOrReject(SyntaxExpr typeExpr,
                                   bool isDot,
                                   Symbol scope,
                                   FileUseSymbols useScope,
                                   string file,
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

            if (mUnaryTypeSymbols.ContainsKey(typeExpr.Token) || typeExpr.Token == ParseZurf.VT_TYPE_ARG)
                return ResolveGenericTypeOrReject();

            // Resolve regular symbol
            bool foundInScope = false;
            var symbol = isDot ? FindLocalTypeOrReject(typeExpr.Token, scope)
                               : FindGlobalTypeOrReject(typeExpr.Token, scope, useScope, out foundInScope);
            if (symbol == null)
                return null; // Error already marked

            if (!symbol.IsAnyType || symbol.IsSpecializedType)
            {
                Reject(typeExpr.Token, "The symbol is not a type, it is a " + symbol.KindName);
                return NoCompilerChecks ? symbol : null;
            }

            if (!hasGenericParams && symbol.GenericParamCount() != 0)
            {
                Reject(typeExpr.Token, $"Expecting {symbol.GenericParamCount()} generic parameter(s), but got 0");
                if (!NoCompilerChecks)
                    return null;
            }

            typeExpr.Token.AddInfo(symbol);

            // Type parameter
            if (symbol.IsTypeParam)
            {
                return mSymbols.GetGenericParam(symbol.GenericParamNum());
            }

            // Type inference: Add implied types to inner types found in this scope
            // e.g: InnerType => OuterType<T>.InnerType
            if (!isDot && symbol.IsType && foundInScope)
                symbol = AddOuterGenericParameters(symbol, symbol.Parent);

            return symbol;

            Symbol ResolveDotOrReject()
            {
                var leftSymbol = ResolveTypeOrReject(typeExpr[0], isDot, scope, useScope, file);
                if (leftSymbol == null)
                    return null;
                if (typeExpr.Count != 2)
                {
                    // Probably user is still typing
                    Reject(typeExpr.Token, $"Syntax error");
                    return null;
                }
                if (!leftSymbol.IsAnyType || leftSymbol.IsTypeParam)
                {
                    Reject(typeExpr.Token, $"The left side of the '.' must evaluate to a module or type, but it is a {leftSymbol.KindName}");
                    return null;
                }

                // The right side of the "." is only a type name identifier, excluding generic parameters.
                var leftScope = leftSymbol.IsSpecializedType ? leftSymbol.Parent : leftSymbol;
                var rightSymbol = ResolveTypeOrReject(typeExpr[1], true, leftScope, useScope, file, hasGenericParams);
                if (rightSymbol == null)
                    return null;

                if (!rightSymbol.IsAnyType)
                {
                    Reject(typeExpr[1].Token, $"The right side of the '.' must evaluate to a module or type, but it is a {rightSymbol.KindName}");
                    return null;
                }

                if (leftSymbol.IsSpecializedType)
                    return mSymbols.GetSpecializedType(rightSymbol, ((SymSpecializedType)leftSymbol).Params);

                return rightSymbol;
            }


            // Resolve "fun" or "afun" types
            Symbol ResolveLambdaFunOrReject()
            {
                if (typeExpr.Count < 3)
                {
                    Reject(typeExpr.Token, "Syntax error");
                    return null;
                }

                // Generic type arguments: TBD
                //foreach (var generic in typeExpr[0])
                //    mSymbols.Add(new SymTypeArg(scope, file, generic.Token, 0));

                // NOTE: This holds more info about the function, such as the
                // variable name.  For now, we are going to throw away this
                // extra info, but it might be nice to keep around to show
                // the user.  So for now, these type definitions are identical
                // and consolidated in the symbol table:
                //      @a fun(a int)int   ==   @ fun(int)int
                //      @b fun(b int)int   ==   @ fun(int)int
                // NOTE 2: This scope is only used to resolve the function
                //         parameters, then it is thrown away.
                var funParamsScope = new SymMethod(scope, "", new Token("$unused"), "$unused");

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
                Symbol genericFunType = mSymbols.FindOrAddIntrinsicType(name, numGenerics);

                var concreteType = genericFunType;
                return mSymbols.GetSpecializedType(concreteType, paramTypes.ToArray(), returnTypes.ToArray());
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
                    var sym = ResolveTypeOrReject(pType[0], false, paramScope, useScope, file);
                    if (sym == null)
                    {
                        resolved = false;
                        continue;
                    }
                    if (sym.IsAnyTypeNotModule)
                    {
                        paramTypes.Add(sym);
                        var newMethodParam = new SymMethodParam(paramScope, file, pType.Token);
                        newMethodParam.ParamOut = isReturn;
                        newMethodParam.Token.AddInfo(newMethodParam);
                        mSymbols.AddOrReject(newMethodParam); // TBD: Fix
                    }
                    else
                    {
                        Reject(sym.Token, $"Expecting a type, but got a {sym.KindName}");
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
                    Reject(typeExpr.Token, "Syntax error, unexpected empty type argument list");
                    return null;
                }

                // Reject incorrect number of type arguments, for example, `List<int,int>`
                var concreteType = typeParent.IsSpecializedType ? typeParent.Parent : typeParent;
                if (concreteType.GenericParamCount() != typeParams.Count)
                {
                    RejectTypeArgLeftDotRight(typeExpr,
                        $"Expecting {concreteType.GenericParamCount()} generic parameter(s), but got {typeParams.Count}");
                    if (!NoCompilerChecks)
                        return null;
                }

                if (typeParent is SymSpecializedType pt)
                {
                    Debug.Assert(pt.Returns.Length == 0); // TBD: Fix for functions
                    for (int i = 0; i < pt.Params.Length; i++)
                        typeParams.Insert(i, pt.Params[i]);
                }

                return mSymbols.GetSpecializedType(concreteType, typeParams.ToArray());
            }


            bool ResolveTypeGenericParamsOrReject(out Symbol typeParent, out List<Symbol> typeParams)
            {
                if (typeExpr.Count == 0)
                {
                    Reject(typeExpr.Token, "Syntax error");
                    typeParent = null;
                    typeParams = null;
                    return false;
                }

                var resolved = true;
                int typeParamIndex = 0;
                if (typeExpr.Token != ParseZurf.VT_TYPE_ARG)
                {
                    // Unary type symbol symbol
                    typeParent = mUnaryTypeSymbols[typeExpr.Token];
                }
                else
                {
                    // Parameter list, eg: typeParent<T1,T2,...>
                    typeParamIndex = 1;
                    typeParent = ResolveTypeOrReject(typeExpr[0], false, scope, useScope, file, true);
                    if (typeParent == null)
                        resolved = false;
                    else if (!typeParent.IsAnyTypeNotModule)
                    {
                        Reject(typeExpr[typeParamIndex].Token, $"Expecting a type, but got a {typeParent.KindName}");
                        resolved = false;
                    }
                }

                // Process type parameters
                typeParams = new List<Symbol>();
                for (; typeParamIndex < typeExpr.Count;  typeParamIndex++)
                {
                    var sym = ResolveTypeOrReject(typeExpr[typeParamIndex], false, scope, useScope, file);
                    if (sym == null)
                        resolved = false;
                    else if (sym.IsAnyTypeNotModule)
                        typeParams.Add(sym);
                    else
                    {
                        Reject(typeExpr[typeParamIndex].Token, $"Expecting a type, but got a {sym.KindName}");
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
        private void RejectTypeArgLeftDotRight(SyntaxExpr expr, string errorMessage)
        {
            bool walking;
            do {
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

            Reject(expr.Token, errorMessage);
        }


        /// <summary>
        /// Add outer generic parameters to the concrete type
        /// e.g: OuterType`1.InnerType => OuterType`1.InnerType<#0>
        /// </summary>
        private Symbol AddOuterGenericParameters(Symbol symbol, Symbol outerScope)
        {
            Debug.Assert(symbol.IsType);
            var genericParamCount = outerScope.GenericParamTotal();
            if (genericParamCount == 0)
                return symbol;

            var genericParams = new List<Symbol>();
            for (int i = 0; i < genericParamCount; i++)
                genericParams.Add(mSymbols.GetGenericParam(i));

            return mSymbols.GetSpecializedType(symbol, genericParams.ToArray());
        }

        /// <summary>
        /// Find a type or module at the current scope.
        /// Return null if not found.
        /// </summary>
        public Symbol FindLocalTypeOrReject(Token name, Symbol scope)
        {
            if (!scope.Children.TryGetValue(name, out var symbol))
            {
                Reject(name, $"'{name}' is not a member of '{scope}'");
                return null;
            }
            if (symbol.IsAnyType)
                return symbol;
            Reject(name, $"'{name}' is not a type, it is a '{symbol.KindName}'");
            return null;
        }

        /// <summary>
        /// Find a type or module in the current scope.
        /// If it's not found, scan use statements for all occurences. 
        /// Marks an error if undefined or duplicate.  Returns null on error.
        /// TBD: If symbol is unique in this package, but duplicated in an
        /// external package, is that an error?  Yes for now.
        /// </summary>
        public Symbol FindGlobalTypeOrReject(Token name, Symbol scope, FileUseSymbols use, out bool foundInScope)
        {
            var symbol = FindTypeInScope(name.Name, scope);
            if (symbol != null)
            {
                foundInScope = true;
                return symbol;
            }
            foundInScope = false;

            // Check for 'use' symbol
            var symbols = new List<Symbol>(); // TBD: Be kind to GC
            if (use.UseSymbols.TryGetValue(name.Name, out var modules))
            {
                foreach (var module in modules)
                {
                    if (module.Children.TryGetValue(name.Name, out var s))
                    {
                        symbols.Add(s);
                    }
                }
            }

            if (symbols.Count == 0)
            {
                Reject(name, "Undefined type name");
                return null;
            }
            if (symbols.Count > 1)
            {
                Reject(name, $"Multiple symbols found.  Found in '{symbols[0]}' and '{symbols[1]}'");
                return null;
            }
            return symbols[0];
        }

        /// <summary>
        /// Find the type or module in the scope, or null if not found.
        /// Does not search use statements.
        /// </summary>
        public Symbol FindTypeInScope(string name, Symbol scope)
        {
            while (scope.Name != "")
            {
                if (scope.Name == name && scope.IsAnyType)
                    return scope;
                if (scope.Children.TryGetValue(name, out var s1) && s1.IsAnyType)
                    return s1;
                scope = scope.Parent;
            }
            if (name == "This")
                return scope.Children["This"];

            return null;
        }

        // Does not reject if there is already an error there
        void Reject(Token token, string message)
        {
            if (NoCompilerChecks)
            {
                if (!token.Warn)
                    token.AddWarning(new ZilWarn("(No compiler checks): " + message));
            }
            else
            {
                if (!token.Error)
                    token.AddError(new ZilHeaderError(message));
            }
        }

        // Does not add a warning if there is already an error there
        void Warning(Token token, string message)
        {
            if (!token.Error)
                token.AddWarning(new ZilWarn(message));
        }

    }
}
