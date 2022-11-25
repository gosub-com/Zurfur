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
        public Token Token;
        public Symbol Type;
        public List<Symbol> Symbols = new List<Symbol>();
        public Symbol[] TypeArgs = Array.Empty<Symbol>();
        public Symbol InType;
        public bool IsUntypedConst; // NOTE: 3int is a typed const

        public Rval(Token token, Symbol returnType = null)
        {
            Type = returnType;
            Token = token;
        }

        public override string ToString()
        {
            var typeParams = "";
            if (TypeArgs.Length != 0)
            {
                typeParams = "<"
                    + string.Join(",", TypeArgs.Select(x => x.ToString()))
                    + ">";
            }
            return $"Token: '{Token}'{typeParams}, symbols: {Symbols.Count}"
                  + (Type == null ? "" : $", Type: {Type.FullName}")
                  + (InType == null ? "" : ", InType:" + InType.FullName);
        }

        // Return parameter types as a string, eg. '(type1, type2,...)<type1,type2...>'
        public static string ParamTypes(List<Rval> args, Symbol[] typeArgs)
        {

            var typeParams = "";
            if (typeArgs.Length != 0)
                typeParams = $"<{string.Join(",", typeArgs.Select(a => a.FullName))}>";
            var paramStr = $"{typeParams}({string.Join(",", args.Select(a => a.Type.FullName))})";
            return paramStr;
        }

    }

    /// <summary>
    /// Compile the code given the output of header file generation.
    /// </summary>
    static class CompileCode
    {
        const string RAW_POINTER_TYPE = "Zurfur.RawPointer`1";
        const string REF_TYPE = "Zurfur.Ref`1";

        static WordSet sOperators = new WordSet("+ - * / % & | ~ ! == != >= <= > < << >> and or in |= &= += -= <<= >>= .. ..+ ]");
        static WordSet sCmpOperators = new WordSet("== != >= <= > <");
        static WordSet sIntTypeNames = new WordSet("Zurfur.int Zurfur.u64 Zurfur.i32 Zurfur.u32");
        static WordSet sDerefPointers = new WordSet("Zurfur.RawPointer`1 Zurfur.Pointer`1");
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
            var typeU32 = table.Lookup("Zurfur.u32");
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

            var locals = new Dictionary<string, Symbol>();

            if (synFunc.Statements != null)
                foreach (var statement in synFunc.Statements)
                {
                    var expr = GenExpr(statement);
                    if (expr != null)
                        EvalType(expr);
                }

            // Unresolved local symbols generate an error
            // TBD: This is anoying while writing code, so show the error
            //      only when the symbol is used somewhere else in the code.
            //      Or don't show error when the cursor is over the symbol?
            // foreach (var local in locals.Values)
            //     if (local.Type == null)
            //         Reject(local.Token, $"'{local.SimpleName}'  has an unresolved type");

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
                else if (name == "\"" || name == "`")
                    return new Rval(token, typeStr);
                else if ((char.IsLetter(name[0]) || name[0] == '_') && !ParseZurf.ReservedWords.Contains(name))
                    return GenIdentifier(ex);
                else if (name == "my")
                    return GenIdentifier(ex);
                else if (name == ParseZurf.VT_TYPE_ARG)
                    return GenTypeArgs(ex);
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
                else if (name == "if" && ex.Count == 1)
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
                    else if (customType == "i32")
                        numberType = typeI32;
                    else if (customType == "u32")
                        numberType = typeU32;
                    else if (customType == "f64")
                        numberType = typeF64;
                    else if (customType == "f32")
                        numberType = typeF32;
                    else
                        Reject(ex[0].Token, $"'{ex[0].Token}' undefined number type");
                }

                var rval = new Rval(ex.Token, numberType) { IsUntypedConst = untypedConst };

                return rval;
            }


            Rval GenIdentifier(SyntaxExpr ex)
            {
                var symbols = FindGlobal(ex.Token, ex.Token.Name);
                if (symbols == null)
                    return null;
                return new Rval(ex.Token) {  Symbols = symbols };
            }

            // Type or function call (e.g. List<int>(), f<int>(), etc)
            Rval GenTypeArgs(SyntaxExpr ex)
            {
                var symbols = GenExpr(ex[0]);
                var typeParams = Resolver.ResolveTypeArgs(ex, table, currentMethod, fileUses);
                if (symbols == null || typeParams == null)
                    return null;
                symbols.TypeArgs = typeParams.ToArray();
                return symbols;
            }

            Rval GenParen(SyntaxExpr ex)
            {
                if (HasError(ex))
                    return null;

                if (ex.Count == 0)
                {
                    Reject(ex.Token, "Expecting an expression inside parenthesis");
                    return null;
                }
                if (ex.Count != 1)
                    Reject(ex.Token, "Compiler not finished: Doesn't support tuples yet");
                return GenExpr(ex[0]);
            }

            // Check top level for syntax error
            bool HasError(SyntaxExpr ex)
            {
                if (ex.Token.Error)
                    return true;
                foreach (var e in ex)
                    if (e.Token.Error)
                        return true;
                return false;
            }


            Rval GenDot(SyntaxExpr ex)
            {
                if (ex.Count != 2)
                    return null;  // Syntax error

                var leftDot = GenExpr(ex[0]);
                if (leftDot == null)
                    return null; // Error already marked
                var leftType = EvalType(leftDot, false, true);
                if (leftType == null)
                    return null;

                // Automatically dereference pointers, etc
                var identifier = ex[1].Token;
                if (identifier != "__valueNoDeref")
                    leftType = DerefPointers(leftType);

                // Generic parameters not finished
                if (leftType.IsGenericArg)
                {
                    // TBD: Use constraints to find the type
                    Reject(identifier, "Compiler not finished: Dot operator on generic type");
                    return null;
                }

                var symbols = FindInType(identifier, identifier.Name, leftType);
                if (symbols.Count == 0)
                {
                    Reject(identifier, $"'{identifier}' is an undefined symbol in the type '{leftType}'");
                    return null;
                }
                return new Rval(identifier) { Symbols = symbols, InType = leftType};
            }

            Symbol DerefPointers(Symbol type)
                => Deref(type, sDerefPointers);

            // Dereference the given type names
            Symbol Deref(Symbol type, WordSet typeNames)
            {
                // Auto-dereference pointers and references
                if (type is SymSpecializedType genericSym
                    && genericSym.Params.Length != 0
                    && typeNames.Contains(genericSym.Parent.FullName))
                {
                    // Move up to non-generic concrete type
                    // TBD: Preserve concrete type parameters
                    type = genericSym.Params[0];
                }
                return type;
            }

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

                if (leftType.Parent == null || leftType.Parent.FullName != RAW_POINTER_TYPE)
                {
                    Reject(ex.Token, $"Only pointers may be dereferenced, but the type is '${leftType}'");
                    return null;
                }
                var deref = DerefPointers(leftType);
                ex.Token.AddInfo(deref);
                return new Rval(ex.Token, deref);
            }

            Rval GenNewVarsOperator(SyntaxExpr ex)
            {
                if (ex.Count == 0)
                    return null;  // Syntax error

                // Unary operator, create variables
                if (ex.Count == 1)
                    return GenNewVars(ex[0], ex.Token);

                // Binary operator, capture variabes
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
                variables.Token.AddInfo(valueType);
                return value;
            }

            Rval GenNewVars(SyntaxExpr ex, Token rejectToken)
            {
                var newSymbols = new List<Symbol>();
                foreach (var e in ex)
                {
                    if (e.Count == 0 || e.Token == "")
                        return null; // Syntax error

                    var newVarToken = e.Token;
                    if (FindLocal(newVarToken) != null)
                    {
                        Reject(newVarToken, $"'{newVarToken}' is a duplicate symbol.");
                        return null;
                    }
                    var local = new Symbol(SymKind.Local, null, newVarToken);
                    locals[newVarToken] = local;
                    newSymbols.Add(local);

                    // Check for type name
                    if (e.Count >= 1 && e[0].Token != "")
                    {
                        local.Type = Resolver.Resolve(e[0], table, false, currentMethod, fileUses);
                    }
                }

                if (newSymbols.Count == 0)
                {
                    Reject(rejectToken, $"'{rejectToken}' expecting at least 1 symbol");
                    return null;
                }
                if (newSymbols.Count != 1)
                {
                    Reject(rejectToken, $"'{rejectToken}' multiple symbols not supported yet");
                    return null;
                }
                var rval = new Rval(newSymbols[0].Token);
                rval.Symbols.Add(newSymbols[0]);
                return rval;
            }

            Rval GenTernary(SyntaxExpr ex)
            {
                if (HasError(ex))
                    return null;
                var parameters = ex[0];
                if (HasError(parameters))
                    return null;
                if (parameters.Count != 2)
                {
                    Reject(ex.Token, "Expecting two parameters");
                    return null;
                }
                if (parameters[1].Token != ":")
                {
                    Reject(ex.Token, "Expecting second parameter to use ':' for the 'else' part");
                    return null;
                }

                var cond = GenExpr(parameters[0]);
                var condIf = GenExpr(parameters[1][0]);
                var condElse = GenExpr(parameters[1][1]);

                if (cond == null || condIf == null || condElse == null)
                    return null;

                var condType = EvalType(cond);
                var condTypeIf = EvalType(condIf);
                var condTypeElse = EvalType(condElse);

                if (condType != null && condType != typeBool)
                {
                    Reject(ex.Token, $"First parameter must evaluate to 'bool', but it evaluates to '{condType}'");
                    return null;
                }
                if (condTypeIf != null && condTypeElse != null && condTypeIf.FullName != condTypeElse.FullName)
                {
                    Reject(parameters[1].Token, $"Left and right sides must evaluate to same type, but they evaluate to '{condTypeIf}' and '{condTypeElse}'");
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
                var type = Resolver.Resolve(ex[0], table, false, currentMethod, fileUses);
                var expr = GenExpr(ex[1]);

                if (type == null || expr == null)
                    return null;

                if (type.Parent.FullName != RAW_POINTER_TYPE && type != typeU64)
                {
                    Reject(ex.Token, $"The cast type must be a pointer or u64, but it is '{type}'");
                    return null;
                }

                var exprType = EvalType(expr);
                if (exprType == null)
                    return null;

                if (exprType.Parent.FullName != RAW_POINTER_TYPE && exprType != typeU64)
                {
                    Reject(ex.Token, $"The expression must evaluate to a pointer or u64, but is a '{exprType}'");
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
                var refType = table.Lookup(ex.Token == "ref" ? REF_TYPE : RAW_POINTER_TYPE);
                if (refType == null)
                {
                    Reject(ex.Token, "Undefined type");
                    return null;
                }

                return new Rval(ex.Token, 
                    table.GetSpecializedType(refType, new Symbol[] { exprType }));
            }


            Rval GenAssign(SyntaxExpr ex)
            {
                if (ex.Count != 2)
                    return null;  // Syntax error

                var left = GenExpr(ex[0]);
                var right = GenExpr(ex[1]);
                var rightType = right == null ? null : EvalType(right);
                if (left != null)
                    EvalType(left, true);

                if (left == null || right == null || rightType == null)
                    return null;

                // Assign type to local variable (if type not already assigned)
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

                if (leftType.Parent != null && leftType.Parent.FullName == RAW_POINTER_TYPE && rightType == typeNil)
                {
                    return null; // Pointer = nil is ok
                }

                // TBD: Must afigure out how to deal with mut
                if (leftType.FullName != rightType.FullName)
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
                if (returns.Count >= 2)
                {
                    Reject(ex.Token, "Use parenthesis to return multiple values.  TBD: Allow not using them.");
                    return null;
                }

                var returnRval = returns.Count == 0 ? new Rval(ex.Token, table.Lookup("()")) : returns[0];
                var returnType = EvalType(returnRval);
                if (returnType == null)
                    return null;

                ex.Token.AddInfo(returnType);
                var functionReturnType = currentMethod.GetReturnTupleOrType(table);

                // TBD: This is to temporarily gloss over pointers, nullable, and nil
                if (returnType.FullName == "Zurfur.nil"
                    && (functionReturnType.Parent.FullName == "Zurfur.RawPointer`1")
                        || functionReturnType.Parent.FullName == "Zurfur.Nullable`1`1")
                    return returnRval;


                if (functionReturnType.FullName != returnType.FullName)
                {
                    Reject(ex.Token, $"Incorrect return type, expecting '{functionReturnType}', got '{returnType}'");
                }

                return returnRval;
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

                if (args.Count == 2 && (args[0].Type.Parent.FullName == RAW_POINTER_TYPE 
                        || args[1].Type.Parent.FullName == RAW_POINTER_TYPE))
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
                
                var functions = FindGlobal(ex.Token, operatorName);
                if (functions == null)
                    return null;

                var rval = FindCompatibleFunction(new Rval(ex.Token) { Symbols = functions},
                                args,  $" '{operatorName}' (operator '{ex.Token}')");
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
                    if (leftType.Parent.FullName == RAW_POINTER_TYPE && sIntTypeNames.Contains(rightType.FullName))
                        return DummyFunction(ex.Token, leftType, leftType, rightType);
                    if (rightType.Parent.FullName == RAW_POINTER_TYPE && sIntTypeNames.Contains(leftType.FullName))
                        return DummyFunction(ex.Token, rightType, leftType, rightType);
                    if (ex.Token == "-"
                        && leftType.Parent.FullName == RAW_POINTER_TYPE && rightType.Parent.FullName == RAW_POINTER_TYPE
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
                    if (leftType.Parent.FullName == RAW_POINTER_TYPE && rightType.Parent.FullName == RAW_POINTER_TYPE)
                        return DummyFunction(ex.Token, typeBool, leftType, rightType);
                    if (ex.Token == "==" || ex.Token == "!=")
                    {
                        if (leftType.Parent.FullName == RAW_POINTER_TYPE && rightType == typeNil)
                            return DummyFunction(ex.Token, typeBool, leftType, rightType);
                        if (leftType == typeNil && rightType.Parent.FullName == RAW_POINTER_TYPE)
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
                    Reject(ex.Token, $"Operator '{ex.Token}' can only take 'bool' parameters, " 
                        + $"not '{Rval.ParamTypes(args, Array.Empty<Symbol>())}'");
                return DummyFunction(ex.Token, typeBool, typeBool, typeBool);

            }

            Rval GenCall(SyntaxExpr ex)
            {
                if (ex.Count == 0)
                    return null; // Syntax error

                // Generate function call and then parameters
                var call = GenExpr(ex[0]);
                var args = GenCallParams(ex, 1);

                if (call == null)
                    return null;  // Undefined symbol or error evaluating left side

                if (args == null)
                {
                    // Give some feedback on the functions that could be called
                    if (call.Type != null)
                        call.Token.AddInfo(call.Type);
                    foreach (var sym in call.Symbols)
                        call.Token.AddInfo(sym);
                    return null;
                }

                if (call.Type != null)
                {
                    Reject(call.Token, "Method or type name expected");
                    return null;
                }

                if (RejectAmbiguousPrimary(call.Token, call.Symbols))
                    return null;

                // Constructor
                if (call.Symbols[0].IsAnyType)
                    return GenConstructor(call, args);

                // Insert inType type as first parameter so all types match
                // TBD: I don't think this is a long term solution
                if (call.InType != null && !call.InType.IsModule)
                    args.Insert(0, new Rval(call.Token, call.InType));

                var function = FindCompatibleFunction(call, args, $"'{call.Token}'");
                if (function == null)
                    return null;

                if (function.Symbols[0].IsGetter)
                    Reject(ex.Token, "Getter cannot be called with parenthesis");

                if (function.Type.HasGenericArg)
                    function.Type = ReplaceGenericTypeParams(call.Token, function.Type, call.TypeArgs);

                if (function.Type == null)
                    return null;

                return function;
            }


            Rval GenConstructor(Rval call, List<Rval> args)
            {
                call.Token.Type = eTokenType.TypeName;
                var callType = call.Symbols[0];
                if (callType.IsSpecializedType)
                {
                    Reject(call.Token, "Compiler error: Unexpected specialized type");
                    return null;
                }

                var callTypeParamCount = call.TypeArgs?.Length ?? 0;
                if (callType.IsTypeParam)
                {
                    if (callTypeParamCount != 0)
                        Reject(call.Token, $"Expecting 0 type parameters, but got '{callTypeParamCount}'");
                    if (args.Count != 0)
                        Reject(call.Token, $"New generic type with arguments not supported yet");
                    return new Rval(call.Token, table.GetGenericParam(callType.Order));
                }

                if (callTypeParamCount != callType.GenericParamTotal())
                {
                    Reject(call.Token,
                        $"Expecting {callType.GenericParamTotal()} generic parameter(s), but got {callTypeParamCount});");
                }

                // Add type parameters
                if (callTypeParamCount != 0)
                    callType = table.GetSpecializedType(callType, call.TypeArgs);

                // Empty constructor (create a default with any type parameters)
                if (args.Count == 0)
                {
                    // TBD: This overrides all users constructors.  Put this below
                    //      `FindCompatibleFuncton`, refactor so that function
                    //      doesn't mark the error.
                    call.Token.AddInfo(callType);
                    return new Rval(call.Token, callType);
                }

                // Search for `new` function
                Debug.Assert(call.InType == null);
                call.InType = callType;
                call.Symbols = FindInType(call.Token, "new", callType);
                args.Insert(0, new Rval(call.Token, callType));
                var function = FindCompatibleFunction(call, args, $"'new' (constructor for '{call.InType}')");
                if (function == null)
                    return null;

                if (function.Type.HasGenericArg)
                    function.Type = ReplaceGenericTypeParams(call.Token, function.Type, call.TypeArgs);

                if (function.Type == null)
                    return null;

                return function;
            }

            // If there is an error, mark it and give feedback on possible matches.
            Rval FindCompatibleFunction(
                Rval call,
                List<Rval> args, 
                string rejectName)
            {
                var oldCalls = call.Symbols.ToArray();
                call.Symbols.RemoveAll(callFunc => !IsCallCompatible(callFunc, call.TypeArgs, args));


                // TBD: Error messages can be improved a lot.
                if (call.Symbols.Count == 0 && oldCalls.Length >= 1)
                {
                    var numGenericArgs = oldCalls[0].GenericParamTotal();
                    var hasNumGenericArgs = false;
                    for (int i = 0; i < oldCalls.Length; i++)
                        if (numGenericArgs == oldCalls[i].GenericParamTotal())
                            hasNumGenericArgs = true;
                        else
                            numGenericArgs = -1;

                    if (numGenericArgs >= 0 && numGenericArgs != call.TypeArgs.Length)
                    {
                        // Wrong number of generic arguments
                        RejectSymbols(call.Token, oldCalls,
                            $"The function {rejectName} expects {numGenericArgs} "
                                + $"type arguments, but {call.TypeArgs.Length} were supplied");
                        return null;
                    }
                    if (!hasNumGenericArgs)
                    {
                        // Nothing has correct number of generic arguments
                        RejectSymbols(call.Token, oldCalls,
                            $"The function {rejectName} does not have "
                                + $"an overload with {call.TypeArgs.Length} type arguments");
                        return null;
                    }

                }

                if (call.Symbols.Count == 0)
                {
                    // Incorrect type
                    RejectSymbols(call.Token, oldCalls, 
                        $"No function {rejectName} taking '{Rval.ParamTypes(args, call.TypeArgs)}' in scope");
                    return null;
                }

                if (call.Symbols.Count != 1)
                {
                    RejectSymbols(call.Token, call.Symbols, 
                        $"Multiple functions {rejectName} taking '{Rval.ParamTypes(args, call.TypeArgs)}' in scope");
                    return null;
                }

                var method = (SymMethod)call.Symbols[0];
                call.Token.AddInfo(method);
                
                return new Rval(call.Token) { 
                    Type = method.GetReturnTupleOrType(table), 
                    Symbols = call.Symbols 
                };

            }

            bool IsCallCompatible(Symbol symbol, Symbol []typeArgs, List<Rval> args)
            {
                var func = symbol as SymMethod;
                if (func == null)
                    return false;

                var methodParamTypes = func.GetParamTuple(table).GetTupleTypeList(table);
                if (args.Count != methodParamTypes.Count)
                    return false;

                for (var i = 0; i < args.Count; i++)
                {
                    if (methodParamTypes[i] == null)
                        return false;

                    var arg = args[i].Type;
                    var param = methodParamTypes[i];

                    // Implicit conversion from *Type to *void
                    if (arg.Parent.FullName == RAW_POINTER_TYPE && param.Parent.FullName == RAW_POINTER_TYPE
                        && DerefPointers(param) == typeVoid)
                    {
                        continue;
                    }

                    if (!MatchWithGenerics(func, arg, typeArgs, param))
                        return false;
                }
                return true;
            }

            bool MatchWithGenerics(SymMethod func, Symbol arg, Symbol []typeArgs, Symbol param)
            {
                if (param.HasGenericArg)
                {
                    if (func.GenericParamTotal() != typeArgs.Length)
                        return false;
                    param = ReplaceGenericTypeParams(func.Token, param, typeArgs);
                }

                if (arg.FullName == param.FullName || param.IsGenericArg)
                    return true;

                return false;
            }

            // Replace the generic type argument with the given argument,
            // return the result, but don't change the original.
            Symbol ReplaceGenericTypeParams(Token token, Symbol type, Symbol[] args)
            {
                if (!type.IsSpecializedType)
                    return type;

                if (type.IsGenericArg)
                {
                    if (type.Order >= 0 && type.Order < args.Length)
                        return args[type.Order];
                    Reject(token, "Compiler error: Index out of range");
                    return type;
                }

                var specType = (SymSpecializedType)type;
                return table.GetSpecializedType(type.Parent,
                    NewGenericTypeParams(token, specType.Params, args));
            }

            // Replace the generic type argument with the given argument,
            // return the result, but don't change the original.
            Symbol[] NewGenericTypeParams(Token token, Symbol[] types, Symbol[] args)
            {
                if (types == null || types.Length == 0)
                    return types;
                var newTypes = new Symbol[types.Length];
                for (int i = 0; i < types.Length; i++)
                    newTypes[i] = ReplaceGenericTypeParams(token, types[i], args);
                return newTypes;
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


            // Get return type of symbol.
            // Returns null if symbol is not found, is unresolved, or ambiguous.
            // When ignoreLocalUntypedError is true, the symbol may be an unresolved local.
            // Mark an error when there is no match.
            Symbol EvalType(Rval rval, bool ignoreLocalUntypedError = false, bool allowTypeItself = false)
            {
                //Debug.Assert(rval.Type == null || rval.Symbols.Count == 0);
                // Done if we already have return type
                if (rval.Type != null)
                    return rval.Type;

                var symbols = rval.Symbols;
                if (symbols.Count == 0)
                    return null;

                if (RejectAmbiguousPrimary(rval.Token, rval.Symbols))
                    return null;

                // Filter out functions, except getters
                var oldCalls = symbols.ToArray();
                symbols.RemoveAll(callFunc => callFunc.IsMethod && !callFunc.IsGetter);
                var token = rval.Token;
                if (symbols.Count == 0)
                {
                    RejectSymbols(token, oldCalls,
                        $"'{token}' can't find variable, field, or getter function");
                    return null;
                }

                // Don't allow multiple symbols
                if (symbols.Count != 1)
                {
                    RejectSymbols(token, symbols,  $"'{token}' is ambiguous");
                    return null;
                }

                var sym = symbols[0];
                token.AddInfo(sym);

                if (sym.IsAnyType)
                {
                    token.Type = eTokenType.TypeName;
                    if (allowTypeItself || sym.FullName == "Zurfur.nil")
                    {
                        rval.Type = sym;
                        return sym;
                    }
                    else
                    {
                        Reject(token, $"'{token}' is a {sym.KindName}, which is not valid when used like this");
                        return null;
                    }
                }

                if (sym.IsLocal)
                {
                    if (sym.Type == null && !ignoreLocalUntypedError)
                        Reject(token, $"'{token}'  has an unresolved type");
                    rval.Type = sym.Type;
                    return sym.Type;
                }
                if (sym.IsField || sym.IsMethodParam)
                {
                    if (sym.Type == null)
                        Reject(token, $"'{token}'  has an unresolved type");
                    rval.Type = sym.Type;
                    return sym.Type;
                }
                if (sym.IsMethod)
                {
                    var returnType = ((SymMethod)sym).GetReturnTupleOrType(table);
                    rval.Type = returnType;
                    return returnType;
                }
                Reject(token, $"'{token}' compiler failure: '{sym}' is {sym.KindName}");
                Debug.Assert(false);
                return null;
            }

            /// <summary>
            /// Find symbols in the type, including methods defined in
            /// the type's module, this module, or use statements.
            /// Returns an empty symbols list if none found.
            /// </summary>
            List<Symbol> FindInType(Token token, string name, Symbol inType)
            {
                // Find symbols defined in the type (specialized and un-specialized)
                var symbols = new List<Symbol>();
                AddSymbolsNamed(name, inType, symbols);
                if (inType is SymSpecializedType)
                    AddSymbolsNamed(name, inType.Parent, symbols);

                // Find methods in the type's module and current module
                AddMethodsNamedInModule(name, inType.Parent, inType, symbols);
                AddMethodsNamedInModule(name, currentMethod.Parent, inType, symbols);

                RemoveLastDuplicates(symbols);

                return symbols;
            }

            // Add all children with the given name (primary or non-extension method)
            void AddSymbolsNamed(string name, Symbol inType, List<Symbol> symbols)
            {
                if (inType.TryGetPrimary(name, out Symbol sym))
                    symbols.Add(sym);
                if (inType.HasMethodNamed(name))
                    foreach (var child in inType.Children)
                        if (child.IsMethod && !child.IsExtension && child.Token == name)
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
                if (withMethodType.IsSpecializedType)
                    withMethodType = withMethodType.Parent;

                foreach (var child in inModule.Children)
                {
                    if (!child.IsMethod || !child.IsExtension || child.Token != name)
                        continue;
                    var parameters = ((SymMethod)child).GetParamTuple(table).GetTupleTypeList(table);
                    if (parameters.Count == 0)
                        continue;

                    // Compare the non-specialized type
                    //      e.g: List<#1> matches List<byte> so we get all functions
                    var paramType = parameters[0];
                    if (paramType.IsSpecializedType)
                        paramType = paramType.Parent;
                   
                    if (paramType.FullName != withMethodType.FullName)
                        continue;

                    symbols.Add(child);
                }
            }

            /// <summary>
            /// Find symbols in the local/global scope that match this
            /// token.  If it's a local or parameter in the current
            /// method, stop searching.  Otherwise find a list of matching
            /// symbols in the current module or use symbols.
            /// Returns NULL and rejects token on error.
            /// </summary>
            List<Symbol> FindGlobal(Token token, string name)
            {
                var symbols = new List<Symbol>();
                var local = FindLocal(name);
                if (local != null)
                {
                    symbols.Add(local);
                    return symbols;
                }

                // Find global symbols in this module
                var module = currentMethod.Parent;
                if (module.TryGetPrimary(name, out Symbol sym1))
                    symbols.Add(sym1);
                if (module.HasMethodNamed(name))
                    foreach (var child in module.Children)
                        if (child.IsMethod && child.Token == name && !child.IsExtension)
                            symbols.Add(child);

                // Search 'use' symbol
                if (fileUses.UseSymbols.TryGetValue(name, out var useSymbols))
                {
                    foreach (var sym2 in useSymbols)
                        if (!sym2.IsExtension && !symbols.Contains(sym2))
                            symbols.Add(sym2);
                }

                if (symbols.Count == 0)
                {
                    Reject(token, $"'{name}' is an undefined symbol");
                    return null;
                }

                RemoveLastDuplicates(symbols);
                return symbols;
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

            // Only allow one primary or multiple functions, but not both.
            // On error, reject and clear the symbols.
            bool RejectAmbiguousPrimary(Token token, List<Symbol> symbols)
            {
                var methods = 0;
                var primaries = 0;
                foreach (var symbol in symbols)
                    if (symbol.IsMethod)
                        methods++;
                    else
                        primaries++;

                var reject = primaries >= 2 || primaries == 1 && methods >= 1;
                if (reject)
                    RejectSymbols(token, symbols,
                        $"'{token}' is ambiguous because there are both functions and types (or fields) with the same name");
                return reject;
            }

            void RejectSymbols(Token token, IEnumerable<Symbol> symbols, string message)
            {
                Reject(token, message);
                foreach (var sym in symbols)
                    token.AddInfo(sym);
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
