﻿using System;
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
        public Symbol LeftDotType;
        public List<Symbol> Symbols = new List<Symbol>();
        public Symbol Type;
        public bool IsConst;

        public Rval(Token identifier, Symbol returnType = null)
        {
            Type = returnType;
            Identifier = identifier;
        }

        public override string ToString()
        {
            return $"Identifier: '{Identifier}', symbols: {Symbols.Count}, "
                  +$"type: {(Type == null ? "(unresolved)" : Type.FullName)}, "
                  +$"leftDotType: {(LeftDotType == null ? "(unresolved)": LeftDotType.FullName)}";
        }
    }

    /// <summary>
    /// Compile the code given the output of header file generation.
    /// </summary>
    static class CompileCode
    {
        static WordSet sOperators = new WordSet("+ - * / % & | ~ ! == != >= <= > < << >> and or |= &= += -= <<= >>=");
        static WordSet sCmpOperators = new WordSet("== != >= <= > < and or");
        static WordSet sIntTypeNames = new WordSet("Zurfur.int Zurfur.u64 Zurfur.i32 Zurfur.u32 Zurfur.xuint Zurfur.xint");
        static WordSet sDerefAll = new WordSet("ref ^ * mut own");
        static WordSet sDerefMut = new WordSet("mut");
        static WordSet sDerefPointer = new WordSet("^");


        static public void GenerateCode(
            Dictionary<string, SyntaxFile> syntaxFiles,
            SymbolTable table,
            Dictionary<SyntaxScope, Symbol> syntaxToSymbol,
            UseSymbols allFileUses)
        {
            foreach (var syntaxFile in syntaxFiles)
            {
                var fileUses = allFileUses.Files[syntaxFile.Key];
                foreach (var synFunc in syntaxFile.Value.Methods)
                {
                    // Get currentMethod
                    if (!syntaxToSymbol.TryGetValue(synFunc, out var scope))
                        continue; // Syntax error
                    var currentMethod = scope as SymMethod;
                    if (currentMethod == null)
                        continue; // Syntax error
                    GenMethod(synFunc, table, fileUses, currentMethod);
                }
            }
        }

        static void GenMethod(
            SyntaxFunc synFunc,
            SymbolTable table,
            UseSymbolsFile fileUses,
            SymMethod currentMethod)
        {
            var typeVoid = table.Lookup("Zurfur.void");
            var typeNil = table.Lookup("Zurfur.nil");
            var typeXuint = table.Lookup("Zurfur.xuint");
            var typeStr = table.Lookup("Zurfur.str");
            var typeBool = table.Lookup("Zurfur.bool");
            var typeF64 = table.Lookup("Zurfur.f64");
            var typeInt = table.Lookup("Zurfur.int");

            Debug.Assert(typeVoid != null 
                && typeNil != null
                && typeXuint != null
                && typeStr != null 
                && typeBool != null 
                && typeF64 != null 
                && typeInt != null);



            var locals = new Dictionary<string, SymLocal>();

            if (synFunc.Statements != null)
                foreach (var statement in synFunc.Statements)
                {
                    var expr = GenExpr(statement);
                    if (expr != null)
                        EvalType(expr);
                }
            return;


            // Evaluate an expression.  When null is returned, the error is already marked.
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
                    return new Rval(token, typeStr);
                else if ((char.IsLetter(name[0]) || name[0] == '_') && !ParseZurf.ReservedWords.Contains(name))
                    return GenIdentifier(ex);
                else if (name == "my")
                    return GenIdentifier(ex);
                else if (name == ParseZurf.VT_TYPE_ARG)
                    return GenGenericType(ex);
                else if (name == "(")
                    return GenParen(ex);
                else if (name == ".")
                    return GenDot(ex);
                else if (name == ".*")
                    return GenDotStar(ex);
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
                    return new Rval(token, typeNil);
                else if (name == "cast")
                    return GenCast(ex);
                else if (name == "ref")
                    return GenRefOrAddressOf(ex);
                else if (name == "sizeof")
                    return new Rval(token, typeXuint);
                else if (name == "return")
                    return GenReturn(ex);
                else if (name == "true" || name == "false")
                    return new Rval(token, typeBool) { IsConst = true };
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
                var rval = new Rval(ex.Token, name.Contains(".") ? typeF64 : typeInt);
                rval.IsConst = true;
                return rval;
            }

            Rval GenIdentifier(SyntaxExpr ex)
            {
                return new Rval(ex.Token);
            }

            // Similar to identifier, except we know it's a type (e.g. List<int>, etc)
            Rval GenGenericType(SyntaxExpr ex)
            {
                var type = ResolveType.ResolveTypeOrReject(ex, table, false, currentMethod, fileUses);
                if (type == null)
                    return null;

                return new Rval(ex.Token, type);
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
                var leftType = EvalType(leftDot);
                if (leftType == null)
                    return null;

                // Automatically dereference pointers, etc
                leftType = DerefMut(leftType);
                leftType = DerefAll(leftType);

                // Generic specialized types use the generic concrete type
                // unless they are type parameters.
                // (e.g. List<int> becomes List`1)
                if (leftType is SymSpecializedType && !leftType.FullName.StartsWith("#"))
                {
                    leftType = leftType.Parent;
                }

                var identifier = ex[1].Token;
                if (leftType.FullName.StartsWith("#"))
                {
                    // TBD: Use constraints to find the type
                    Reject(identifier, "Compiler not finished: Dot operator on generic type");
                    return null;
                }

                var rval = new Rval(identifier);
                rval.LeftDotType = leftType;
                return rval;
            }


            // Dereference the given type names
            Symbol Deref(Symbol type, WordSet typeNames)
            {
                // Auto-dereference pointers and references
                if (type is SymSpecializedType genericSym
                    && genericSym.Params.Length != 0
                    && typeNames.Contains(genericSym.Parent.SimpleName))
                {
                    // Move up to non-generic concrete type
                    // TBD: Preserve concrete type parameters
                    type = genericSym.Params[0];
                }
                return type;
            }

            Symbol DerefMut(Symbol type) => Deref(type, sDerefMut);
            Symbol DerefAll(Symbol type) => Deref(type, sDerefAll);
            Symbol DerefPointer(Symbol type) => Deref(type, sDerefPointer);

            Rval GenDotStar(SyntaxExpr ex)
            {
                if (ex.Count != 1)
                    return null; // Syntax error
                var leftDot = GenExpr(ex[0]);
                if (leftDot == null)
                    return null; // Error already marked
                var leftType = EvalType(leftDot);
                if (leftType == null)
                    return null;

                if (leftType.Parent == null || leftType.Parent.SimpleName != "^")
                {
                    Reject(ex.Token, $"Only pointers may be dereferenced, but the type is '${leftType}'");
                    return null;
                }
                var deref = DerefPointer(leftType);
                ex.Token.AddInfo(deref);
                return new Rval(ex.Token, deref);
            }

            Rval GenNewVars(SyntaxExpr ex)
            {
                if (ex.Count == 0)
                    return null;  // Syntax error

                var newSymbols = new List<Symbol>();
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
                    newSymbols.Add(local);

                    // Check for type name
                    if (e.Count >= 1 && e[0].Token != "")
                    {
                        local.Type = ResolveType.ResolveTypeOrReject(e[0], table, false, currentMethod, fileUses);
                    }
                }

                if (newSymbols.Count == 0)
                {
                    Reject(ex.Token, "Expecting at least 1 symbol");
                    return null;
                }
                if (newSymbols.Count != 1)
                {
                    Reject(ex.Token, "Multiple symbols not supported yet"); ;
                    return null;
                }
                var rval = new Rval(newSymbols[0].Token);
                rval.Symbols.Add(newSymbols[0]);
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

                var condType = EvalType(cond);
                var condTypeIf = EvalType(condIf);
                var condTypeElse = EvalType(condElse);

                if (condType != null && condType != typeBool)
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
                    return new Rval(ex.Token, condTypeIf);

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

                var typeNoMut = DerefMut(type);
                if (typeNoMut.Parent.SimpleName != "^" && typeNoMut != typeXuint)
                {
                    Reject(ex.Token, $"The cast type must be a pointer or xuint, but it is '{typeNoMut}'");
                    return null;
                }

                var exprType = EvalType(expr);
                if (exprType == null)
                    return null;

                var exprTypeNoMut = DerefMut(exprType);
                if (exprTypeNoMut.Parent.SimpleName != "^" && exprTypeNoMut != typeXuint)
                {
                    Reject(ex.Token, $"The expression must evaluate to a pointer, but is a '{exprTypeNoMut}'");
                    return null;
                }
                return new Rval(ex.Token, type);
            }

            Rval GenRefOrAddressOf(SyntaxExpr ex)
            {
                if (ex.Count != 1)
                    return null; // Syntax error
                Debug.Assert(ex.Token == "ref" || ex.Token == "&");

                var expr = GenExpr(ex[0]);
                if (expr == null)
                    return null;

                // TBD: Check to make sure this is valid (ref of return parameter is not, etc.)

                var exprType = EvalType(expr);
                if (exprType == null)
                    return null;

                // Ref or address off
                var refType = ex.Token == "ref" ? table.UnaryTypeSymbols["ref"] : table.UnaryTypeSymbols["^"];

                return new Rval(ex.Token, 
                    table.GetSpecializedType(refType, new Symbol[] { exprType }));
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
                var rightType = EvalType(right);
                if (rightType == null)
                    return null;

                EvalType(left, true);

                if (left.Symbols.Count == 1 && left.Symbols[0].IsLocal && left.Symbols[0].Type == null)
                {
                    // Assign untyped local
                    left.Symbols[0].Type = rightType;
                    left.Type = rightType;
                }

                var leftType = left.Type;
                if (leftType == null)
                {
                    Reject(ex.Token, "Untyped symbol");
                    return null;
                }

                // TBD: Calculate const at run time
                // For now, pretend a constant int can be converted to any number type
                if (sIntTypeNames.Contains(leftType.FullName) && right.IsConst && sIntTypeNames.Contains(rightType.FullName))
                    rightType = leftType; // Dummy

                if (leftType.Parent != null && leftType.Parent.SimpleName == "^" && rightType == typeNil)
                {
                    return null; // Pointer = nil is ok
                }

                if (DerefAll(leftType).FullName != DerefAll(rightType).FullName)
                {
                    Reject(ex.Token, $"Types must match: {leftType.FullName} = {rightType.FullName}");
                    return null;
                }

                // Debug, TBD: Remove
                ex.Token.AddInfo($"({rightType.FullName}) = ({rightType.FullName})");

                return null;
            }

            Rval GenReturn(SyntaxExpr ex)
            {
                var returns = new List<Rval>();
                var hasError = GenParams(ex, returns);
                if (hasError)
                    return null;
                if (returns.Count == 0)
                    return new Rval(ex.Token, typeVoid);

                if (returns.Count != 1)
                {
                    Reject(ex.Token, "Return tuples not supported yet");
                    return null;
                }
                return returns[0];
            }

            bool GenParams(SyntaxExpr ex, List<Rval> param)
            {
                bool hasError = false;
                for (int p = 0; p < ex.Count; p++)
                {
                    var rval = GenExpr(ex[p]);
                    if (rval != null)
                    {
                        param.Add(rval);
                        EvalType(rval);
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
            Rval GenOperator(SyntaxExpr ex)
            {
                if (ex.Token == "+")
                {
                    return GenPlus(ex);
                }
                if (ex.Count == 1
                        && (ex.Token == "-" || ex.Token == "~" || ex.Token == "&" || ex.Token == "!"))
                    return GenUnaryOp(ex);

                if (ex.Count != 2)
                    return null;  // Syntax error

                var left = GenExpr(ex[0]);
                var right = GenExpr(ex[1]);

                if (left == null || right == null)
                    return null;

                var leftType = EvalType(left);
                var rightType = EvalType(right);

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
                        && leftType != typeNil && rightType != typeNil)
                    {
                        Reject(ex.Token, $"Left and right side types must match or be nil: {leftType.FullName} {ex.Token} {rightType.FullName}");
                        return null;
                    }
                }
                else if (ex.Token == "+" || ex.Token == "-")
                {
                    if (leftType.FullName != rightType.FullName
                        && leftType.Parent.SimpleName != "^" && sIntTypeNames.Contains(rightType.FullName))
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
                return DummyFunction(ex.Token, sCmpOperators.Contains(ex.Token) ? typeBool : leftType, leftType, rightType);
            }

            Rval DummyFunction(Token token, Symbol returnType, Symbol arg1Type, Symbol arg2Type)
            {
                var opFunc = new SymMethod(currentMethod.Parent, token, $"op{token}({arg1Type},{arg2Type})({returnType})");
                opFunc.Type = returnType;
                token.AddInfo(opFunc);
                return new Rval(token, returnType);
            }

            Rval GenUnaryOp(SyntaxExpr ex)
            {
                if (ex.Count != 1)
                    return null; // Syntax error

                if (ex.Token == "&")
                    return GenRefOrAddressOf(ex);

                var left = GenExpr(ex[0]);
                if (left == null)
                    return null;

                // TBD: Need to search for operator (see GenOperator)
                //      For now just return same type as expression
                var type = EvalType(left);
                if (type == null)
                    return null;
                var rval = new Rval(ex.Token, type);
                rval.IsConst = left.IsConst;
                return rval;
            }

            Rval GenPlus(SyntaxExpr ex)
            {
                if (ex.Count != 2)
                    return null;  // Syntax error
                var left = GenExpr(ex[0]);
                var right = GenExpr(ex[1]);

                if (left == null || right == null)
                    return null;

                var leftType = EvalType(left);
                var rightType = EvalType(right);

                if (leftType == null || rightType == null)
                    return null;

                // DUMMY: Add pointers to number types
                if (leftType.Parent.SimpleName == "^" && sIntTypeNames.Contains(rightType.FullName))
                    return DummyFunction(ex.Token, leftType, leftType, rightType);
                if (rightType.Parent.SimpleName == "^" && sIntTypeNames.Contains(leftType.FullName))
                    return DummyFunction(ex.Token, rightType, leftType, rightType);

                var functions = new List<Symbol>();
                FindGlobal("_opAdd", functions);

                return FindCompatibleFunction(ex.Token, new List<Symbol> { leftType, rightType}, functions);
            }


            Rval GenCall(SyntaxExpr ex)
            {
                if (ex.Count == 0)
                    return null; // Syntax error

                // Generate function call and then parameters
                var funCall = GenExpr(ex[0]);

                // TBD: This is temporary until we collect "new" methods from types
                //      Allow this so all 'Type<T>(expression)' returns Type for now
                if (funCall != null && funCall.Type != null)
                    return funCall;

                if (funCall != null)
                    EvalSymbols(funCall);
                var argTypes = GenCallParams(ex, out var paramHasError);

                if (funCall == null || funCall.Symbols.Count == 0)
                    return null;  // Undefined symbol or error evaluating left side

                if (paramHasError)
                {
                    // Give some feedback on the functions that could be called
                    foreach (var sym in funCall.Symbols)
                        funCall.Identifier.AddInfo(sym);
                    return null;
                }

                // TBD: This is temporary until we collect "new" methods from types
                //      Allow this so all 'Type(expression)' returns Type for now 
                if (funCall.Symbols.Count == 1 && !funCall.Symbols[0].IsMethod)
                {
                    return funCall;
                }

                // TBD: Collect "new" methods from types, allow function calls on types and fields, etc.
                foreach (var s in funCall.Symbols)
                {
                    // For now, reject anything that is not a method
                    if (!s.IsMethod)
                    {
                        Reject(funCall.Identifier, $"Compiler not finished: Function calls on {s.KindName} not working yet");
                        return null;
                    }
                }

                // Insert leftDot type as first parameter so all types match
                // TBD: I don't think this is a long term solution
                if (funCall.LeftDotType != null && !funCall.LeftDotType.IsModule)
                    argTypes.Insert(0, funCall.LeftDotType);

                return FindCompatibleFunction(funCall.Identifier, argTypes, funCall.Symbols);
            }

            // If there is an error, mark it and give feedback on possible matches.
            Rval FindCompatibleFunction(Token token, List<Symbol> argTypes, List<Symbol> functions)
            {
                foreach (var sym in argTypes)
                    if (sym.FullName.Contains("#"))
                    {
                        Reject(token, "Generics not supported yet");
                        return null;
                    }

                var oldCalls = functions.ToArray();
                functions.RemoveAll(callFunc => !IsCallCompatible((SymMethod)callFunc, argTypes));

                // TBD: Would be nice to give an error on the parameter if it's obvious
                //      which one it is.  Error messages can be improved a lot.
                if (functions.Count == 0)
                {
                    var paramNames = string.Join(", ", argTypes.Select(a => a.FullName));
                    Reject(token, $"No function taking parameters '({paramNames})' is in scope.");
                    foreach (var sym in oldCalls)
                        token.AddInfo(sym);
                    return null;
                }

                if (functions.Count != 1)
                {
                    var paramNames = string.Join(", ", argTypes.Select(a => a.FullName));
                    Reject(token, $"Found multiple functions taking '({paramNames})'");
                    foreach (var sym in functions)
                        token.AddInfo(sym);
                    return null;
                }

                var method = (SymMethod)functions[0];
                token.AddInfo(method);
                return new Rval(token) { Type = method.GetReturnType(table) };
            }

            bool IsCallCompatible(SymMethod method, List<Symbol> argTypes)
            {
                var methodParamTypes = method.GetParamTypeList();
                if (argTypes.Count != methodParamTypes.Count)
                    return false;

                // TBD: Maybe mut<Type> should not be a generic type (just an attribute)
                for (var i = 0; i < argTypes.Count; i++)
                {
                    if (methodParamTypes[i] == null)
                        return false;

                    // TBD: Consider making 'mut' into a type attribute
                    var arg = DerefMut(argTypes[i]);
                    var param = DerefMut(methodParamTypes[i]);

                    // Implicicit conversion from ^Type to ^void
                    if (arg.Parent.SimpleName == "^" && param.Parent.SimpleName == "^"
                        && DerefPointer(param) == typeVoid)
                    {
                        continue;
                    }


                    if (arg.FullName != param.FullName)
                        return false;
                }
                return true;
            }


            // Parameters are evaluated for the types.  If any parameter can't be
            // evaluated, an error is generated and paramHasError is set to true.
            List<Symbol> GenCallParams(SyntaxExpr ex, out bool paramHasError)
            {
                var funArgs = new List<Symbol>();
                paramHasError = false;
                for (int p = 1; p < ex.Count; p++)
                {
                    var rval = GenExpr(ex[p]);
                    if (rval != null)
                    {
                        EvalType(rval);
                        if (rval.Type != null)
                            funArgs.Add(rval.Type);
                        else
                            paramHasError = true;

                    }
                    else
                    {
                        paramHasError = true;
                    }
                }
                return funArgs;
            }



            // Get return type or null if symbol is not found, is unresolved, or ambiguous.
            // Mark an error when there is no match.
            Symbol EvalType(Rval rval, bool ignoreLocalUntypedError = false)
            {
                // Done if we already have return type
                if (rval.Type != null)
                    return rval.Type;

                EvalSymbols(rval);
                var symbols = rval.Symbols;
                if (symbols.Count == 0)
                    return null;

                // Filter out functions, except getter's
                var oldCalls = symbols.ToArray();
                symbols.RemoveAll(callFunc => callFunc.IsMethod && !callFunc.IsGetter);
                if (symbols.Count == 0)
                {
                    Reject(rval.Identifier, $"Can't find variable, field, or getter function");
                    foreach (var oldSym in oldCalls)
                        rval.Identifier.AddInfo(oldSym);
                    return null;
                }

                // Don't allow multiple symbols
                var identifier = rval.Identifier;
                if (symbols.Count != 1)
                {
                    Reject(identifier, "Found multiple symbols");
                    foreach (var symbol in symbols)
                        identifier.AddInfo(symbol);
                    return null;
                }

                var sym = symbols[0];
                identifier.AddInfo(sym);
                if (sym.IsType || sym.IsModule)
                    identifier.Type = eTokenType.TypeName;

                if (sym.IsAnyType)
                {
                    rval.Type = sym;
                    return sym;
                }

                if (sym.IsLocal)
                {
                    if (sym.Type == null && !ignoreLocalUntypedError)
                        Reject(rval.Identifier, $"'{sym.SimpleName}'  has an unresolved type");
                    rval.Type = sym.Type;
                    return sym.Type;
                }
                if (sym.IsField || sym.IsMethodParam)
                {
                    if (sym.Type == null)
                        Reject(rval.Identifier, $"'{sym.SimpleName}'  has an unresolved type");
                    rval.Type = sym.Type;
                    return sym.Type;
                }
                if (sym.IsMethod)
                {
                    var returnType = ((SymMethod)sym).GetReturnType(table);
                    if (returnType == null)
                        Reject(rval.Identifier, $"'{sym}' has no type"); // Syntax error
                    rval.Type = returnType;
                    return returnType;
                }
                Reject(rval.Identifier, $"Compiler failure: '{sym}' is {sym.KindName}");
                Debug.Assert(false);
                return null;
            }

            void EvalSymbols(Rval rval)
            {
                var identifier = rval.Identifier;
                var symbols = rval.Symbols;
                symbols.Clear();

                // Find the matching symbols
                if (rval.LeftDotType == null)
                    FindGlobal(identifier, symbols);
                else
                    FindInType(identifier, rval.LeftDotType, symbols);

                // Check for undefined symbol
                if (symbols.Count == 0)
                {
                    if (rval.LeftDotType == null)
                        Reject(identifier, "Undefined symbol");
                    else
                        Reject(identifier, $"Undefined symbol in the type '{rval.LeftDotType}'");
                }
                RemoveLastDuplicates(symbols);
            }


            /// <summary>
            /// Find symbols in the type, including methods defined in
            /// the type's module, this module, or use statements.
            /// </summary>
            void FindInType(string identifier, Symbol type, List<Symbol> symbols)
            {
                AddSymbolsNamed(type, identifier, symbols);

                // Find methods in the type's module or parents
                var mod1 = type.Parent;
                while (mod1 != null)
                {
                    AddMethodsNamed(mod1, identifier, type, symbols);
                    mod1 = mod1.Parent;
                }

                // Find methods in the type's module or parents
                var mod2 = currentMethod.Parent;
                while (mod2 != null)
                {
                    AddMethodsNamed(mod2, identifier, type, symbols);
                    mod2 = mod2.Parent;
                }
            }

            void AddMethodsNamed(Symbol symbol, string name, Symbol type, List<Symbol> symbols)
            {
                if (!symbol.HasMethodNamed(name))
                    return;
                foreach (var child in symbol.Children)
                {
                    if (!child.IsMethod || !child.IsExtension || child.Token != name)
                        continue;
                    var parameters = ((SymMethod)child).GetParamTypeList();
                    if (parameters.Count == 0)
                        continue;

                    if (DerefMut(parameters[0]).FullName != type.FullName)
                        continue;

                    symbols.Add(child);
                }
            }

            /// <summary>
            /// Find symbols in the local/global scope that match this
            /// identifier.  If it's a local or parameter in the current
            /// method, stop searching.  Otherwise find a list of matching
            /// symbols in the current module (or parents) and also the use
            /// statements.
            /// </summary>
            void FindGlobal(string identifier, List<Symbol> symbols)
            {
                var local = FindLocal(identifier);
                if (local != null)
                {
                    symbols.Add(local);
                    return;
                }

                // Find symbols in module or parents of module
                var mod = currentMethod.Parent;
                AddSymbolsNamed(mod, identifier, symbols);

                // Search 'use' symbol
                if (fileUses.UseSymbols.TryGetValue(identifier, out var useSymbols))
                {
                    foreach (var sym in useSymbols)
                        if (!symbols.Contains(sym))
                            symbols.Add(sym);
                }
            }

            // Add all children with the given name (primary or method)
            void AddSymbolsNamed(Symbol moduleOrType, string name, List<Symbol> symbols)
            {
                if (moduleOrType.TryGetPrimary(name, out Symbol sym))
                    symbols.Add(sym);
                if (moduleOrType.HasMethodNamed(name))
                    foreach (var child in moduleOrType.Children)
                        if (child.IsMethod && child.Token == name)
                            symbols.Add(child);
            }

            void RemoveLastDuplicates(List<Symbol> symbols)
            {
                if (symbols.Count < 2)
                    return;
                for (int i = symbols.Count - 1; i >= 0; i--)
                {
                    var sym = symbols[i].FullName;
                    var findIndex = symbols.FindIndex(findSym => findSym.FullName == sym);
                    if (findIndex >= 0 && findIndex != i)
                        symbols.RemoveAt(findIndex);
                }
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
                // TBD: Limit errors based on nearby syntax errors?

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
