using System;
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

    class ZilGenHeader
    {
        bool mNoCompilerChecks;
        SymbolTable mSymbols = new SymbolTable();
        Dictionary<string, SymType> mUnaryTypeSymbols = new Dictionary<string, SymType>();

        readonly static string[] sQualifiersPubModule = new string[] { "pub", "module" };
        readonly static string[] sQualifiersPtype = new string[] { "type_param" };
        readonly static string[] sQualifiersParam = new string[] { "param" };
        readonly static string[] sQualifiersParamReturn = new string[] { "param_return" };

        public SymbolTable Symbols => mSymbols;

        public ZilGenHeader()
        {
            // Find built in generic types
            foreach (var genericType in "* ^ [ ? ref own mut ro".Split(' '))
                mUnaryTypeSymbols[genericType] = (SymType)mSymbols.Root.Children[genericType];
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
            AddModules();
            mSymbols.GenerateLookup();
            var fileUses = ProcessUseStatements();

            // Temporary lookup table to find symbols from syntax
            var syntaxToSymbol = new Dictionary<object, Symbol>();

            AddTypes();
            AddImpls();

            AddFields();
            AddMethodGroups();
            mSymbols.GenerateLookup();
            ResolveTypeConstraints();
            ResolveFieldTypes();
            ResolveMethodGroups();
            mSymbols.GenerateLookup();
            return;

            void AddModules()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var ns in syntaxFile.Value.Modules)
                    {
                        var symbol = AddModule(syntaxFile.Key, ns.Value.Path);
                        symbol.Comments += " " + ns.Value.Comments;
                        // TBD: Accumulate qualifiers so we get "pub"
                    }
                }
            }

            // Add the module, or return the one we already have
            Symbol AddModule(string file, Token[] path)
            {
                var childrenSymbols = mSymbols.Root.Children;
                Symbol parentSymbol = mSymbols.Root;
                Symbol newModule = null;
                foreach (var token in path)
                {
                    if (childrenSymbols.TryGetValue(token.Name, out var childModule))
                    {
                        Debug.Assert(childModule is SymModule);
                        newModule = childModule;
                    }
                    else
                    {
                        newModule = new SymModule(parentSymbol, token.Name);
                        newModule.Qualifiers = sQualifiersPubModule; // TBD: Allow private/internal
                        token.AddInfo(newModule);
                        mSymbols.AddOrReject(newModule);
                    }

                    parentSymbol = newModule;
                    childrenSymbols = newModule.Children;
                }
                return newModule;
            }

            void AddTypes()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var type in syntaxFile.Value.Types)
                    {
                        if (type.ImplInterface != null || type.ImplType != null)
                            continue; // Process impl blocks later

                        var newType = new SymType(mSymbols.FindPath(type.ModulePath), syntaxFile.Key, type.Name);
                        newType.Comments = type.Comments;
                        newType.Qualifiers = Array.ConvertAll(type.Qualifiers, a => a.Name).ToArray();
                        newType.Token.AddInfo(newType);
                        if (mSymbols.AddOrReject(newType))
                        {
                            AddTypeParams(newType, type.TypeArgs, syntaxFile.Key, sQualifiersPtype);
                            syntaxToSymbol[type] = newType;
                        }
                    }
                }
            }

            void AddTypeParams(Symbol scope, IEnumerable<SyntaxExpr> typeArgs, string file, string[] qualifiers)
            {
                foreach (var expr in typeArgs)
                {
                    var typeParam = new SymTypeParam(scope, file, expr.Token);
                    typeParam.Qualifiers = qualifiers;
                    if (mSymbols.AddOrReject(typeParam))
                        expr.Token.AddInfo(typeParam);
                }
            }


            void AddImpls()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var type in syntaxFile.Value.Types)
                    {
                        if (type.ImplInterface == null || type.ImplType == null)
                            continue; // Non-imple types already processed

                        var module = mSymbols.FindPath(type.ModulePath);
                        var implType = ResolveTypeNameOrReject(module, type.ImplType, syntaxFile.Key);
                        var implInterface = ResolveTypeNameOrReject(module, type.ImplInterface, syntaxFile.Key);

                        if (implType == "" || implInterface == "")
                        {
                            Reject(type.Keyword, "'impl' not processed because of error");
                            continue;
                        }

                        // TBD: Still not sure how to store these in the symbol table.
                        var newType = new SymType(mSymbols.FindPath(type.ModulePath),
                            syntaxFile.Key, type.Name, "$impl:" + implInterface + "::" + implType);
                        newType.Comments = type.Comments;
                        newType.Qualifiers = Array.ConvertAll(type.Qualifiers, a => a.Name).ToArray();
                        newType.Qualifiers = newType.Qualifiers.Append("interface_impl").ToArray();
                        type.Keyword.AddInfo(newType);
                        if (mSymbols.AddOrReject(newType))
                            syntaxToSymbol[type] = newType;
                    }
                }
            }

            void AddFields()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var field in syntaxFile.Value.Fields)
                    {
                        // TBD: Impl's must not accept regular fields and const
                        //      fields should be moved to be similar to methods.
                        //      Maybe convert const fields to get methods

                        if (!syntaxToSymbol.TryGetValue(field.ParentScope, out var symParent))
                        {
                            Warning(field.Name, $"Symbol not processed because the parent scope has an error");
                            continue;
                        }

                        var newField = new SymField(symParent, syntaxFile.Key, field.Name);
                        newField.Qualifiers = Array.ConvertAll(field.Qualifiers, a => a.Name).Append("field").ToArray();
                        newField.Comments = field.Comments;
                        newField.Token.AddInfo(newField);
                        mSymbols.AddOrReject(newField);
                        syntaxToSymbol[field] = newField;
                    }
                }
            }

            void AddMethodGroups()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var method in syntaxFile.Value.Methods)
                    {
                        Symbol scope;
                        var parentType = method.ParentScope as SyntaxType;
                        if (parentType != null && parentType.ImplInterface != null)
                        {
                            // TBD: For now these go inside the impl block type.
                            //      This may be changed to put them at the module level

                            if (!syntaxToSymbol.TryGetValue(parentType, out var implScope))
                            {
                                Warning(method.Name, $"Symbol not processed because the parent scope has an error");
                                continue;
                            }
                            scope = implScope;
                        }
                        else
                        {
                            scope = mSymbols.FindPath(method.ModulePath);
                        }

                        if (!scope.Children.TryGetValue(method.Name, out var methodGroup))
                        {
                            methodGroup = new SymMethodGroup(scope, method.Name.Name);
                            mSymbols.AddOrReject(methodGroup);
                        }
                        syntaxToSymbol[method] = methodGroup;
                    }
                }
            }

            Dictionary<string, string[]> ProcessUseStatements()
            {
                var uses = new Dictionary<string, string[]>();
                foreach (var syntaxFile in syntaxFiles)
                {
                    var useModules = new List<string>();
                    foreach (var use in syntaxFile.Value.Using)
                    {
                        var symbol = mSymbols.FindPathOrReject(use.NamePath);
                        if (symbol == null || use.NamePath.Length == 0)
                            continue;  // Error marked by function

                        var lastToken = use.NamePath[use.NamePath.Length - 1];
                        if (!(symbol is SymModule))
                        {
                            Reject(lastToken, "Must be a module, not a " + symbol.Kind);
                            continue;
                        }

                        if (useModules.Contains(symbol.FullName))
                        {
                            Reject(lastToken, "Already included in previous use statement");
                            continue;
                        }
                        useModules.Add(symbol.FullName);
                    }
                    uses[syntaxFile.Key] = useModules.ToArray();
                }
                return uses;
            }

            void ResolveTypeConstraints()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var type in syntaxFile.Value.Types)
                    {
                        var module = mSymbols.FindPath(type.ModulePath);
                        if (!module.Children.TryGetValue(type.Name, out var symbol))
                            continue; // Syntax error already marked
                        if (symbol is SymType symType)
                            ResolveConstraints(symType, type.Constraints, syntaxFile.Key);
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
                    else if (constrainedType is SymTypeParam typeParam)
                    {
                        argName = "#" + typeParam.GenericParamNum();
                        synConstraint.TypeName.AddInfo(typeParam);
                    }
                    else
                    {
                        Reject(synConstraint.TypeName, $"The symbol '{name}' is not a type parameter, it is a {constrainedType.Kind}");
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
                            // TBD: Check for interface, also need to accept SymSpecializedType.
                            //      Also, this should be in verification.
                            RejectTypeArgLeftDotRight(c, $"Symbol is not an interface, it is a {sym.Kind}");
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


            void ResolveFieldTypes()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var field in syntaxFile.Value.Fields)
                    {
                        if (!syntaxToSymbol.TryGetValue(field, out var symbol)
                                || !(symbol is SymField symField))
                            continue;  // Warning given by AddFields

                        if (field.ParentScope != null && field.ParentScope.Keyword == "enum")
                        {
                            // The feild has it's parent enumeration type
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

            void ResolveMethodGroups()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var method in syntaxFile.Value.Methods)
                    {
                        var groupScope = syntaxToSymbol[method] as SymMethodGroup;
                        if (groupScope == null)
                            continue;  // // Warning given by AddMethodGroups

                        ResolveMethodTypes(groupScope, method, syntaxFile.Key);
                    }
                }
            }

            void ResolveMethodTypes(SymMethodGroup scope, SyntaxFunc func, string file)
            {
                if (func.MethodSignature.Count != 3)
                {
                    Reject(func.Name, "Syntax error or compiler error");
                    return;
                }

                // Give each function a unique name (final name calculated below)
                var method = new SymMethod(scope, file, func.Name,  $"$LOADING...${scope.Children.Count}");
                method.Qualifiers = Array.ConvertAll(func.Qualifiers, a => a.Name).Append("method").ToArray();
                method.Comments = func.Comments;

                AddTypeParams(method, func.TypeArgs, file, sQualifiersPtype);

                // Add first parameter for extension types and non-static members
                var isExtension = func.ExtensionType != null && func.ExtensionType.Token != "";
                var parentType = scope.Parent as SymType;
                if (isExtension
                    || parentType != null && !method.Qualifiers.Contains("static"))
                {
                    // First parameter is "$this"
                    var methodParam = new SymMethodParam(method, file, func.Name, "$this", false);
                    methodParam.Qualifiers = sQualifiersParam;
                    if (isExtension)
                    {
                        methodParam.TypeName = ResolveTypeNameOrReject(methodParam, func.ExtensionType, file);
                        Array.Resize(ref method.Qualifiers, method.Qualifiers.Length + 1);
                        method.Qualifiers[method.Qualifiers.Length - 1] = "extension";
                    }
                    else
                    {
                        methodParam.TypeName = AddOuterGenericParameters(parentType, parentType).ToString();
                    }
                    if (methodParam.TypeName == "" && !NoCompilerChecks)
                        methodParam.Token.AddInfo(new VerifySuppressError());
                    var ok = mSymbols.AddOrReject(methodParam);
                    Debug.Assert(ok); // Unique first param
                }


                // Resolve method parameters and returns (TBD: error/exit specifier)
                ResolveMethodParams(file, method, func.MethodSignature[0], false); // Parameters
                ResolveMethodParams(file, method, func.MethodSignature[1], true);  // Returns

                method.Token.AddInfo(method);
                var scopeParent = func.ModulePath[func.ModulePath.Length - 1];
                if (scopeParent.Error)
                {
                    Warning(func.Token, $"Symbol not processed because '{scopeParent}' has an error");
                    return;
                }

                // Set the final function name: "`#(t1,t2...)(r1,r2...)"
                //      # - Number of generic parameters
                //      t1,t2... - Parameter types
                //      r1,r2... - Return types
                var genericsCount = method.GenericParamCount();
                var mp = method.FindChildren("param");
                var mpr = method.FindChildren("param_return");
                mp.Sort((a, b) => a.Order.CompareTo(b.Order));
                mpr.Sort((a, b) => a.Order.CompareTo(b.Order));
                var methodName = (genericsCount == 0 ? "" : "`" + genericsCount)
                            + "(" + string.Join(",", mp.ConvertAll(a => a.TypeName)) + ")"
                            + "(" + string.Join(",", mpr.ConvertAll(a => a.TypeName)) + ")";
                method.SetName(methodName);
                mSymbols.AddOrReject(method);

                ResolveConstraints(method, func.Constraints, file);
            }

            void ResolveMethodParams(string file, SymMethod method, SyntaxExpr parameters, bool isReturn)
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
                    var methodParam = new SymMethodParam(method, file, token, name, isReturn);
                    methodParam.Qualifiers = isReturn ? sQualifiersParamReturn : sQualifiersParam;
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

                if (symbol is SymType || symbol is SymTypeParam || symbol is SymSpecializedType || NoCompilerChecks)
                    return symbol.FullName;

                RejectTypeArgLeftDotRight(typeExpr, "The symbol is not a type, it is a " + symbol.Kind);
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
                                   string []useScope,
                                   string file,
                                   bool hasGenericParams = false)
        {
            // There will also be a syntax error
            if (typeExpr == null || typeExpr.Token.Name == "")
                return null;
            Debug.Assert(!(scope is SymSpecializedType));

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

            if (!(symbol is SymModule) && !(symbol is SymType) && !(symbol is SymTypeParam))
            {
                Reject(typeExpr.Token, "The symbol is not a type, it is a " + symbol.Kind);
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
            if (symbol is SymTypeParam symTypeParam)
            {
                return mSymbols.GetGenericParam(symTypeParam.GenericParamNum());
            }

            // Type inference: Add implied types to inner types found in this scope
            // e.g: InnerType => OuterType<T>.InnerType
            if (!isDot && symbol is SymType && foundInScope)
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
                if (!(leftSymbol is SymModule) && !(leftSymbol is SymType) && !(leftSymbol is SymSpecializedType))
                {
                    Reject(typeExpr.Token, $"The left side of the '.' must evaluate to a module or type, but it is a {leftSymbol.Kind}");
                    return null;
                }

                // The right side of the "." is only a type name identifier, excluding generic parameters.
                var leftScope = leftSymbol is SymSpecializedType ? leftSymbol.Parent : leftSymbol;
                var rightSymbol = ResolveTypeOrReject(typeExpr[1], true, leftScope, useScope, file, hasGenericParams);
                if (rightSymbol == null)
                    return null;

                if (!(rightSymbol is SymModule) && !(rightSymbol is SymType) && !(rightSymbol is SymSpecializedType))
                {
                    Reject(typeExpr[1].Token, $"The right side of the '.' must evaluate to a module or type, but it is a {rightSymbol.Kind}");
                    return null;
                }

                if (leftSymbol is SymSpecializedType pt)
                    return mSymbols.GetSpecializedType(rightSymbol, pt.Params);

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
                    if (sym is SymType || sym is SymTypeParam || sym is SymSpecializedType)
                    {
                        paramTypes.Add(sym);
                        var newMethodParam = new SymMethodParam(paramScope, file, pType.Token, isReturn);
                        newMethodParam.Qualifiers = isReturn ? sQualifiersParamReturn : sQualifiersParam;
                        newMethodParam.Token.AddInfo(newMethodParam);
                        mSymbols.AddOrReject(newMethodParam); // TBD: Fix
                    }
                    else
                    {
                        Reject(sym.Token, $"Expecting a type, but got a {sym.Kind}");
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
                var concreteType = typeParent is SymSpecializedType ? typeParent.Parent : typeParent;
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
                    else if (!(typeParent is SymType || typeParent is SymTypeParam || typeParent is SymSpecializedType))
                    {
                        Reject(typeExpr[typeParamIndex].Token, $"Expecting a type, but got a {typeParent.Kind}");
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
                    else if (sym is SymType || sym is SymTypeParam || sym is SymSpecializedType)
                        typeParams.Add(sym);
                    else
                    {
                        Reject(typeExpr[typeParamIndex].Token, $"Expecting a type, but got a {sym.Kind}");
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
            Debug.Assert(symbol is SymType);
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
            if (symbol is SymModule || symbol is SymType || symbol is SymTypeParam || symbol is SymSpecializedType)
                return symbol;
            Reject(name, $"'{name}' is not a type, it is a '{symbol.Kind}'");
            return null;
        }

        /// <summary>
        /// Find a type or module in the current scope, excluding $extension.
        /// If it's not found, scan use statements for all occurences. 
        /// Marks an error if undefined or duplicate.  Returns null on error.
        /// TBD: If symbol is unique in this package, but duplicated in an
        /// external package, is that an error?  Yes for now.
        /// </summary>
        public Symbol FindGlobalTypeOrReject(Token name, Symbol scope, string[] use, out bool foundInScope)
        {
            var symbol = FindTypeInScope(name.Name, scope);
            if (symbol != null)
            {
                foundInScope = true;
                return symbol;
            }
            foundInScope = false;

            var symbols = new List<Symbol>(); // TBD: Be kind to GC
            foreach (var u in use)
            {
                var ns = mSymbols.Lookup(u) as SymModule;
                Debug.Assert(ns != null);
                if (ns != null
                    && ns.Children.TryGetValue(name.Name, out var s)
                    && (s is SymModule || s is SymType || s is SymTypeParam || s is SymSpecializedType))
                {
                    symbols.Add(s);
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
                if (scope.Children.TryGetValue(name, out var s1)
                        && (s1 is SymModule || s1 is SymType || s1 is SymTypeParam || s1 is SymSpecializedType))
                    return s1;
                scope = scope.Parent;
            }
            if (scope.Children.TryGetValue(name, out var s2)
                    && (s2 is SymModule || s2 is SymType || s2 is SymTypeParam || s2 is SymSpecializedType))
                return s2;

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
