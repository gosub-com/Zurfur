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

        // TBD: Still figuring out how to deal with these.
        Dictionary<string, SymParameterizedType> mSpecializedTypes = new Dictionary<string, SymParameterizedType>();

        public SymbolTable Symbols => mSymbols;

        static WordSet sPropertyQualifiers = new WordSet("get aget set aset");

        public ZilGenHeader()
        {
            // Add built in unary generic types
            foreach (var genericType in "* ^ [ ? ref own mut ro".Split(' '))
            {
                SymType sym = AddRootType(genericType, 1);
                mUnaryTypeSymbols[genericType] = sym;
            }
        }

        private SymType AddRootType(string type, int numGenerics)
        {
            var sym = new SymType(mSymbols.Root, type);
            mSymbols.AddOrReject(sym);
            for (int i = 0; i < numGenerics; i++)
            {
                var tn = "T" + (numGenerics == 1 ? "" : $"{i + 1}");
                mSymbols.AddOrReject(new SymTypeParam(sym, "", new Token(tn)));
            }
            return sym;
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
            // Does not require symbols from external packages
            Dictionary<string, string[]> fileUses = new Dictionary<string, string[]>();
            AddModules();
            mSymbols.GenerateLookup();
            AddTypes();
            AddFields();
            AddMethodGroups();

            // This requires symbols from external packages
            ProcessUseStatements();
            ResolveFieldTypes();
            ResolveMethodGroups();
            mSymbols.GenerateLookup();
            mSymbols.AddSpecializations(mSpecializedTypes);
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
                        newModule.Qualifiers = new string[] { "pub", "module" }; // TBD: Allow private/internal
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
                        var newType = new SymType(mSymbols.FindPath(type.ModulePath), syntaxFile.Key, type.Name);
                        newType.Comments = type.Comments;
                        newType.Qualifiers = Array.ConvertAll(type.Qualifiers, a => a.Name).Append("type").ToArray();
                        newType.Token.AddInfo(newType);
                        if (mSymbols.AddOrReject(newType))
                        {
                            // Add type arguments
                            foreach (var expr in type.TypeArgs)
                            {                                
                                var newTypeParam = new SymTypeParam(newType, syntaxFile.Key, expr.Token);
                                newTypeParam.Token.AddInfo(newTypeParam);
                                mSymbols.AddOrReject(newTypeParam);
                            }
                        }
                    }
                }
            }

            void AddFields()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var field in syntaxFile.Value.Fields)
                    {
                        var newField = new SymField(mSymbols.FindPath(field.ModulePath), syntaxFile.Key, field.Name);
                        newField.Qualifiers = Array.ConvertAll(field.Qualifiers, a => a.Name);
                        newField.Comments = field.Comments;
                        newField.Token.AddInfo(newField);
                        var scopeParent = field.ModulePath[field.ModulePath.Length-1];
                        if (scopeParent.Error)
                        {
                            Warning(newField.Token, $"Symbol not processed because '{scopeParent}' has an error");
                            continue;
                        }
                        mSymbols.AddOrReject(newField);
                    }
                }
            }

            void AddMethodGroups()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var method in syntaxFile.Value.Methods)
                    {
                        var scope = mSymbols.FindPath(method.ModulePath);

                        // Move extensions to $ext type
                        var isExtension = method.ExtensionType != null && method.ExtensionType.Token != "";
                        if (isExtension)
                        {
                            if (!scope.Children.TryGetValue("$ext", out var extensionScope))
                            {
                                extensionScope = new SymType(scope, "$ext");
                                extensionScope.Qualifiers = new string[] { "pub", "type", "ext" };
                                if (!mSymbols.AddOrReject(extensionScope))
                                    Debug.Assert(false);  // Can't fail
                            }
                            scope = extensionScope;
                        }

                        if (!scope.Children.ContainsKey(method.Name))
                        {
                            var newMethod = new SymMethodGroup(scope, method.Name.Name);
                            mSymbols.AddOrReject(newMethod);
                        }
                    }
                }
            }

            void ProcessUseStatements()
            {
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
                    fileUses[syntaxFile.Key] = useModules.ToArray();
                }
            }

            void ResolveFieldTypes()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var field in syntaxFile.Value.Fields)
                    {
                        if (field.ModulePath[field.ModulePath.Length - 1].Error)
                            continue; // Warning given by AddFields

                        var symField = mSymbols.FindPath(field.ModulePath).Children[field.Name] as SymField;
                        if (symField == null)
                        {
                            Reject(field.Name, "Compiler error"); // Shouldn't happen
                            continue;
                        }
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
                    foreach (var func in syntaxFile.Value.Methods)
                    {
                        var scope = mSymbols.FindPath(func.ModulePath);

                        // Extension functions are located in $ext
                        if (func.ExtensionType != null && func.ExtensionType.Token != "")
                            scope = scope.Children["$ext"];

                        var group = scope.Children[func.Name];
                        if (!(group is SymMethodGroup))
                        {
                            Reject(func.Name, "Duplicate symbol.  There is a " + group.Kind + $" with the same name as '{group.FullName}'");
                            func.Name.AddInfo(group);
                        }
                        else
                        {
                            ResolveMethodTypes(group as SymMethodGroup, func, syntaxFile.Key);
                        }
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
                method.Qualifiers = Array.ConvertAll(func.Qualifiers, a => a.Name);
                method.Comments = func.Comments;

                // Add type arguments
                foreach (var expr in func.TypeArgs)
                {
                    var typeSym = new SymTypeParam(method, file, expr.Token);
                    if (mSymbols.AddOrReject(typeSym))
                        expr.Token.AddInfo(typeSym);
                }

                var isExtension = func.ExtensionType != null && func.ExtensionType.Token != "";
                if (isExtension)
                {
                    // Resolve extension method type name (first parameter is "$this")
                    var methodParam = new SymMethodParam(method, file, new Token("$this"), false);
                    methodParam.TypeName = ResolveTypeNameOrReject(methodParam, func.ExtensionType, file);
                    if (methodParam.TypeName == "" && !NoCompilerChecks)
                        methodParam.Token.AddInfo(new VerifySuppressError());
                    mSymbols.AddOrReject(methodParam); // Extension method parameter name "$this" is unique
                }
                else if (!method.Qualifiers.Contains("static")) // TBD: Check if in type instead of module
                {
                    // Non static method (first parameter is "$this")
                    var methodParam = new SymMethodParam(method, file, new Token("$this"), false);
                    if (scope.Parent is SymType tn)
                    {
                        methodParam.TypeName = tn.ToString();
                        mSymbols.AddOrReject(methodParam);  // Method parameter "$this" is unique
                    }
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
                var mp = method.FindChildren<SymMethodParam>();
                mp.Sort((a, b) => a.Order.CompareTo(b.Order));
                var xParams = mp.FindAll(a => !a.IsReturn).ToArray();
                var xReturns = mp.FindAll(a => a.IsReturn).ToArray();
                var methodName = (genericsCount == 0 ? "" : "`" + genericsCount)
                            + "(" + string.Join(",", Array.ConvertAll(xParams, a => a.TypeName)) + ")"
                            + "(" + string.Join(",", Array.ConvertAll(xReturns, a => a.TypeName)) + ")";
                method.SetName(methodName);
                mSymbols.AddOrReject(method);
            }

            void ResolveMethodParams(string file, SymMethod method, SyntaxExpr parameters, bool isReturn)
            {
                if (parameters is SyntaxError)
                    return;

                foreach (var expr in parameters)
                {
                    if (expr is SyntaxError)
                        continue;

                    var name = expr.Token == "" ? new Token("$return") : expr.Token;
                    var methodParam = new SymMethodParam(method, file, name, isReturn);
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

                if (symbol is SymType || symbol is SymTypeParam || symbol is SymParameterizedType || NoCompilerChecks)
                    return symbol.FullName;

                Reject(typeExpr.Token, "The symbol is not a type, it is a " + symbol.Kind);
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
            Debug.Assert(!(scope is SymParameterizedType));

            if (typeExpr.Token.Name == ".")
                return ResolveDotOrReject();

            if (typeExpr.Token == "fun" || typeExpr.Token == "afun")
                return ResolveFunOrReject();

            if (mUnaryTypeSymbols.ContainsKey(typeExpr.Token) || typeExpr.Token == ParseZurf.VT_TYPE_ARG)
                return ResolveGenericTypeOrReject();

            // Resolve regular symbol
            bool foundInScope = false;
            var symbol = isDot ? FindLocalOrReject(typeExpr.Token, scope)
                               : FindGlobalOrReject("type name", typeExpr.Token, scope, useScope, out foundInScope);
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
            if (symbol is SymTypeParam)
            {
                var totalParamsAbove = symbol.Parent.Parent.GenericParamTotal();
                var argNum = totalParamsAbove + symbol.Order;
                return GetGenericParam(argNum);
            }

            // Type inference: Add implied types to inner types
            // e.g: InnerType => OuterType<T>.InnterType
            if (!isDot && symbol is SymType && symbol.Parent is SymType && foundInScope)
            {
                var genericParamCount = symbol.Parent.GenericParamTotal();
                if (genericParamCount != 0)
                {
                    var genericParams = new List<Symbol>();
                    for (int i = 0; i < genericParamCount; i++)
                        genericParams.Add(GetGenericParam(i));
                    symbol = new SymParameterizedType(symbol, genericParams.ToArray());
                }
            }

            return symbol;

            SymParameterizedType GetGenericParam(int argNum)
            {
                var name = "#" + argNum;
                if (mSpecializedTypes.ContainsKey(name))
                    return mSpecializedTypes[name];
                var spec = new SymParameterizedType(mSymbols.Root, name);
                mSpecializedTypes[name] = spec;
                return spec;
            }

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
                if (!(leftSymbol is SymModule) && !(leftSymbol is SymType) && !(leftSymbol is SymParameterizedType))
                {
                    Reject(typeExpr.Token, $"The left side of the '.' must evaluate to a module or type, but it is a {leftSymbol.Kind}");
                    return null;
                }

                // The right side of the "." is only a type name identifier, excluding generic parameters.
                var leftScope = leftSymbol is SymParameterizedType ? leftSymbol.Parent : leftSymbol;
                var rightSymbol = ResolveTypeOrReject(typeExpr[1], true, leftScope, useScope, file, hasGenericParams);
                if (rightSymbol == null)
                    return null;

                if (!(rightSymbol is SymModule) && !(rightSymbol is SymType) && !(rightSymbol is SymParameterizedType))
                {
                    Reject(typeExpr[1].Token, $"The right side of the '.' must evaluate to a module or type, but it is a {rightSymbol.Kind}");
                    return null;
                }

                if (leftSymbol is SymParameterizedType pt)
                    return new SymParameterizedType(rightSymbol, pt.Params);

                return rightSymbol;
            }


            // Resolve "fun" or "afun" types
            Symbol ResolveFunOrReject()
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
                var funParamsScope = new SymMethod(scope, "", new Token("$unused"), "$unused");

                var paramTypes = new List<Symbol>();
                var returnTypes = new List<Symbol>();
                var resolved1 = ResolveTypeFunParamsOrReject(funParamsScope, typeExpr[0], paramTypes, false);
                var resolved2 = ResolveTypeFunParamsOrReject(funParamsScope, typeExpr[1], returnTypes, true);
                if (!resolved1 || !resolved2)
                    return null;

                // TBD: Figure out what to do with this (error/exit attribute)
                //if (typeExpr[2].Token.Name != "") // error attribute
                //    returnTypes.Add(typeExpr[2].Token.Name);

                // Add generic "$fun#" symbol to root, where # is the number of generic arguments
                var name = "$" + typeExpr.Token.Name + (paramTypes.Count + returnTypes.Count);
                if (!mSymbols.Root.Children.TryGetValue(name, out var genericFunType))
                {
                    genericFunType = AddRootType(name, paramTypes.Count + returnTypes.Count);
                }

                var typeParent = genericFunType;
                var spec = new SymParameterizedType(typeParent, paramTypes.ToArray(), returnTypes.ToArray());

                // Return the one in the symbol table, if it exists
                if (mSpecializedTypes.TryGetValue(spec.FullName, out var specExists))
                    spec = specExists;
                else
                    mSpecializedTypes[spec.FullName] = spec;
                return spec;
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
                    if (sym is SymType || sym is SymTypeParam || sym is SymParameterizedType)
                    {
                        paramTypes.Add(sym);
                        var newMethodParam = new SymMethodParam(paramScope, file, pType.Token, isReturn);
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
                var concreteType = typeParent is SymParameterizedType ? typeParent.Parent : typeParent;
                if (concreteType.GenericParamCount() != typeParams.Count)
                {
                    var errorToken = typeExpr[0].Token;
                    if (errorToken.Name == "." && typeExpr[0].Count >= 2)
                        errorToken = typeExpr[0][1].Token; // Put error after "." (e.g. 'List' in 'Zurfur.List<int,int>')

                    Reject(errorToken, $"Expecting {concreteType.GenericParamCount()} generic parameter(s), but got {typeParams.Count}");
                    if (!NoCompilerChecks)
                        return null;
                }

                if (typeParent is SymParameterizedType pt)
                {
                    Debug.Assert(pt.Returns.Length == 0); // TBD: Fix for functions
                    for (int i = 0; i < pt.Params.Length; i++)
                        typeParams.Insert(i, pt.Params[i]);
                }

                // Return the one in the symbol table, if it exists
                var sym = new SymParameterizedType(concreteType, typeParams.ToArray());
                if (mSpecializedTypes.TryGetValue(sym.FullName, out var specExists))
                    sym = specExists;
                else
                    mSpecializedTypes[sym.FullName] = sym;
                return sym;
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
                    else if (!(typeParent is SymType || typeParent is SymTypeParam || typeParent is SymParameterizedType))
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
                    else if (sym is SymType || sym is SymTypeParam || sym is SymParameterizedType)
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
        /// Find a symbol at the current scope.
        /// Return null if not found.
        /// </summary>
        public Symbol FindLocalOrReject(Token name, Symbol scope)
        {
            if (scope.Children.TryGetValue(name, out var symbol))
                return symbol;
            Reject(name, $"'{name}' is not a member of '{scope}'");
            return null;
        }

        /// <summary>
        /// Find a symbol in the current scope, excluding $ext.
        /// If it's not found, scan use statements for all occurences. 
        /// Marks an error if undefined or duplicate.  Returns null on error.
        /// TBD: If symbol is unique in this package, but duplicated in an
        /// external package, is that an error?  Yes for now.
        /// </summary>
        public Symbol FindGlobalOrReject(string symbolType, Token name, Symbol scope, string[] use, out bool foundInScope)
        {
            var symbol = FindInScopeNoExtension(name.Name, scope);


            if (symbol != null)
            {
                foundInScope = true;
                return symbol;
            }
            foundInScope = false;

            var symbols = new List<Symbol>(); // TBD: Be kind to GC
            foreach (var u in use)
            {
                var ns = mSymbols.LookupModule(u);
                Debug.Assert(ns != null);
                if (ns != null
                    && ns.Children.TryGetValue(name.Name, out var newSymbol))
                {
                    symbols.Add(newSymbol);
                }
            }

            if (symbols.Count == 0)
            {
                Reject(name, "Undefined " + symbolType);
                return null;
            }
            if (symbols.Count > 1)
            {
                Reject(name, "Multiple symbols defined.  Found in '" + symbols[0].File
                    + "' and '" + symbols[1].File + "'");
                return null;
            }
            return symbols[0];
        }

        /// <summary>
        /// Find the symbol in the scope, excluding $ext, or null if it is not found.
        /// Does not search use statements.
        /// </summary>
        public Symbol FindInScopeNoExtension(string name, Symbol scope)
        {
            while (scope.Name != "")
            {
                if (scope.Name != "$ext" && scope.Children.TryGetValue(name, out var symbol))
                    return symbol;
                scope = scope.Parent;
            }
            if (scope.Children.TryGetValue(name, out var symbol2))
                return symbol2;

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
