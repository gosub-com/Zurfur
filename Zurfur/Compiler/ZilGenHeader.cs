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
        static WordSet sTypeAttributes = new WordSet("in out mut ro ref");

        bool mNoCompilerChecks;
        SymbolTable mSymbols = new SymbolTable();
        Dictionary<string, SymType> mUnaryTypeSymbols = new Dictionary<string, SymType>();
        //SymType mUnresolvedType;

        // TBD: Still figuring out how to deal with these.
        Dictionary<string, SymParameterizedType> mSpecializedTypes = new Dictionary<string, SymParameterizedType>();

        public SymbolTable Symbols => mSymbols;

        public ZilGenHeader()
        {
            //mUnresolvedType = new SymType(mSymbols.Root, "$unresolved");
            //mSymbols.AddOrReject(mUnresolvedType);

            // Add built in unary generic types
            foreach (var genericType in "* ^ [ ? fun afun ref".Split(' '))
            {
                var sym = new SymType(mSymbols.Root, genericType);
                mUnaryTypeSymbols[genericType] = sym;
                mSymbols.AddOrReject(sym);
                mSymbols.AddOrReject(new SymTypeParam(sym, "", new Token("T")));
            }

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
            AddNamespaces();
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

            void AddNamespaces()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var ns in syntaxFile.Value.Namespaces)
                    {
                        var symbol = AddNamespace(syntaxFile.Key, ns.Value.Path);
                        symbol.Comments += " " + ns.Value.Comments;
                    }
                }
            }

            // Add the namespace, or return the one we already have
            Symbol AddNamespace(string file, Token[] path)
            {
                var childrenSymbols = mSymbols.Root.Children;
                Symbol parentSymbol = mSymbols.Root;
                Symbol newNamespace = null;
                foreach (var token in path)
                {
                    if (childrenSymbols.TryGetValue(token.Name, out var childNamespace))
                    {
                        Debug.Assert(childNamespace is SymNamespace);
                        newNamespace = childNamespace;
                        newNamespace.AddLocation(file, token);
                    }
                    else
                    {
                        newNamespace = new SymNamespace(parentSymbol, file, token);
                        newNamespace.Token.AddInfo(newNamespace);
                        mSymbols.AddOrReject(newNamespace);
                    }

                    parentSymbol = newNamespace;
                    childrenSymbols = newNamespace.Children;
                }
                return newNamespace;
            }

            void AddTypes()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var type in syntaxFile.Value.Types)
                    {
                        var newType = new SymType(mSymbols.FindPath(type.NamePath), syntaxFile.Key, type.Name);
                        newType.Comments = type.Comments;
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
                        var newField = new SymField(mSymbols.FindPath(field.NamePath), syntaxFile.Key, field.Name);
                        newField.Qualifiers = Array.ConvertAll(field.Qualifiers, a => a.Name);
                        newField.Comments = field.Comments;
                        newField.Token.AddInfo(newField);
                        var scopeParent = field.NamePath[field.NamePath.Length-1];
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
                        var parentSymbol = mSymbols.FindPath(method.NamePath);
                        if (!parentSymbol.Children.ContainsKey(method.Name))
                        {
                            var newMethod = new SymMethodGroup(parentSymbol, syntaxFile.Key, method.Name);
                            mSymbols.AddOrReject(newMethod);
                        }
                    }
                }
            }

            void ProcessUseStatements()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    var useNamespaces = new List<string>();
                    foreach (var use in syntaxFile.Value.Using)
                    {
                        var symbol = mSymbols.FindPathOrReject(use.NamePath);
                        if (symbol == null || use.NamePath.Length == 0)
                            continue;  // Error marked by function

                        var lastToken = use.NamePath[use.NamePath.Length - 1];
                        if (!(symbol is SymNamespace))
                        {
                            Reject(lastToken, "Must be a namespace, not a " + symbol.Kind);
                            continue;
                        }

                        if (useNamespaces.Contains(symbol.GetFullName()))
                        {
                            Reject(lastToken, "Already included in previous use statement");
                            continue;
                        }
                        useNamespaces.Add(symbol.GetFullName());
                    }
                    fileUses[syntaxFile.Key] = useNamespaces.ToArray();
                }
            }

            void ResolveFieldTypes()
            {
                foreach (var syntaxFile in syntaxFiles)
                {
                    foreach (var field in syntaxFile.Value.Fields)
                    {
                        if (field.NamePath[field.NamePath.Length - 1].Error)
                            continue; // Warning given by AddFields

                        var symField = mSymbols.FindPath(field.NamePath).Children[field.Name] as SymField;
                        if (symField == null)
                        {
                            Reject(field.Name, "Compiler error"); // Shouldn't happen
                            continue;
                        }
                        // Skip enum and errors
                        if (field.TypeName == null || field.TypeName.Token.Name == "")
                        {
                            if (field.ParentScope !=null && field.ParentScope.Keyword == "enum")
                                Warning(field.Name, "TBD: Process enum");
                            else
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
                        var group = mSymbols.FindPath(func.NamePath).Children[func.Name];
                        if (!(group is SymMethodGroup))
                        {
                            Reject(func.Name, "Duplicate symbol.  There is a " + group.Kind + $" with the same name as '{group.GetFullName()}'");
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
                if (func.IsProperty)
                {
                    Warning(func.Name, "TBD: Property not processed yet");
                    return;
                }
                if (func.MethodSignature.Count != 3)
                {
                    Reject(func.Name, "Syntax error or compiler error");
                    return;
                }

                // Give each function a unique name (final name calculated below)
                var method = new SymMethod(scope, $"#LOADING...#{scope.Children.Count}");
                method.Qualifiers = Array.ConvertAll(func.Qualifiers, a => a.Name);
                method.AddLocation(file, func.Name);
                method.Comments = func.Comments;

                // Add type arguments
                foreach (var expr in func.TypeArgs)
                {
                    var typeSym = new SymTypeParam(method, file, expr.Token);
                    mSymbols.AddOrReject(typeSym);
                }

                if (func.ExtensionType != null && func.ExtensionType.Token != "")
                {
                    // Resolve extension method type name (first parameter is "$this_ex")
                    var methodParam = new SymMethodParam(method, file, new Token("$this_ex"));
                    methodParam.TypeName = ResolveTypeNameOrReject(methodParam, func.ExtensionType, file);
                    if (methodParam.TypeName == "" && !NoCompilerChecks)
                        methodParam.Token.AddInfo(new VerifySuppressError());
                    mSymbols.AddOrReject(methodParam); // Extension method parameter name "$this_ex" is unique
                }
                else if (!method.Qualifiers.Contains("static")) // TBD: Check if in type instead of namespace
                {
                    // Non static method (first parameter is "$this")
                    var methodParam = new SymMethodParam(method, file, new Token("$this"));
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
                var scopeParent = func.NamePath[func.NamePath.Length - 1];
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
                    var methodParam = new SymMethodParam(method, file, name);
                    methodParam.IsReturn = isReturn;
                    methodParam.TypeName = ResolveTypeNameOrReject(methodParam, expr[0], file);
                    if (methodParam.TypeName == "" && !NoCompilerChecks)
                        methodParam.Token.AddInfo(new VerifySuppressError());
                    mSymbols.AddOrReject(methodParam);
                }
            }

            string ResolveTypeNameOrReject(Symbol scope, SyntaxExpr typeExpr, string file)
            {
                var symbol = ResolveTypeOrReject(typeExpr, false, scope, fileUses[file], file);
                if (symbol == null)
                    return "";

                if (symbol is SymType || symbol is SymTypeParam || symbol is SymParameterizedType || NoCompilerChecks)
                    return symbol.GetFullName();

                Reject(typeExpr.Token, "The symbol is not a type, it is a " + symbol.Kind);
                return "";
            }

        }

        /// <summary>
        /// Resolve a type.  Non-generic types are found in the symbol table
        /// and given a full name (e.g. 'int' -> 'Zufur.int').  Generic types
        /// must have all type arguments resolved as well.
        /// Return symbol is always a namespace, type, or type parameter.
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

            if (sTypeAttributes.Contains(typeExpr.Token))
            {
                // TBD: Figure out what to do about the attributes.  For now skip them.
                //Warning(typeExpr.Token, "Attribute not processed yet");
                if (typeExpr.Count == 1)
                    return ResolveTypeOrReject(typeExpr[0], false, scope, useScope, file);
                Reject(typeExpr.Token, "Index error");
                return null;
            }

            // Resolve regular symbol
            bool foundInScope = false;
            var symbol = isDot ? mSymbols.FindAtScopeOrReject(typeExpr.Token, scope)
                               : mSymbols.FindInScopeOrUseOrReject("type name", typeExpr.Token, scope, useScope, out foundInScope);
            if (symbol == null)
                return null; // Error already marked

            if (!(symbol is SymNamespace) && !(symbol is SymType) && !(symbol is SymTypeParam))
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
                    return new SymParameterizedType(symbol, genericParams.ToArray());

                }
            }

            return symbol;

            SymParameterizedType GetGenericParam(int argNum)
            {
                var name = "!" + argNum;
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
                if (!(leftSymbol is SymNamespace) && !(leftSymbol is SymType) && !(leftSymbol is SymParameterizedType))
                {
                    Reject(typeExpr.Token, $"The left side of the '.' must evaluate to a namespace or type, but it is a {leftSymbol.Kind}");
                    return null;
                }

                // The right side of the "." is only a type name identifier, excluding generic parameters.
                var leftScope = leftSymbol is SymParameterizedType ? leftSymbol.Parent : leftSymbol;
                var rightSymbol = ResolveTypeOrReject(typeExpr[1], true, leftScope, useScope, file, hasGenericParams);
                if (rightSymbol == null)
                    return null;

                if (!(rightSymbol is SymType))
                {
                    Reject(typeExpr[1].Token, "Must be a concrete type (not a type parameter, parameterized type, or namespace)");
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

                var paramTypes = new List<Symbol>();
                var returnTypes = new List<Symbol>();
                var resolved1 = ResolveTypeFunParamsOrReject(typeExpr[0], paramTypes);
                var resolved2 = ResolveTypeFunParamsOrReject(typeExpr[1], returnTypes);
                if (!resolved1 || !resolved2)
                    return null;

                // TBD: Figure out what to do with this (error/exit attribute)
                //if (typeExpr[2].Token.Name != "") // error attribute
                //    returnTypes.Add(typeExpr[2].Token.Name);

                var typeParent = mUnaryTypeSymbols[typeExpr.Token.Name];
                var spec = new SymParameterizedType(typeParent, paramTypes.ToArray(), returnTypes.ToArray());

                // Return the one in the symbol table, if it exists
                if (mSpecializedTypes.TryGetValue(spec.GetFullName(), out var specExists))
                    spec = specExists;
                else
                    mSpecializedTypes[spec.GetFullName()] = spec;
                return spec;
            }

            // Resolve "fun" or "afun" parameter types
            bool ResolveTypeFunParamsOrReject(SyntaxExpr paramExprs, List<Symbol> paramTypes)
            {
                bool resolved = true;
                foreach (var pType in paramExprs)
                {
                    if (pType is SyntaxError)
                        continue;
                    var sym = ResolveTypeOrReject(pType[0], false, scope, useScope, file);
                    if (sym == null)
                    {
                        resolved = false;
                        continue;
                    }
                    if (sym is SymType || sym is SymTypeParam || sym is SymParameterizedType)
                    {
                        paramTypes.Add(sym);
                        var newMethodParam = new SymMethodParam(scope, file, pType.Token);
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
                if (mSpecializedTypes.TryGetValue(sym.GetFullName(), out var specExists))
                    sym = specExists;
                else
                    mSpecializedTypes[sym.GetFullName()] = sym;
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
