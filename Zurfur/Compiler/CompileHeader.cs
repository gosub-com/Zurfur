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

    class CompilerHeaderOutput
    {
        public Dictionary<string, FileUseSymbols> FileUses;
        public SymbolTable Table;
        public Dictionary<SyntaxScope, Symbol> SyntaxToSymbol;
    }

 
    static class CompileHeader
    {
        const string ZURFUR_PRELUDE = "void bool i8 u8 byte i16 u16 i32 u32 i64 int u64 f32 f64 xint object str List Map Array Buffer Span";

        static public CompilerHeaderOutput GenerateHeader(
            Dictionary<string, SyntaxFile> syntaxFiles,
            bool noCompilerChecks)
        {
            SymbolTable table = new SymbolTable();
            Dictionary<SyntaxScope, Symbol> syntaxToSymbol = new Dictionary<SyntaxScope, Symbol>();
            table.NoCompilerChecks = noCompilerChecks; // TBD: Move to compiler options class

            // Find a symbol for the type or module syntax
            AddModules();
            table.GenerateLookup();
            var fileUses = ProcessUseStatements();
            AddTypes();
            table.GenerateLookup();
            ResolveFields();
            ResolveMethods();
            ResolveTypeConstraints();
            table.GenerateLookup();
            CheckUseStatements();
            return new CompilerHeaderOutput { FileUses = fileUses, Table = table, SyntaxToSymbol = syntaxToSymbol};

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

            Dictionary<string, FileUseSymbols> ProcessUseStatements()
            {
                var uses = new Dictionary<string, FileUseSymbols>();
                foreach (var syntaxFile in syntaxFiles)
                {
                    var fileUseSymbols = new FileUseSymbols();

                    // Add prelude
                    if (table.Root.TryGetPrimary("Zurfur", out var zSym) && zSym is SymModule zMod)
                    {
                        foreach (var name in ZURFUR_PRELUDE.Split(' '))
                        {
                            fileUseSymbols.AddSymbol(name, zMod);
                        }
                    }

                    // Process use statements
                    foreach (var use in syntaxFile.Value.Using)
                    {
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
                        var module = table.FindTypeInPathOrReject(use.ModuleName);
                        if (module == null || use.ModuleName.Length == 0)
                            continue;  // Error marked above
                        if (!module.IsModule)
                            continue; // Error marked above

                        foreach (var token in use.Symbols)
                        {
                            bool found = false;
                            if (module.TryGetPrimary(token, out var symbol))
                            {
                                if (symbol.IsAnyType)
                                    token.Type = eTokenType.TypeName;
                                token.AddInfo(symbol);
                                found = true;
                            }
                            if (module.HasMethodNamed(token))
                            {
                                foreach (var method in module.ChildrenFilter(SymKind.Method, token))
                                    token.AddInfo(method);
                                found = true;
                            }
                            if (!found)
                                Reject(token, $"Symbol not found in the module '{module.FullName}'");
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
                        if (!syntaxToSymbol.TryGetValue(type.Parent, out var parent))
                            continue; // Syntax errors
                        var newType = new SymType(parent, type.Name);
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
                    var typeParam = new SymTypeParam(scope, expr.Token);
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
                        // TBD: Maybe convert const fields to get methods

                        if (!syntaxToSymbol.TryGetValue(field.Parent, out var symParent))
                        {
                            Reject(field.Name, $"Symbol not processed because the parent scope has an error");
                            continue;
                        }

                        // Create the field
                        var symField = new SymField(symParent, field.Name);
                        symField.SetQualifiers(field.Qualifiers);
                        symField.Comments = field.Comments;
                        symField.Token.AddInfo(symField);
                        table.AddOrReject(symField);

                        if (field.Parent != null && field.Parent.Keyword == "enum")
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
                    var constrainedType = ResolveType.FindTypeInScopeWalk(name, scope);
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
                        var sym = ResolveTypeNameOrReject(constrainedType.Name == "This" 
                                                            ? scope : constrainedType, c);
                        if (sym == null)
                            continue;  // Error already given
                        if (!sym.IsInterface)
                        {
                            // TBD: This should be in verification.
                            ResolveType.RejectTypeArgLeftDotRight(c, table, $"Symbol is not an interface, it is a {sym.KindName}");
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

            void ResolveMethods()
            {
                foreach (var syntaxFile in syntaxFiles)
                    foreach (var synFunc in syntaxFile.Value.Methods)
                        ResolveMethod(synFunc);
            }

            // Scope is where the function is defined (a module or type)
            void ResolveMethod(SyntaxFunc synFunc)
            {
                // Get module containing function
                if (!syntaxToSymbol.TryGetValue(synFunc.Parent, out var scope))
                    return; // Syntax errors

                Debug.Assert(scope.IsModule  || scope.IsType);
                if (synFunc.MethodSignature.Count != 3)
                {
                    Reject(synFunc.Name, "Syntax error or compiler error");
                    return;
                }

                // Give each function a unique name (final name calculated below)
                var method = new SymMethod(scope, synFunc.Name, $"$LOADING...${scope.ChildrenCount}");
                method.SetQualifiers(synFunc.Qualifiers);
                method.Comments = synFunc.Comments;

                AddExtensionMethodGenerics(method, synFunc);
                AddTypeParams(method, synFunc.TypeArgs);
                AddMyParam(method, synFunc);
                ResolveMethodParams(method, synFunc.MethodSignature[0], false); // Parameters
                ResolveMethodParams(method, synFunc.MethodSignature[1], true);  // Returns

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
                var mp = method.ChildrenFilter(SymKind.MethodParam).Where(child => !child.ParamOut).ToList();
                var mpr = method.ChildrenFilter(SymKind.MethodParam).Where(child => child.ParamOut).ToList();
                mp.Sort((a, b) => a.Order.CompareTo(b.Order));
                mpr.Sort((a, b) => a.Order.CompareTo(b.Order));
                var methodName = synFunc.Name
                            + (genericsCount == 0 ? "" : "`" + genericsCount)
                            + "(" + string.Join(",", mp.ConvertAll(a => a.TypeName)) + ")"
                            + "(" + string.Join(",", mpr.ConvertAll(a => a.TypeName)) + ")";
                method.SetName(methodName);
                table.AddOrReject(method);

                ResolveConstraints(method, synFunc.Constraints);

                Debug.Assert(!syntaxToSymbol.ContainsKey(synFunc));
                syntaxToSymbol[synFunc] = method;

            }

            // For now, extension methods with generic receivers
            // allow only 1 level deep with all type parameters matching:
            //      (List<int>) f(x)        // Ok, no generic types
            //      (List<T>) f(x)          // Ok, 1 level, matching generic
            //      (Map<TKey,TValue>) f(x) // Ok, 1 level, all matching generic
            //      (Map<Key,int>) f(x)     // No, not all matching genrics
            //      (Span<List<T>>) f(x)    // No, multi-level not accepted
            //
            // TBD: Allow Map<K,V>.Pair, etc.
            void AddExtensionMethodGenerics(Symbol method, SyntaxFunc f)
            {
                if (f.ExtensionType == null)
                    return;

                var extensionType = f.ExtensionType;
                method.Qualifiers |= SymQualifiers.Extension;

                // Skip qualifiers. 
                while (extensionType.Token == "mut" && extensionType.Count != 0)
                    extensionType = extensionType[0];

                // TBD: Follow "."
                if (extensionType.Token != ParseZurf.VT_TYPE_ARG || extensionType.Count < 2)
                    return;

                var typeName = extensionType[0].Token;
                var typeSymbol = ResolveType.FindGlobalTypeOrReject(typeName, table, method, fileUses[typeName.Path], out var inScope);
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
                    AddTypeParams(method, extensionType.Skip(1));
                else if (firstMatchedType != null)
                    Reject(firstMatchedType, $"Type parameter '{firstMatchedType}' found in '{typeName}', but other type parameters don't match.");
            }
            

            // Add $my parameter for extension methods and member functions.
            // NOTE: Even static methods get $this, but it is used as a
            //       type name and not passed as a parameter
            void AddMyParam(Symbol method, SyntaxFunc func)
            {
                var extType = func.ExtensionType;
                if (extType == null || extType.Token == "")
                    return;

                var methodParam = new SymMethodParam(method, func.Name, "my");
                methodParam.Type = ResolveTypeNameOrReject(methodParam, extType);
                method.Qualifiers |= SymQualifiers.Extension;
                if (methodParam.TypeName == "" && !noCompilerChecks)
                    methodParam.Token.AddInfo(new VerifySuppressError());
                table.AddOrReject(methodParam);
            }
            

            void ResolveMethodParams(Symbol method, SyntaxExpr parameters, bool isReturn)
            {
                if (parameters is SyntaxError)
                    return;

                foreach (var expr in parameters)
                {
                    if (expr is SyntaxError)
                        continue;
                    Debug.Assert(expr.Count >= 2);

                    var token = expr.Token == "" ? expr[1].Token : expr.Token;
                    var name = expr.Token == "" ? "$return" : expr.Token.Name;
                    var methodParam = new SymMethodParam(method, token, name);
                    methodParam.ParamOut = isReturn;
                    methodParam.Type = ResolveTypeNameOrReject(methodParam, expr[0]);
                    if (methodParam.TypeName == "" && !noCompilerChecks)
                        methodParam.Token.AddInfo(new VerifySuppressError());
                    if (methodParam.TypeName != "" && expr.Token.Name != "")
                        expr.Token.AddInfo(methodParam);
                    table.AddOrReject(methodParam);
                }
            }

            Symbol ResolveTypeNameOrReject(Symbol scope, SyntaxExpr typeExpr)
            {
                // There will also be a syntax error
                if (typeExpr == null || typeExpr.Token.Name == "")
                    return null;
                var symbol = ResolveType.ResolveTypeOrReject(typeExpr, table, false, scope, fileUses[typeExpr.Token.Path]);
                if (symbol == null)
                    return null;

                if (symbol.IsAnyTypeNotModule  || noCompilerChecks)
                    return symbol;

                ResolveType.RejectTypeArgLeftDotRight(typeExpr, table, "The symbol is not a type, it is a " + symbol.KindName);
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
