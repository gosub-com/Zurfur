using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Gosub.Zurfur.Lex;


namespace Gosub.Zurfur.Compiler
{
    public class ZilGenerateError : TokenError
    {
        public ZilGenerateError(string message) : base(message) { }
    }


    class Rval
    {
        public Token Identifier;
        public Symbol ReturnType;
        public bool IsConst;

        public Rval(Symbol symbol)
        {
            ReturnType = symbol;
        }

        public Rval(Symbol symbol, Token identifier)
        {
            ReturnType = symbol;
            Identifier = identifier;
        }

        public override string ToString()
        {
            if (ReturnType == null)
                return "()";
            return ReturnType.FullName;
        }

    }

    /// <summary>
    /// Compile the code given the output of header file generation.
    /// </summary>
    static class CompileCode
    {
        static WordSet sOperators = new WordSet("+ - * / % & | ~ == != >= <= > < << >> and or |= &= += -= <<= >>=");
        static WordSet sCmpOperators = new WordSet("== != >= <= > < and or");
        static WordSet sIntTypeNames = new WordSet("Zurfur.int Zurfur.uint Zurfur.i64 Zurfur.i32 Zurfur.xint Zurfur.xuint");


        static public void GenerateCode(
            Dictionary<string, SyntaxFile> syntaxFiles,
            SymbolTable table,
            Dictionary<SyntaxScope, Symbol> syntaxToSymbol,
            Dictionary<string, FileUseSymbols> allFileUses)
        {
            foreach (var syntaxFile in syntaxFiles)
            {
                var fileUses = allFileUses[syntaxFile.Key];
                foreach (var synFunc in syntaxFile.Value.Methods)
                {
                    // Get currentMethod
                    if (!syntaxToSymbol.TryGetValue(synFunc, out var scope))
                        continue; // Syntax error
                    var currentMethod = scope as SymMethod;
                    if (currentMethod == null)
                        continue; // Syntax error
                    GenMethod(syntaxFile.Value, synFunc, table, fileUses, currentMethod);
                }
            }
        }


        static void GenMethod(
            SyntaxFile syntaxFile,
            SyntaxFunc synFunc,
            SymbolTable table,
            FileUseSymbols fileUses,
            SymMethod currentMethod)
        {

            var locals = new Dictionary<string, SymLocal>();

            if (synFunc.Statements != null)
                foreach (var statement in synFunc.Statements)
                {
                    GenExpr(statement);
                }
            return;


            // Evaluate an expression.  When null is returned, the error is already marked
            Rval GenExpr(SyntaxExpr ex)
            {
                var token = ex.Token;
                var name = token.Name;
                if (name == "")
                {
                    Warn(token, "Compiler error: Not compiled");
                    return null;  // Syntax error should already be marked
                }

                // Terminals: Number, string, identifier
                if (char.IsDigit(name[0]))
                    return GenConstNumber(ex);
                else if (name == "\"" || name == "``")
                    return new Rval(table.Lookup("Zurfur.str"));
                else if (token.Type == eTokenType.Identifier)
                    return GenIdentifier(ex);
                else if (name == "(")
                    return GenParen(ex);
                else if (name == ".")
                    return GenDot(ex);
                else if (name == ")")
                    return GenCall(ex);
                else if (name == "@")
                    return GenNewVars(ex);
                else if (name == "=")
                    return GenAssign(ex);
                else if (sOperators.Contains(name))
                    return GenOperator(ex);
                else if (name == "?")
                    return GenTernary(ex);
                else if (name == "null" || name == "nil")
                    return new Rval(table.UnaryTypeSymbols["nil"]);
                else if (name == "cast")
                    return GenCast(ex);
                else if (name == "ref")
                    return GenRef(ex);
                else if (name == "sizeof")
                    return new Rval(table.Lookup("Zurfur.xuint"));
                else if (name == "return")
                    return GenReturn(ex);
                else
                {
                    Warn(ex.Token, "Not compiled yet");
                    GenParams(ex, new List<Rval>());
                }
                return null;
            }

            Rval GenConstNumber(SyntaxExpr ex)
            {
                var name = ex.Token.Name;
                var rval = new Rval(name.Contains(".") ? table.Lookup("Zurfur.f64") : table.Lookup("Zurfur.int"));
                rval.IsConst = true;
                return rval;
            }

            Rval GenIdentifier(SyntaxExpr ex)
            {
                return FindGlobal(ex.Token);
            }

            Rval GenParen(SyntaxExpr ex)
            {
                if (ex.Count == 0)
                {
                    Reject(ex.Token, "Expecting an expression inside parenthesis");
                    return null;
                }
                if (ex.Count != 1)
                    Reject(ex.Token, "Compiler not finished: Doesn't support tuples yet");
                return GenExpr(ex[0]);
            }

            Rval GenDot(SyntaxExpr ex)
            {
                if (ex.Count != 2)
                    return null;  // Syntax error

                var leftDot = GenExpr(ex[0]);
                if (leftDot == null)
                    return null; // Error already marked
                var leftType = EvalForType(leftDot, ex.Token);
                if (leftType == null)
                    return null;

                // Get type from left parameter: (type).identifier
                var identifier = ex[1].Token;
                if (identifier.Type != eTokenType.Identifier)
                {
                    Reject(ex.Token, "Error to the right, must be an identifier"); // TBD: Shouldn't need this, error already marked
                    return null;
                }

                // Auto-dereference pointers and references
                while (leftType is SymSpecializedType genericSym
                    && genericSym.Params.Length != 0
                    && (genericSym.Parent.Name == "ref" || genericSym.Parent.Name == "mut"
                        || genericSym.Parent.Name == "^" || genericSym.Parent.Name == "*"))
                {
                    // Move up to non-generic concrete type
                    // TBD: Preserve concrete type parameters
                    leftType = genericSym.Params[0];
                }

                // Generic specialized types use the generic concrete type
                // unless they are type parameters.
                // (e.g. List<int> becomes List`1)
                if (leftType is SymSpecializedType && !leftType.FullName.StartsWith("#"))
                {
                    leftType = leftType.Parent;
                }

                if (leftType.FullName.StartsWith("#"))
                {
                    // TBD: Use constraints to find the type
                    Reject(identifier, "Compiler not finished: Dot operator on generic type");
                    return null;
                }


                // Select the best matching symbol found on the left side of the dot.
                // The list of symbols can come from the global scope
                // (local, parameter, use, etc.) or from the dot operator.
                // TBD: Find member functions for this type
                var identifierSyms = new List<Symbol>();
                leftType.FindSymbolsNamed(identifier, identifierSyms);

                // Undefined symbol
                if (identifierSyms.Count == 0)
                {
                    Reject(identifier, $"Undefined symbol in {leftType}");
                    return null;
                }

                // TBD: Have to select the best match
                if (identifierSyms.Count != 1)
                {
                    // TBD: Allow multiple symbols (for now, just mark them)
                    Reject(identifier, $"Multiple symbols found in {leftType}.  Compiler not finished.");

                    // TBD: Remove - this is just debug info
                    var s = "";
                    foreach (var symbol in identifierSyms)
                        s += symbol.FullName + "\n";
                    identifier.AddInfo(s);
                    return null;
                }

                // Link token to symbol
                var sym = identifierSyms[0];
                identifier.AddInfo(sym);
                return new Rval(sym, identifier);
            }


            // Evaluate the type of the symbol, return NULL if there is an error.
            // TBD: Need to reject actual identifier symbol, not "left" or "right" side.
            Symbol EvalForType(Rval rval, Token rejectIfNull)
            {
                var sym = rval.ReturnType;
                if (sym.IsAnyType)
                    return sym;
                if (sym.IsField || sym.IsMethodParam || sym.IsLocal)
                {
                    if (sym.Type == null)
                        Reject(rval.Identifier ?? rejectIfNull, $"'{sym.Name}'  has an unresolved type");
                    return sym.Type;
                }
                if (sym.IsMethod)
                {
                    var returnType = ((SymMethod)sym).GetReturnType(table);
                    if (returnType == null)
                        Reject(rval.Identifier ?? rejectIfNull, $"'{sym}' has no type"); // Syntax error
                    return returnType;
                }
                Reject(rval.Identifier ?? rejectIfNull, $"Compiler failure: '{sym}' is {sym.KindName}");
                Debug.Assert(false);
                return null;
            }


            Rval GenNewVars(SyntaxExpr ex)
            {
                if (ex.Count == 0)
                    return null;  // Syntax error

                var localSyms = new List<Symbol>();
                foreach (var e in ex[0])
                {
                    if (e.Count == 0 || e.Token == "")
                        return null; // Syntax error

                    var newVarIdentifier = e.Token;
                    if (FindLocal(newVarIdentifier) != null)
                    {
                        Reject(newVarIdentifier, $"Duplicate symbole.  There is already a {FindLocal(newVarIdentifier).KindName} named '{newVarIdentifier}'");
                        return null;
                    }
                    var local = new SymLocal(null, newVarIdentifier);
                    locals[newVarIdentifier] = local;
                    localSyms.Add(local);
                    newVarIdentifier.AddInfo(local);

                    // Check for type name
                    if (e.Count >= 1 && e[0].Token != "")
                    {
                        local.Type = ResolveType.ResolveTypeOrReject(e[0], table, false, currentMethod, fileUses);
                    }
                }

                if (localSyms.Count == 0)
                {
                    Reject(ex.Token, "Expecting at least 1 symbol");
                    return null;
                }
                if (localSyms.Count != 1)
                {
                    Reject(ex.Token, "Multiple symbols not supported yet"); ;
                    return null;
                }
                return new Rval(localSyms[0], localSyms[0].Token);
            }

            Rval GenCall(SyntaxExpr ex)
            {
                // Generate function parameters
                var param = new List<Rval>();
                if (GenParams(ex, param))
                    return null;
                if (param.Count == 0)
                {
                    Reject(ex.Token, "No parameters");
                    return null;
                }

                // TBD: Select correct function
                var call = param[0].ReturnType;
                if (call.IsMethod)
                {
                    // TBD: Find correct function
                    //      Verify parameters are correct type
                }
                else if (call.IsType)
                {
                    // TBD: Search for new on that type (for now, just return the type)

                }

                var t = EvalForType(param[0], ex.Token);
                if (t == null)
                    return null;


                // TBD: Generate type
                return new Rval(t);
            }

            Rval GenOperator(SyntaxExpr ex)
            {
                if (ex.Count == 1
                        && (ex.Token == "-" || ex.Token == "~" || ex.Token == "&"))
                    return GenUnaryOp(ex);

                if (ex.Count != 2)
                    return null;  // Syntax error

                var left = GenExpr(ex[0]);
                var right = GenExpr(ex[1]);

                if (left == null || right == null)
                    return null;

                var leftType = EvalForType(left, ex.Token);
                var rightType = EvalForType(right, ex.Token);

                if (leftType == null || rightType == null)
                    return null;


                // TBD: Calculate const at run time
                // For now, pretend a constant int can be converted to any number type
                if (sIntTypeNames.Contains(leftType.FullName) && right.IsConst && sIntTypeNames.Contains(rightType.FullName))
                    rightType = leftType; // Dummy
                if (sIntTypeNames.Contains(rightType.FullName) && left.IsConst && sIntTypeNames.Contains(leftType.FullName))
                    leftType = rightType; // Dummy

                // TBD: For now, predefine dummy operator types.
                //      Need to actually search for an operator here.
                if (ex.Token == "<<" || ex.Token == ">>")
                {
                    if (!sIntTypeNames.Contains(leftType.FullName) || !sIntTypeNames.Contains(rightType.FullName))
                    {
                        Reject(ex.Token, $"Left and right side types must be number types (for now): {leftType.FullName} {ex.Token} {rightType.FullName}");
                        return null;
                    }
                }
                else if (sCmpOperators.Contains(ex.Token))
                {
                    if (leftType.FullName != rightType.FullName
                        && leftType.FullName != "nil"  && rightType.FullName != "nil")
                    {
                        Reject(ex.Token, $"Left and right side types must match or be nil: {leftType.FullName} {ex.Token} {rightType.FullName}");
                        return null;
                    }
                }
                else if (ex.Token == "+" || ex.Token == "-")
                {
                    if (leftType.FullName != rightType.FullName
                        && leftType.Parent.Name != "^" && sIntTypeNames.Contains(rightType.FullName))
                    {
                        Reject(ex.Token, $"Left and right side types must match: {leftType.FullName} {ex.Token} {rightType.FullName}");
                        return null;
                    }
                }
                else if (leftType.FullName != rightType.FullName)
                {
                    // Other operators take the same type on left and right
                    Reject(ex.Token, $"Left and right side types must match: {leftType.FullName} {ex.Token} {rightType.FullName}");
                    return null;
                }

                // TBD: For now, just return made up dummy operator
                var returnType = sCmpOperators.Contains(ex.Token) ? table.Lookup("Zurfur.bool") : leftType;
                var opFunc = new SymMethod(currentMethod.Parent, ex.Token, $"op{ex.Token}({leftType},{rightType})({returnType})");
                opFunc.Type = leftType;
                ex.Token.AddInfo(opFunc);
                return new Rval(returnType);
            }

            Rval GenUnaryOp(SyntaxExpr ex)
            {
                if (ex.Count != 1)
                    return null; // Syntax error

                var left = GenExpr(ex[0]);
                if (left == null)
                    return null;

                // TBD: Need to search for operator (see GenOperator)
                //      For now just return same type as expression
                var type = EvalForType(left, ex.Token);
                if (type == null)
                    return null;
                var rval = new Rval(type);
                rval.IsConst = left.IsConst;
                return rval;
            }

            Rval GenTernary(SyntaxExpr ex)
            {
                if (ex.Count != 3)
                    return null;  // Syntax error
                var cond = GenExpr(ex[0]);
                var condIf = GenExpr(ex[1]);
                var condElse = GenExpr(ex[2]);

                if (cond == null || condIf == null || condElse == null)
                    return null;

                var condType = EvalForType(cond, ex.Token);
                var condTypeIf = EvalForType(condIf, ex.Token);
                var condTypeElse = EvalForType(condElse, ex.Token);

                if (condType != null && condType.FullName != "Zurfur.bool")
                {
                    Reject(ex.Token, $"Left side must evaluate to 'Zurfur.bool', but it evaluates to '{condType}'");
                    return null;
                }
                if (condTypeIf != null && condTypeElse != null && condTypeIf.FullName != condTypeElse.FullName)
                {
                    Reject(ex.Token, $"Left and right sides must evaluate to same type, but they evaluate to '{condTypeIf}' and '{condTypeElse}'");
                    return null;
                }
                if (condTypeIf != null)
                    return new Rval(condTypeIf);

                return null;
            }

            Rval GenCast(SyntaxExpr ex)
            {
                if (ex.Count != 2)
                    return null; // Syntax error
                var type = ResolveType.ResolveTypeOrReject(ex[0], table, false, currentMethod, fileUses);
                var expr = GenExpr(ex[1]);

                if (type == null || expr == null)
                    return null;

                if (type.Parent.Name != "^" && type.FullName != "Zurfur.xuint" && type.FullName != "Zurfur.xint")
                {
                    Reject(ex.Token, $"The cast type must be a pointer, xuint, or xint, but it is '{type}'");
                    return null;
                }

                var exprType = EvalForType(expr, ex.Token);
                if (exprType == null)
                    return null;

                if (exprType.Parent.Name != "^" && exprType.FullName != "Zurfur.xuint" && exprType.FullName != "Zurfur.xint")
                {
                    Reject(ex.Token, $"The expression must evaluate to a pointer, but is a '{exprType}'");
                    return null;
                }
                return new Rval(type);
            }

            Rval GenRef(SyntaxExpr ex)
            {
                if (ex.Count != 1)
                    return null; // Syntax error

                var expr = GenExpr(ex[0]);
                if (expr == null)
                    return null;

                // TBD: Check to make sure this is valid (ref of return parameter is not, etc.)

                var exprType = EvalForType(expr, ex.Token);
                if (exprType == null)
                    return null;

                return new Rval(table.GetSpecializedType(table.UnaryTypeSymbols["ref"],
                                    new Symbol[] { exprType }));
            }



            Rval GenAssign(SyntaxExpr ex)
            {
                if (ex.Count != 2)
                    return null;  // Syntax error

                var left = GenExpr(ex[0]);
                var right = GenExpr(ex[1]);

                if (left == null || right == null)
                    return null;

                // Assign or verify type names
                // TBD allow destructuring: @(x,y) = Point()
                var rightType = EvalForType(right, ex.Token);
                if (rightType == null)
                    return null;

                var leftSym = left.ReturnType;
                if (leftSym.IsLocal && leftSym.TypeName == "")
                {
                    // Assign un-typed local
                    leftSym.Type = rightType;
                }
                else
                {
                    var leftType = EvalForType(left, ex.Token);
                    if (leftType == null)
                        return null;

                    if (leftType.FullName != rightType.FullName)
                    {
                        Reject(ex.Token, $"Types must match: {leftType.FullName} = {rightType.FullName}");
                        return null;
                    }
                }

                ex.Token.AddInfo($"({rightType.FullName}) = ({rightType.FullName})");

                return null;
            }

            Rval GenReturn(SyntaxExpr ex)
            {
                foreach (var e in ex)
                    GenExpr(e);
                return null;
            }

            bool GenParams(SyntaxExpr ex, List<Rval> param)
            {
                bool hasError = false;
                for (int p = 0; p < ex.Count; p++)
                {
                    var r = GenExpr(ex[p]);
                    if (r != null)
                    {
                        param.Add(r);
                    }
                    else
                    {
                        hasError = true;
                        Warn(ex.Token, $"Not compiled: Param #{p} has an error");
                        if (ex[p].Token != "")
                            Warn(ex.Token, $"Not compiled: Expression has an error");
                    }
                }
                return hasError;
            }



            /// <summary>
            /// Find a symbol in the current method.
            /// If it's not found, scan use statements for all occurences. 
            /// If still not found, mark an error.
            /// </summary>
            Rval FindGlobal(Token name)
            {
                var local = FindLocal(name);
                if (local != null)
                {
                    // TBD: In a future version, this will become a reserved word
                    if (name == "my")
                        name.Type = eTokenType.Reserved;
                    
                    name.AddInfo(local);
                    return new Rval(local, name);
                }

                // Scope walk modules
                var symbols = new List<Symbol>();
                var mod = currentMethod.Parent;
                Debug.Assert(mod.IsModule);
                while (mod != null)
                {
                    // NOTE: Verifier should not allow methods with the
                    //       same name as any primary including super modulkes
                    if (mod.TryGetPrimary(name, out var primary))
                    {
                        name.AddInfo(primary);
                        if (primary.IsType)
                            name.Type = eTokenType.TypeName;
                        return new Rval(primary, name);
                    }
                    mod.FindSymbolsNamed(name, symbols);
                    mod = mod.Parent;
                }

                // Search 'use' symbol
                if (fileUses.UseSymbols.TryGetValue(name.Name, out var modules))
                {
                    foreach (var module in modules)
                    {
                        module.FindSymbolsNamed(name, symbols);
                    }
                }
                if (symbols.Count == 0)
                {
                    Reject(name, "Undefined symbol");
                    return null;
                }
                if (symbols.Count != 1)
                {
                    Reject(name, "Compiler not finished: Multiple were found");
                    foreach (var symbol in symbols)
                        name.AddInfo(symbol.FullName);
                    return null;
                }
                name.AddInfo(symbols[0]);
                if (symbols[0].IsType)
                    name.Type = eTokenType.TypeName;
                return new Rval(symbols[0], name);
            }

            // Find a local variable or function parameter, return NULL if there isn't one.
            Symbol FindLocal(string name)
            {
                if (locals.TryGetValue(name, out var local))
                    return local;
                if (currentMethod.TryGetPrimary(name, out var primary))
                    return primary;
                return null;
            }

            void Reject(Token token, string message)
            {
                // TBD: Limit errors based on nearby syntax errors

                // If there is a syntax error on the line, ignore this error.
                // TBD: This also ignores other errors caused during code
                //      generation.  Maybe give more errors.
                //if (token.Y < syntaxFile.Lexer.LineCount)
                //    foreach (var t in syntaxFile.Lexer.GetLineTokens(token.Y))
                //        if (t.Error)
                //            return;
                //foreach (var t in syntaxFile.Lexer.MetaTokens)
                //    if (t.Error && t.Y == token.Y)
                //        return;

                table.Reject(token, message);
            }


            // Does not add a warning if there is already an error there
            void Warn(Token token, string message)
            {
                if (!token.Error)
                    token.AddWarning(new ZilWarn(message));
            }


        }


    }
}
