using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics;

using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    public class SilError : TokenError
    {
        public SilError(string message) : base(message) { }
    }

    class SilGenHeader
    {
        static WordSet sUnaryTypeSymbols = new WordSet("? * ^ [ mut ref in out ro");

        Dictionary<string, SymFile> mFiles = new Dictionary<string, SymFile>();
        SymbolTable mSymbols = new SymbolTable();

        List<string> mNamespaces = new List<string>();
        List<string> mAllSymbols = new List<string>();
        DateTime mStartTime = DateTime.Now;

        /// <summary>
        /// Step 1: GenerateTypeDefinitions, requires nothing from any other package.
        /// Step 2: ResolveTypeNames, requires type definitions from all other packages.
        /// Step 3: GenerateHeader.
        /// </summary>
        public SilGenHeader()
        {
        }


        /// <summary>
        /// Step 1: Find all the symbols in this package.
        /// Load namespaces, types, fields, and method group names.
        /// Each file is independent, requires nothing from other packages.
        /// </summary>
        public void EnumerateSymbols(Dictionary<string, SyntaxFile> syntaxFiles)
        {
            foreach (var syntaxFile in syntaxFiles)
                mFiles[syntaxFile.Key] = new SymFile(syntaxFile.Key, syntaxFile.Value);

            mSymbols[""] = new SymNamespace("", new SymFile("", new SyntaxFile()), new Token(""));

            AddNamespaces();
            AddTypes();
            AddFields();
            AddMethodGroups();
            VerifyNoSameParentSymbols();
            return;

            void AddNamespaces()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var ns in symFile.SyntaxFile.Namespaces)
                    {
                        var symbol = AddNamespace(symFile, ns.Value.Path);
                        symbol.Comments += " " + ns.Value.Comments;
                    }
                }
            }

            Symbol AddNamespace(SymFile file, Token[] path)
            {
                var childrenSymbols = mSymbols[""].Children;
                Symbol parentSymbol = mSymbols[""];
                Symbol newNamespace = null;
                foreach (var token in path)
                {
                    if (childrenSymbols.TryGetValue(token.Name, out var childNamespaceName))
                    {
                        newNamespace = mSymbols[childNamespaceName];
                        newNamespace.AddLocation(file, token);
                    }
                    else
                    {
                        newNamespace = new SymNamespace(parentSymbol.FullName, file, token);
                        childrenSymbols[token] = newNamespace.FullName;
                        mSymbols[newNamespace.FullName] = newNamespace;
                        parentSymbol.Children[token.Name] = newNamespace.FullName;
                    }
                    Debug.Assert(mSymbols[newNamespace.FullName] == newNamespace);

                    parentSymbol = newNamespace;
                    childrenSymbols = newNamespace.Children;
                }
                return newNamespace;
            }

            void AddTypes()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var type in symFile.SyntaxFile.Types)
                    {
                        var newType = new SymType(mSymbols.FindPath(type.NamePath).FullName, symFile, type.Name);
                        newType.TypeKeyword = type.Keyword.Name;
                        newType.Comments = type.Comments;
                        if (mSymbols.Add(newType) && type.TypeParams.Count != 0)
                        {
                            // Add type arguments
                            var typeArgs = new List<string>();
                            foreach (var expr in type.TypeParams)
                            {
                                mSymbols.Add(new SymTypeArg(newType.FullName, symFile, expr.Token));
                                typeArgs.Add(expr.Token.Name);
                            }
                            // TBD: We need to do this because we need to maintain the
                            //      parameter order.  It might be better to keep an ordered
                            //      list of names in the symbol table.  We will need that
                            //      if we want to maintain field order.
                            newType.TypeArgs = typeArgs.ToArray();
                        }
                    }
                }
            }

            void AddFields()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var field in symFile.SyntaxFile.Fields)
                    {
                        var newField = new SymField(mSymbols.FindPath(field.NamePath).FullName, symFile, field.Name);
                        newField.Syntax = field;
                        newField.Comments = field.Comments;
                        mSymbols.Add(newField);
                    }
                }
            }

            void AddMethodGroups()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var method in symFile.SyntaxFile.Methods)
                    {
                        var parentSymbol = mSymbols.FindPath(method.NamePath);
                        if (!parentSymbol.Children.ContainsKey(method.Name))
                        {
                            var newMethod = new SymMethods(parentSymbol.FullName, symFile, method.Name);
                            parentSymbol.Children[method.Name] = newMethod.FullName;
                            mSymbols[newMethod.FullName] = newMethod;
                        }
                    }
                }
            }

            void VerifyNoSameParentSymbols()
            {
                foreach (var symbol in mSymbols.Values)
                {
                    if (symbol.FullName != "" && symbol.Name == mSymbols[symbol.ParentName].Name)
                        Reject(symbol.Token, "Name must not be same as parent symbol");
                }
            }

        }

        /// <summary>
        /// Step 2: Resolve type names.  Requires symbols from external packages.
        /// TBD: Add way to include external package headers
        /// </summary>
        public void ResolveTypeNames()
        {
            ProcessUseStatements();
            MarkDuplicateFunctions();
            ResolveFieldTypes();
            BuildTypeFieldInfo();
            return;

            void ProcessUseStatements()
            {
                foreach (var symFile in mFiles.Values)
                {
                    var useNamespaces = new List<string>();
                    foreach (var use in symFile.SyntaxFile.Using)
                    {
                        var symbol = mSymbols.FindPath(use.NamePath);
                        if (symbol == null)
                            continue;  // Error marked by function

                        var lastToken = use.NamePath[use.NamePath.Length - 1];
                        if (!(symbol is SymNamespace))
                        {
                            Reject(lastToken, "Must be a namespace, not a " + symbol.Kind);
                            continue;
                        }

                        if (useNamespaces.Contains(symbol.FullName))
                        {
                            Reject(lastToken, "Already included in previous use statement");
                            continue;
                        }
                        useNamespaces.Add(symbol.FullName);
                    }
                    // TBD: Sort namespaces so packages are searched first
                    symFile.Use = useNamespaces.ToArray();
                }
            }


            void MarkDuplicateFunctions()
            {
                foreach (var symFile in mFiles.Values)
                {
                    foreach (var func in symFile.SyntaxFile.Methods)
                    {
                        var group = mSymbols[mSymbols.FindPath(func.NamePath).Children[func.Name.Name]];
                        if (!(group is SymMethods))
                        {
                            Reject(func.Name, "Duplicate symbol.  There is a " + group.Kind + " with the same name");
                            func.Name.AddInfo(group);
                        }
                    }
                }
            }

            void ResolveFieldTypes()
            {
                foreach (var sym in mSymbols.Values)
                {
                    if (!(sym is SymField field))
                        continue;

                    if (mSymbols[field.ParentName] as SymType == null)
                    {
                        Warning(field.Token, "Compiler error: Parent of field must be a type, namespace not implemented yet");
                        continue;
                    }
                    // Skip enum and errors
                    if (field.Syntax.TypeName == null || field.Syntax.TypeName.Token.Name == "")
                    {
                        if (field.Syntax.ParentScope.Keyword == "enum")
                            Warning(field.Syntax.Name, "TBD: Process enum");
                        else
                            Reject(field.Syntax.Name, "Expecting symbol to have an explicitly named type");
                        continue;
                    }
                    field.Type = ResolveType(field, field.Syntax.TypeName);
                    if (field.Type == null)
                    {
                        Warning(field.Token, "Error resolving symbol type (warning for now)");
                        continue;
                    }
                }
            }


            void BuildTypeFieldInfo()
            {
                var fields = new List<SymFieldInfo>();
                var constFields = new List<SymConstFieldInfo>();
                var staticFields = new List<SymFieldInfo>();

                foreach (var sym in mSymbols.Values)
                {
                    if (!(sym is SymType symType))
                        continue;

                    fields.Clear();
                    constFields.Clear();
                    staticFields.Clear();
                    foreach (var sym2Name in sym.Children.Values)
                    {
                        var sym2 = mSymbols[sym2Name];
                        if (!(sym2 is SymField symField))
                            continue;

                        if (symField.Type == null)
                        {
                            Warning(symField.Token, "Unresolved type name, compiler error should already have an error");
                            continue;
                        }

                        var syntax = symField.Syntax;
                        if (Array.Exists(syntax.Qualifiers, t => t.Name == "const"))
                        {
                            var constField = new SymConstFieldInfo();
                            constField.Type = symField.Type;
                            constField.Name = symField.Token;
                            constFields.Add(constField);
                        }
                        else
                        {
                            var field = new SymFieldInfo();
                            field.Type = symField.Type;
                            field.Name = symField.Token;
                            if (Array.Exists(syntax.Qualifiers, t => t.Name == "static"))
                                staticFields.Add(field);
                            else
                                fields.Add(field);
                        }
                    }

                    var typeInfo = new SymTypeInfo();
                    typeInfo.FullName = symType.FullName;
                    typeInfo.TypeArgs = symType.TypeArgs;
                    typeInfo.ConstFields = constFields.ToArray();
                    typeInfo.StaticFields = staticFields.ToArray();
                    typeInfo.Fields = fields.ToArray();
                    symType.Info = typeInfo;
                }
            }

        }

        /// <summary>
        /// Step 3: Generate header file
        /// </summary>
        public void GenerateHeader()
        {

            foreach (var s in mSymbols.Values)
            {
                mAllSymbols.Add(s.Kind.ToUpper() + ": " + s.FullName + "  ->  " + s.ParentName);
                if (s is SymNamespace n)
                    mNamespaces.Add(n.FullName);
            }

            mAllSymbols.Sort((a, b) => a.CompareTo(b));
            mNamespaces.Sort((a, b) => a.CompareTo(b));
        }

        /// <summary>
        /// This may be called after all steps have been completed.
        /// </summary>
        public List<string> GenerateReport()
        {
            // Count errors
            var errors = new Dictionary<string, int>();
            int unknownErrors = 0;
            int totalErrors = 0;
            foreach (var file in mFiles.Values)
            {
                foreach (var token in file.SyntaxFile.Lexer)
                {
                    if (token.Error)
                    {
                        var foundError = false;
                        foreach (var error in token.GetInfos<TokenError>())
                        {
                            foundError = true;
                            var name = error.GetType().Name.ToString();
                            if (errors.ContainsKey(name))
                                errors[name] += 1;
                            else
                                errors[name] = 1;
                            totalErrors++;
                        }
                        if (!foundError)
                        {
                            unknownErrors++;
                            totalErrors++;
                        }
                    }
                }
                if (unknownErrors != 0)
                    errors["Unknown"] = unknownErrors;
            }

            // Report errors
            var headerFile = new List<string>();
            if (totalErrors == 0)
            {
                headerFile.Add("SUCCESS!  No Errors found");
            }
            else
            {
                headerFile.Add("FAIL!  " + totalErrors + " errors found!");
                foreach (var error in errors)
                {
                    headerFile.Add("    " + error.Key + ": " + error.Value);
                }
            }
            headerFile.Add("");

            // Count symbols
            int concreteTypes = 0;
            int genericTypes = 0;
            foreach (var sym in mSymbols.Values)
            {
                if (sym.GetType() == typeof(SymType))
                {
                    var t = sym as SymType;
                    if (t.Info.TypeArgs.Length == 0)
                        concreteTypes++;
                    else
                        genericTypes++;
                }
            }

            headerFile.Add("SYMBOLS: " + mAllSymbols.Count);
            headerFile.Add("    Concrete: " + concreteTypes);
            headerFile.Add("    Generic: " + genericTypes);
            headerFile.Add("");

            // Report symbols
            foreach (var sym in mSymbols.Values)
            {
                if (sym.GetType() != typeof(SymType))
                    continue;
                var type = sym as SymType;

                headerFile.Add("");
                headerFile.Add("");
                headerFile.Add("TYPE " + type.Kind.ToUpper() + ": " + type.Info.FullNameWithTypeArgs());
                if (type.Info == null)
                {
                    headerFile.Add("    ERROR, unresolved info");
                    continue;
                }

                foreach (var field in type.Info.ConstFields)
                    headerFile.Add("    const " + field.Name + " " + field.Type);
                foreach (var field in type.Info.StaticFields)
                    headerFile.Add("    static " + field.Name + " " + field.Type);
                foreach (var field in type.Info.Fields)
                    headerFile.Add("    field " + field.Name + " " + field.Type + ", address=" + field.Address);
            }

            headerFile.Add("");
            headerFile.Add("Header time = " + (int)(DateTime.Now - mStartTime).TotalMilliseconds + " ms");


            return headerFile;
        }

        SymType ResolveType(Symbol scope, SyntaxExpr typeExpr)
        {
            Debug.Assert(typeExpr != null && typeExpr.Token.Name != "");

            // For now skip unary type qualifiers
            while (sUnaryTypeSymbols.Contains(typeExpr.Token) && typeExpr.Count >= 1)
                typeExpr = typeExpr[0];
            if (typeExpr.Token == "fun" || typeExpr.Token == "afun" || typeExpr.Token == "type")
            {
                Warning(typeExpr.Token, "Function types not resolved yet");
                return null; // For now skip these
            }

            if (typeExpr.Token == ParseZurf.VT_TYPE_ARG)
            {
                if (typeExpr.Count == 0)
                {
                    Reject(typeExpr.Token, "Unexpected empty type argument list");
                    return null;
                }
                // Resolve sub-types, TBD: Track errors
                for (int i = 1; i < typeExpr.Count; i++)
                    ResolveType(scope, typeExpr[i]);
                return ResolveType(scope, typeExpr[0]);
            }


            var symbol = mSymbols.FindUse(typeExpr.Token, scope, scope.File.Use);
            if (symbol == null)
                return null; // Error already marked
            typeExpr.Token.AddInfo(symbol);
            var typeSymbol = symbol as SymType;
            if (typeSymbol == null)
            {
                Reject(typeExpr.Token, "The symbol is not a type, it is a " + symbol.Kind);
                return null;
            }
            return typeSymbol;
        }


        // Does not reject if there is already an error there
        void Reject(Token token, string message)
        {
            if (!token.Error)
                token.AddError(new SilError(message));
        }

        // Does not add a warning if there is already an error there
        void Warning(Token token, string message)
        {
            if (!token.Error)
                token.AddWarning(message);
        }

    }
}
