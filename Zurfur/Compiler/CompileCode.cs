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
        public Symbol Type;
        public List<Symbol> Symbols = new List<Symbol>();
        public Symbol LeftDotType;
        public bool IsUntypedConst; // NOTE: 3int is a typed const

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
        static WordSet sOperators = new WordSet("+ - * / % & | ~ ! == != >= <= > < << >> and or in |= &= += -= <<= >>= .. ..+ ]");
        static WordSet sCmpOperators = new WordSet("== != >= <= > <");
        static WordSet sIntTypeNames = new WordSet("Zurfur.int Zurfur.u64 Zurfur.i32 Zurfur.u32");
        static WordSet sDerefAll = new WordSet("ref ^ * mut own");
        static WordSet sDerefMutOwn = new WordSet("mut own");
        static WordSet sDerefPointer = new WordSet("^");
        static WordMap<string> sBinOpNames = new WordMap<string> {
            {"+", "_opAdd"}, {"+=", "_opAdd"}, {"-", "_opSub"}, {"-=", "_opSub"},
            {"*", "_opMul"}, {"*=", "_opMul"}, {"/", "_opDiv"}, {"/=", "_opDiv"},
            {"%","_opRem" }, {"%=","_opRem" },
            {"..", "_opRange" }, {"..+", "_opRange" },
            {"==", "_opEq"}, {"!=", "_opEq" }, {"in", "_opIn" },
            {">", "_opCmp" }, {">=", "_opCmp" }, {"<", "_opCmp" }, {"<=", "_opCmp" },
            {"<<", "_opBitShl"}, {"<<=", "_opBitShl"}, {">>", "_opBitShr"}, {">>=", "_opBitShr"},
            {"&", "_opBitAnd"}, {"&=", "_opBitAnd"}, {"|", "_opBitOr"}, {"|=", "_opBitOr"},
            {"~", "_opBitXor"}, {"~=", "_opBitXor"},
            {"]", "_opIndex" }
        };

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
            var typeInt = table.Lookup("Zurfur.int");
            var typeU64 = table.Lookup("Zurfur.u64");
            var typeI32 = table.Lookup("Zurfur.i32");
            var typeStr = table.Lookup("Zurfur.str");
            var typeBool = table.Lookup("Zurfur.bool");
            var typeF64 = table.Lookup("Zurfur.f64");
            var typeF32 = table.Lookup("Zurfur.f32");

            Debug.Assert(typeVoid != null 
                && typeNil != null
                && typeInt != null
                && typeU64 != null
                && typeI32 != null
                && typeStr != null 
                && typeBool != null
                && typeF64 != null
                && typeF32 != null);

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
                    return GenNewVarsOperator(ex);
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
                    return new Rval(token, typeInt);
                else if (name == "return")
                    return GenReturn(ex);
                else if (name == "true" || name == "false")
                    return new Rval(token, typeBool);
                else
                {
                    Warn(ex.Token, "Not compiled yet");
                    GenParams(ex, new List<Rval>());
                }
                return null;
            }

            Rval GenConstNumber(SyntaxExpr ex)
            {
                // Get type (int, f64, or custom)
                var numberType = ex.Token.Name.Contains(".") ? typeF64 : typeInt;
                var untypedConst = true;
                if (ex.Count == 1)
                {
                    // TBD: Allow user defined custom types
                    untypedConst = false;
                    ex[0].Token.Type = eTokenType.TypeName;
                    var customType = ex[0].Token;
                    if (customType == "int")
                        numberType = typeInt;
                    else if (customType == "u64")
                        numberType = typeU64;
                    else if (customType == "f64")
                        numberType = typeF64;
                    else if (customType == "f32")
                        numberType = typeF32;
                    else
                    {
                        Reject(ex[0].Token, "Undefined number type");
                    }
                }

                var rval = new Rval(ex.Token, numberType) { IsUntypedConst = untypedConst };

                return rval;
            }


            Rval GenIdentifier(SyntaxExpr ex)
            {
                var rval = new Rval(ex.Token);
                FindGlobal(ex.Token, rval.Symbols);
                if (rval.Symbols.Count == 0)
                    Reject(ex.Token, "Undefined symbol");
                RemoveLastDuplicates(rval.Symbols);
                return rval;
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
                leftType = DerefMutOwn(leftType);
                leftType = DerefAll(leftType);

                // Generic parameters not finished
                var identifier = ex[1].Token;
                if (leftType.FullName.StartsWith("#"))
                {
                    // TBD: Use constraints to find the type
                    Reject(identifier, "Compiler not finished: Dot operator on generic type");
                    return null;
                }

                var rval = new Rval(identifier);
                rval.LeftDotType = leftType;
                FindInType(identifier, leftType, rval.Symbols);
                RemoveLastDuplicates(rval.Symbols);
                if (rval.Symbols.Count == 0)
                    Reject(identifier, $"Undefined symbol in the type '{leftType}'");

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

            Symbol DerefMutOwn(Symbol type) => Deref(type, sDerefMutOwn);
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

            Rval GenNewVarsOperator(SyntaxExpr ex)
            {
                if (ex.Count == 0)
                    return null;  // Syntax error

                // Create variables
                if (ex.Count == 1)
                    return GenNewVars(ex[0], ex.Token);

                // Capture variabes
                var value = GenExpr(ex[0]);
                var variables = GenNewVars(ex[1], ex.Token);

                if (value == null || variables == null)
                    return null;

                var valueType = EvalType(value);
                if (valueType == null)
                    return null;

                // Assign type to local variable
                Debug.Assert(variables.Symbols.Count == 1 && variables.Symbols[0].IsLocal && variables.Symbols[0].Type == null);
                variables.Symbols[0].Type = valueType;
                variables.Identifier.AddInfo(valueType);
                return value;
            }

            Rval GenNewVars(SyntaxExpr ex, Token rejectToken)
            {
                var newSymbols = new List<Symbol>();
                foreach (var e in ex)
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
                    Reject(rejectToken, "Expecting at least 1 symbol");
                    return null;
                }
                if (newSymbols.Count != 1)
                {
                    Reject(rejectToken, "Multiple symbols not supported yet");
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

                var typeNoMut = DerefMutOwn(type);
                if (typeNoMut.Parent.SimpleName != "^" && typeNoMut != typeU64)
                {
                    Reject(ex.Token, $"The cast type must be a pointer or u64, but it is '{typeNoMut}'");
                    return null;
                }

                var exprType = EvalType(expr);
                if (exprType == null)
                    return null;

                var exprTypeNoMut = DerefMutOwn(exprType);
                if (exprTypeNoMut.Parent.SimpleName != "^" && exprTypeNoMut != typeU64)
                {
                    Reject(ex.Token, $"The expression must evaluate to a pointer or u64, but is a '{exprTypeNoMut}'");
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

                // Assign type to local variable
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
                if (sIntTypeNames.Contains(leftType.FullName) && right.IsUntypedConst && sIntTypeNames.Contains(rightType.FullName))
                    rightType = leftType; // Dummy

                if (leftType.Parent != null && leftType.Parent.SimpleName == "^" && rightType == typeNil)
                {
                    return null; // Pointer = nil is ok
                }

                // TBD: Must afigure out how to deal with mut
                if (DerefMutOwn(leftType).FullName != DerefMutOwn(rightType).FullName)
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

            Rval DummyFunction(Token token, Symbol returnType, Symbol arg1Type, Symbol arg2Type)
            {
                var opFunc = new SymMethod(currentMethod.Parent, token, $"op{token}({arg1Type},{arg2Type})({returnType})");
                opFunc.Type = returnType;
                token.AddInfo(opFunc);
                return new Rval(token, returnType);
            }

            Rval GenOperator(SyntaxExpr ex)
            {
                if (ex.Count == 1 && ex.Token == "&")
                    return GenRefOrAddressOf(ex);

                var args = GenCallParams(ex, 0);
                if (args == null || args.Count == 0)
                    return null;  // Unresolved type or syntax error

                if (ex.Token == "and" || ex.Token == "or" || ex.Token == "!")
                    return GenBooleanOperator(ex, args);

                if (args.Count == 2 && (args[0].Type.Parent.SimpleName == "^" || args[1].Type.Parent.SimpleName == "^"))
                    return GenRawPointerOperator(ex, args[0], args[1]);

                // Implicit conversion of untyped constant to integer types
                // TBD: Calculate constant during compilation and do range checks.
                //      Also, this probably belongs in FindCompatibleFunction so it applies to functions
                if (args.Count == 2 && ex.Token != "<<" && ex.Token != ">>" && ex.Token != "]")
                {
                    // Most operators want both sides to be of same type
                    var left = args[0];
                    var right = args[1];
                    if (right.IsUntypedConst && !left.IsUntypedConst 
                            && sIntTypeNames.Contains(left.Type.FullName) && sIntTypeNames.Contains(right.Type.FullName))
                        right.Type = left.Type;
                    if (!right.IsUntypedConst && left.IsUntypedConst 
                            && sIntTypeNames.Contains(right.Type.FullName) && sIntTypeNames.Contains(left.Type.FullName))
                        left.Type = right.Type;
                }

                var operatorName = sBinOpNames[ex.Token];
                if (args.Count == 1)
                {
                    if (ex.Token == "-")
                        operatorName = "_opNeg";
                    else if (ex.Token == "~")
                        operatorName = "_opBitNot";
                }
                
                var functions = new List<Symbol>();
                FindGlobal(operatorName, functions);

                var rval = FindCompatibleFunction(ex.Token, args, functions, 
                    $" '{operatorName}' (operator '{ex.Token}')");
                if (rval == null)
                    return null;

                if (sCmpOperators.Contains(ex.Token))
                {
                    var returnType = ex.Token == "==" || ex.Token == "!=" ? typeBool : typeI32;
                    if (rval.Type != returnType)
                    {
                        Reject(ex.Token, $"Expecting operator to return '{returnType}'");
                        return null;
                    }
                    rval.Type = typeBool;
                }

                return rval;
            }

            Rval GenRawPointerOperator(SyntaxExpr ex, Rval left, Rval right)
            {
                var leftType = left.Type;
                var rightType = right.Type;
                // Add/subtract pointers to number types
                if (ex.Token == "+" || ex.Token == "-")
                {
                    if (leftType.Parent.SimpleName == "^" && sIntTypeNames.Contains(rightType.FullName))
                        return DummyFunction(ex.Token, leftType, leftType, rightType);
                    if (rightType.Parent.SimpleName == "^" && sIntTypeNames.Contains(leftType.FullName))
                        return DummyFunction(ex.Token, rightType, leftType, rightType);
                    if (ex.Token == "-"
                        && leftType.Parent.SimpleName == "^" && rightType.Parent.SimpleName == "^"
                        && leftType.FullName == rightType.FullName)
                    {
                        return DummyFunction(ex.Token, typeU64, leftType, rightType);
                    }
                    Reject(ex.Token, $"Operator '{ex.Token}' cannot be used with types '({leftType},{rightType})'");
                    return null;
                }

                // Compare pointers to pointers and nil
                if (sCmpOperators.Contains(ex.Token))
                {
                    if (leftType.Parent.SimpleName == "^" && rightType.Parent.SimpleName == "^")
                        return DummyFunction(ex.Token, typeBool, leftType, rightType);
                    if (ex.Token == "==" || ex.Token == "!=")
                    {
                        if (leftType.Parent.SimpleName == "^" && rightType == typeNil)
                            return DummyFunction(ex.Token, typeBool, leftType, rightType);
                        if (leftType == typeNil && rightType.Parent.SimpleName == "^")
                            return DummyFunction(ex.Token, typeBool, leftType, rightType);
                    }
                    Reject(ex.Token, $"Operator '{ex.Token}' cannot be used with types '({leftType},{rightType})'");
                    return null;
                }
                Reject(ex.Token, $"Operator '{ex.Token}' does not apply to pointers");
                return null;
            }

            Rval GenBooleanOperator(SyntaxExpr ex, List<Rval> args)
            {
                if (args.FindIndex(a => a.Type.FullName != "Zurfur.bool") >= 0)
                    Reject(ex.Token, $"Operator '{ex.Token}' can only take 'bool' parameters, not {ParamTypes(args)}");
                return DummyFunction(ex.Token, typeBool, typeBool, typeBool);

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

                var args = GenCallParams(ex, 1);

                if (funCall == null || funCall.Symbols.Count == 0)
                    return null;  // Undefined symbol or error evaluating left side

                if (args == null)
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
                {
                    funCall.Type = funCall.LeftDotType;
                    args.Insert(0, funCall);
                }

                var function = FindCompatibleFunction(funCall.Identifier, args, funCall.Symbols, $"'{funCall.Identifier}'");
                if (function != null && function.Symbols.Count > 0 && function.Symbols[0].IsGetter)
                    Reject(ex.Token, "Getter cannot be called with parenthesis");

                return function;
            }

            // If there is an error, mark it and give feedback on possible matches.
            Rval FindCompatibleFunction(Token token, List<Rval> args, List<Symbol> functions, string rejectName)
            {
                foreach (var sym in args)
                    if (sym.Type.FullName.Contains("#"))
                    {
                        Reject(token, "Generics not supported yet");
                        return null;
                    }

                var oldCalls = functions.ToArray();
                functions.RemoveAll(callFunc => !IsCallCompatible((SymMethod)callFunc, args));

                // TBD: Would be nice to give an error on the parameter if it's obvious
                //      which one it is.  Error messages can be improved a lot.
                if (functions.Count == 0)
                {
                    Reject(token, $"No function {rejectName} taking parameters {ParamTypes(args)} is in scope.");
                    foreach (var sym in oldCalls)
                        token.AddInfo(sym);
                    return null;
                }

                if (functions.Count != 1)
                {
                    Reject(token, $"Found multiple functions {rejectName} taking {ParamTypes(args)}");
                    foreach (var sym in functions)
                        token.AddInfo(sym);
                    return null;
                }

                var method = (SymMethod)functions[0];
                token.AddInfo(method);
                return new Rval(token) { Type = method.GetReturnType(table), Symbols = functions };
            }


            // Return parameter types as a string, eg. '(type1, type2,...)'
            string ParamTypes(List<Rval> args)
                => "'(" + string.Join(", ", args.Select(a => a.Type.FullName)) + ")'";

            bool IsCallCompatible(SymMethod method, List<Rval> args)
            {
                var methodParamTypes = method.GetParamTypeList();
                if (args.Count != methodParamTypes.Count)
                    return false;

                // TBD: Maybe mut<Type> should not be a generic type (just an attribute)
                for (var i = 0; i < args.Count; i++)
                {
                    if (methodParamTypes[i] == null)
                        return false;

                    // TBD: Consider making 'mut' into a type attribute
                    var arg = DerefMutOwn(args[i].Type);
                    var param = DerefMutOwn(methodParamTypes[i]);

                    // Implicit conversion from ^Type to ^void
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


            // Parameters are evaluated for the types. 
            // If any parameter can't be evaluated, NULL is returned.
            // A non-null value means that all Rval's have a valid Type.
            List<Rval> GenCallParams(SyntaxExpr ex, int startParam)
            {
                var funArgs = new List<Rval>();
                var paramHasError = false;
                for (int i = startParam; i < ex.Count; i++)
                {
                    var rval = GenExpr(ex[i]);
                    if (rval != null)
                        EvalType(rval);
                    if (rval != null && rval.Type != null)
                        funArgs.Add(rval);
                    else
                        paramHasError = true;
                }
                return paramHasError ? null : funArgs;
            }


            // Get return type or null if symbol is not found, is unresolved, or ambiguous.
            // Mark an error when there is no match.
            Symbol EvalType(Rval rval, bool ignoreLocalUntypedError = false)
            {
                // Done if we already have return type
                if (rval.Type != null)
                    return rval.Type;

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

            /// <summary>
            /// Find symbols in the type, including methods defined in
            /// the type's module, this module, or use statements.
            /// </summary>
            void FindInType(string identifier, Symbol inType, List<Symbol> symbols)
            {
                // Find symbols defined in the type (specialized and un-specialized)
                AddSymbolsNamed(identifier, inType, symbols);
                if (inType is SymSpecializedType)
                    AddSymbolsNamed(identifier, inType.Parent, symbols);

                // Find methods in the type's module
                AddMethodsNamedInModule(identifier, inType.Parent, inType, symbols);

                // Find methods in the current function's module
                AddMethodsNamedInModule(identifier, currentMethod.Parent, inType, symbols);

                // TBD: Scan use statements for methods
                if (identifier == "bytesToHex")
                {

                }

            }

            // Add all children with the given name (primary or method)
            void AddSymbolsNamed(string name, Symbol inType, List<Symbol> symbols)
            {
                if (inType.TryGetPrimary(name, out Symbol sym))
                    symbols.Add(sym);
                if (inType.HasMethodNamed(name))
                    foreach (var child in inType.Children)
                        if (child.IsMethod && child.Token == name)
                            symbols.Add(child);
            }

            // Walk up `inModule` to find the module, then collect methods `withMethodType`
            void AddMethodsNamedInModule(string name, Symbol inModule, Symbol withMethodType, List<Symbol> symbols)
            {
                while (inModule != null && !inModule.IsModule)
                    inModule = inModule.Parent;
                if (inModule == null || !inModule.HasMethodNamed(name))
                    return;

                // Ignore mut, etc., then just compare the non-specialized type.
                withMethodType = DerefMutOwn(withMethodType);
                if (withMethodType.IsSpecializedType)
                    withMethodType = withMethodType.Parent;

                foreach (var child in inModule.Children)
                {
                    if (!child.IsMethod || !child.IsExtension || child.Token != name)
                        continue;
                    var parameters = ((SymMethod)child).GetParamTypeList();
                    if (parameters.Count == 0)
                        continue;

                    // Ignore mut, etc., then just compare the non-specialized type
                    //      e.g: List<#1> matches List<byte> so we get all functions
                    var paramType = DerefMutOwn(parameters[0]);
                    if (paramType.IsSpecializedType)
                        paramType = paramType.Parent;
                   
                    if (paramType.FullName != withMethodType.FullName)
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

                // Find global symbols in this module
                AddGlobalSymbolsNamed(identifier, currentMethod.Parent, symbols);

                // Search 'use' symbol
                if (fileUses.UseSymbols.TryGetValue(identifier, out var useSymbols))
                {
                    foreach (var sym in useSymbols)
                        if (!symbols.Contains(sym))
                            symbols.Add(sym);
                }
            }

            // Add all children with the given name (primary or method)
            void AddGlobalSymbolsNamed(string name, Symbol module, List<Symbol> symbols)
            {
                if (module.TryGetPrimary(name, out Symbol sym))
                    symbols.Add(sym);
                if (module.HasMethodNamed(name))
                    foreach (var child in module.Children)
                        if (child.IsMethod && child.Token == name && !child.IsExtension)
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
