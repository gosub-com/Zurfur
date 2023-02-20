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
        public string Name;
        public List<Symbol> Candidates = new();
        public Symbol Type;
        public Symbol[] TypeArgs = Array.Empty<Symbol>();
        public Symbol InType;
        public bool IsUntypedConst; // NOTE: 3int is a typed const
        public bool IsSetter;
        public bool IsLocal;
        public bool IsExplicitRef;
        public bool IsStatic;

        public Rval(Token token)
        {
            Token = token;
            Name = token.Name;
        }

        public Rval(Token token, string name)
        {
            Token = token;
            Name = name;
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
            return $"Token: '{Token}'{typeParams}"
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

    enum CallCompatible
    {
        Compatible = 0,
        NotAFunction = 1,
        StaticCallToNonStaticMethod = 2,
        NonStaticCallToStaticMethod = 4,
        IncompatibleParameterTypes = 8,
        ExpectingSomeTypeArgs = 16,
        ExpectingNoTypeArgs = 32,
        WrongNumberOfTypeArgs = 64,
        WrongNumberOfParameters = 128,
        TypeArgsSuppliedByConstraint = 256
    }

    class LocalSymbol
    {
        public int ScopeNum;
        public Symbol Symbol;

        public LocalSymbol(int scopeNum, Symbol symbol)
        {
            ScopeNum = scopeNum;
            Symbol = symbol;
        }
    }

    /// <summary>
    /// Compile the code given the output of header file generation.
    /// </summary>
    static class CompileCode
    {
        const string RAW_POINTER_TYPE = "Zurfur.RawPointer`1";
        const string REF_TYPE = "Zurfur.Ref`1";
        const string NIL_TYPE = "Zurfur.nil";
        const string NILABLE_TYPE = "Zurfur.Nilable`1";
        const string RESULT_TYPE = "Zurfur.Result`1";

        static WordSet sOperators = new WordSet("+ - * / % & | ~ == != >= <= > < << >> and or not in |= &= += -= <<= >>= .. ..+ ]");
        static WordSet sCmpOperators = new WordSet("== != >= <= > <");
        static WordSet sIntTypeNames = new WordSet("Zurfur.int Zurfur.u64 Zurfur.i32 Zurfur.u32");
        static WordSet sDerefRef = new WordSet("Zurfur.Ref`1");
        static WordSet sDerefPointers = new WordSet("Zurfur.RawPointer`1 Zurfur.Pointer`1");
        static WordMap<string> sBinOpNames = new WordMap<string> {
            {"+", "_opAdd"}, {"+=", "_opAdd"}, {"-", "_opSub"}, {"-=", "_opSub"},
            {"*", "_opMul"}, {"*=", "_opMul"}, {"/", "_opDiv"}, {"/=", "_opDiv"},
            {"%","_opRem" }, {"%=","_opRem" },
            {"..", "_opRange" }, {"..+", "_opRange" },
            {"==", "_opEq"}, {"!=", "_opEq" }, { "not", "_opEq" }, {"in", "_opIn" },
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
                foreach (var synFunc in syntaxFile.Value.Functions)
                {
                    // Get current function
                    if (!syntaxToSymbol.TryGetValue(synFunc, out var currentFunction))
                        continue; // Syntax error
                    Debug.Assert(currentFunction.IsFun);
                    GenFunction(synFunc, table, fileUses, currentFunction);
                }
            }
        }

        static void GenFunction(
            SyntaxFunc synFunc,
            SymbolTable table,
            UseSymbolsFile fileUses,
            Symbol currentFunction)
        {
            Debug.Assert(currentFunction.IsFun);
            var typeVoid = table.Lookup("Zurfur.void");
            var typeNil = table.Lookup("Zurfur.nil");
            var typeInt = table.Lookup("Zurfur.int");
            var typeU64 = table.Lookup("Zurfur.u64");
            var typeI32 = table.Lookup("Zurfur.i32");
            var typeU32 = table.Lookup("Zurfur.u32");
            var typeStr = table.Lookup("Zurfur.str");
            var typeBool = table.Lookup("Zurfur.bool");
            var typeByte = table.Lookup("Zurfur.byte");
            var typeF64 = table.Lookup("Zurfur.f64");
            var typeF32 = table.Lookup("Zurfur.f32");

            Debug.Assert(typeVoid != null 
                && typeNil != null
                && typeInt != null
                && typeU64 != null
                && typeI32 != null
                && typeStr != null 
                && typeBool != null
                && typeByte != null
                && typeF64 != null
                && typeF32 != null);

            var locals = new Dictionary<string, LocalSymbol>();
            var scopeNum = 0;

            BeginLocalScope();
            if (synFunc.Statements != null)
                GenStatements(synFunc.Statements);
            EndLocalScope();

            return;


            void GenStatements(SyntaxExpr ex)
            {
                for (int i = 0;  i < ex.Count;  i++)
                {
                    int evalIndex = i;
                    try
                    {
                        GenStatement(ex, ref i);
                    }
                    catch (Exception err)
                    {
                        Debug.Assert(false);
                        RejectExpr(ex[evalIndex], err.Message + "\r\n\r\n" + err.StackTrace);
                    }
                }
            }

            // Generate a statement, scoop up compund parts
            void GenStatement(SyntaxExpr ex, ref int index)
            {
                var s = ex[index];
                var name = s.Token.Name;
                if (name == "while")
                    GenWhileStatement(s);
                else if (name == "scope")
                    GenScopeStatement(s);
                else if (name == "do")
                    index = GenDoStatement(ex, index);
                else if (name == "if")
                    index = GenIfStatement(ex, index);
                else if (name == "break")
                    GenBreakStatement(s);
                else if (name == "continue")
                    GenContinueStatement(s);
                else if (name == "for")
                    GenForStatement(s);
                else if (name == "return")
                    GenReturnStatement(s);
                else
                    EvalTypeStatement(GenExpr(s));
            }

            void GenWhileStatement(SyntaxExpr ex)
            {
                var cond = GenExpr(ex[0]);
                EvalType(cond);
                CheckBool(ex[0].Token, cond?.Type, "if");
                BeginLocalScope();
                GenStatements(ex[1]);
                EndLocalScope();
            }

            void GenScopeStatement(SyntaxExpr ex)
            {
                BeginLocalScope();
                GenStatements(ex[0]);
                EndLocalScope();
            }

            int GenDoStatement(SyntaxExpr s, int i)
            {
                BeginLocalScope();
                GenStatements(s[i]);
                if (i+1 < s.Count && s[i+1].Token == "dowhile")
                {
                    i += 1;
                    var cond = GenExpr(s[i][0]);
                    EvalType(cond);
                    CheckBool(s[i].Token, cond?.Type, "dowhile");
                }
                else
                {
                    if (!HasError(s[i]))
                        Reject(s[i].Token, "Expecting 'dowhile'");
                }
                EndLocalScope();
                return i;
            }

            int GenIfStatement(SyntaxExpr s, int i)
            {
                // If...
                GenIfCondStatement(s[i]);

                // ...elif...
                while (i+1 < s.Count && s[i+1].Token == "elif")
                {
                    i += 1;
                    GenIfCondStatement(s[i]);
                }

                // ...else
                if (i + 1 < s.Count && s[i+1].Token == "else")
                {
                    i += 1;
                    BeginLocalScope();
                    GenStatements(s[i][0]);
                    EndLocalScope();
                }
                return i;
            }

            void GenIfCondStatement(SyntaxExpr ex)
            {
                var cond = GenExpr(ex[0]);
                EvalType(cond);
                CheckBool(ex[0].Token, cond?.Type, "if");
                BeginLocalScope();
                GenStatements(ex[1]);
                EndLocalScope();
            }

            bool CheckBool(Token token, Symbol conditionType, string name)
            {
                if (conditionType == null)
                    return false;
                if (DerefRef(conditionType).FullName != "Zurfur.bool")
                {
                    Reject(token, $"'{name}' condition must evaluate to 'bool', but it evaluates to '{conditionType}'");
                    return false;
                }
                return true;
            }


            void GenBreakStatement(SyntaxExpr ex)
            {
                // TBD
            }

            void GenContinueStatement(SyntaxExpr ex)
            {
                // TBD
            }

            void GenForStatement(SyntaxExpr ex)
            {
                if (ex.Count != 3)
                    return; // Syntax error

                BeginLocalScope();

                var local = CreateLocal(ex[0].Token);
                if (local != null)
                    local.Token.AddInfo(local);
                var inRval = GenExpr(ex[1]);
                EvalType(inRval);

                if (inRval != null && inRval.Type != null)
                {
                    var iterType = GenForIterator(ex[0].Token, inRval.Type);
                    if (iterType != null && local != null)
                        local.Type = iterType;
                }

                var loopExpr = GenExpr(ex[2]);
                if (loopExpr != null)
                    EvalType(loopExpr);

                EndLocalScope();
            }

            // Get the for loop iterator type, e.g. T in object.iterator.next() -> Nilable<T>
            Symbol GenForIterator(Token token, Symbol inType)
            {
                var getIter = FindAndCall(token, DerefRef(inType), "iterator");
                if (getIter == null)
                    return null;
                var getNext = FindAndCall(token, getIter, "next");
                if (getNext == null)
                    return null;
                if (!getNext.IsSpecialized
                    || getNext.TypeArgs.Length != 1
                    || getNext.Parent.FullName != NILABLE_TYPE)
                {
                    Reject(token, $"Expecting the function '{getIter}.next()' to return a Nilable<T>, but it returns '{getNext}'");
                    return null;
                }
                return getNext.TypeArgs[0];
            }

            Rval GenReturnStatement(SyntaxExpr ex)
            {
                var returns = GenCallParams(ex, 0);
                if (returns == null)
                    return null;
                if (returns.Count >= 2)
                {
                    Reject(ex.Token, "Use parenthesis to return multiple values.  TBD: Allow not using them.");
                    return null;
                }

                var rval = returns.Count == 0 ? new Rval(ex.Token) { Type = table.EmptyTuple } : returns[0];
                EvalType(rval);
                if (rval == null || rval.Type == null)
                    return null;

                ex.Token.AddInfo(rval.Type);
                var functionType = currentFunction.FunReturnTupleOrType;

                // TBD: This is to temporarily gloss over pointers, Nilable, and nil
                if (rval.Type.FullName == NIL_TYPE
                    && (functionType.Parent.FullName == RAW_POINTER_TYPE)
                        || functionType.Parent.FullName == NILABLE_TYPE)
                    return rval;

                if (!TypesMatch(DerefRef(functionType), DerefRef(rval.Type)))
                {
                    Reject(ex.Token, $"Incorrect return type, expecting '{functionType}', got '{rval.Type}'");
                }
                return rval;
            }



            // Evaluate an expression.  When null is returned, the error is already marked.
            Rval GenExpr(SyntaxExpr ex)
            {
                var token = ex.Token;
                var name = token.Name;
                if (name == "")
                {
                    Reject(token, "Compiler error: GenExpr, not compiled");
                    return null;  // Syntax error should already be marked
                }

                // Terminals: Number, string, identifier
                if (char.IsDigit(name[0]))
                    return GenConstNumber(ex);
                else if (name == "\"" || name == "\"\"\"")
                    return new Rval(token) { Type = typeStr };
                else if (name == "nil")
                {
                    ex.Token.Type = eTokenType.TypeName;
                    return new Rval(token) { Type = typeNil };
                }
                else if (name == "my")
                {
                    ex.Token.Type = eTokenType.ReservedVar;
                    return GenIdentifier(ex);
                }
                else if (name == "My")
                    return GenMy(ex);
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
                else if (name == "if")
                    return GenIfExpr(ex);
                else if (name == "ref")
                    return GenRefOrAddressOf(ex);
                else if (name == "sizeof")
                    return new Rval(token) { Type = typeInt };
                else if (name == "true" || name == "false")
                    return new Rval(token) { Type = typeBool };
                else if (name == "astart")
                    return GenAstart(ex);
                else if (name == "?")
                    return GenDefaultOperator(ex);
                else if (name == "!")
                    return GenErrorOperator(ex);
                else if (name == ";" || name == "{")
                    GenStatements(ex);
                else if ((char.IsLetter(name[0]) || name[0] == '_') && !ParseZurf.ReservedWords.Contains(name))
                    return GenIdentifier(ex);
                else
                {
                    if (name == "else" || name == "elif" || name == "dowhile")
                        Reject(ex.Token, $"Unexpected '{name}' statement");
                    else
                        Reject(ex.Token, "Not compiled yet");
                    GenCallParams(ex, 0);
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
                    else if (customType == "byte")
                        numberType = typeByte;
                    else
                        Reject(ex[0].Token, $"'{ex[0].Token}' undefined number type");
                }
                var rval = new Rval(ex.Token) { Type = numberType, IsUntypedConst = untypedConst };

                return rval;
            }


            Rval GenIdentifier(SyntaxExpr ex)
            {
                return new Rval(ex.Token) { Candidates = FindGlobal(ex.Token, ex.Token) };
            }

            Rval GenMy(SyntaxExpr ex)
            {
                ex.Token.Type = eTokenType.ReservedType;
                var my = Resolver.ResolveMy(table, ex.Token, currentFunction);
                if (my == null)
                    return null;
                return new Rval(ex.Token) { Candidates = new() { my } };
            }

            // Type or function call (e.g. List<int>(), f<int>(), etc)
            Rval GenTypeArgs(SyntaxExpr ex)
            {
                var symbols = GenExpr(ex[0]);
                var typeParams = Resolver.ResolveTypeArgs(ex, table, currentFunction, fileUses);
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
                if (ex.Count == 1)
                    return GenExpr(ex[0]);

                var args = GenCallParams(ex, 0);
                if (args == null)
                    return null;

                var types = args.Select(t => t.Type).ToArray();

                var  tuple = table.CreateSpecializedType(table.GetTupleBaseType(types.Length), types);
                return new Rval(ex.Token) { Type = tuple };
            }

            // Check top level for syntax error so we can skip compiling stuff
            // that always fails, giving the user un-necessary error messages.
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
                EvalType(leftDot, false, true);
                var leftType = leftDot.Type;
                if (leftType == null)
                    return null;

                // Automatically dereference pointers, etc
                var identifier = ex[1].Token;
                leftType = DerefRef(leftType);
                leftType = DerefPointers(leftType);

                // Find in tuple
                // TBD: Consider moving to EvalType or FindInType
                if (leftType.IsTuple && leftType.IsSpecialized)
                {
                    if (leftType.TupleNames.Length == 0)
                    {
                        Reject(ex.Token, $"The type '{leftType}' is an anonymous type without field names, so cannot be resolved with '.'");
                        return null;
                    }
                    var i = Array.IndexOf(leftType.TupleNames, identifier);
                    if (i < 0)
                    {
                        Reject(identifier, $"'{identifier}' is an undefined symbol in the named tuple '{leftType}'");
                        return null;
                    }
                    return new Rval(identifier) { Type = leftType.TypeArgs[i] };
                }
                return new Rval(identifier) { 
                    InType = leftType, 
                    Candidates = FindInType(identifier, leftType),
                    IsStatic = leftDot.IsStatic
                };
            }

            Symbol DerefRef(Symbol type) => Deref(type, sDerefRef);
            Symbol DerefPointers(Symbol type) => Deref(type, sDerefPointers);

            // Dereference the given type names
            Symbol Deref(Symbol type, WordSet typeNames)
            {
                if (type == null)
                    return null;

                // Auto-dereference pointers and references
                if (type.IsSpecialized
                    && type.TypeArgs.Length != 0
                    && typeNames.Contains(type.Parent.FullName))
                {
                    // Move up to non-generic concrete type
                    // TBD: Preserve concrete type parameters
                    type = type.TypeArgs[0];
                }
                return type;
            }

            Rval GenDotStar(SyntaxExpr ex)
            {
                if (ex.Count != 1)
                    return null; // Syntax error
                var left = GenExpr(ex[0]);
                EvalType(left);
                if (left == null || left.Type == null)
                    return null;

                left.Type = DerefRef(left.Type);
                if (left.Type.Parent.FullName != RAW_POINTER_TYPE)
                {
                    Reject(ex.Token, $"Only pointers may be dereferenced, but the type is '${left.Type}'");
                    return null;
                }
                left.Type = DerefPointers(left.Type);
                MakeIntoRef(left);
                ex.Token.AddInfo(left.Type);
                return new Rval(ex.Token) { Type = left.Type };
            }

            Rval GenNewVarsOperator(SyntaxExpr ex)
            {
                ex.Token.Type = eTokenType.NewVarSymbol;
                if (ex.Count == 0)
                    return null;  // Syntax error
                if (ex.Count == 1)
                    return GenNewVarsUnary(ex[0], ex.Token);
                return GenNewVarsBinary(ex);
            }

            Rval GenNewVarsBinary(SyntaxExpr ex)
            {
                // Binary operator, capture variabe
                var value = GenExpr(ex[0]);
                var variables = GenNewVarsUnary(ex[1], ex.Token);

                if (value == null || variables == null)
                    return null;

                EvalType(value);
                var valueType = value.Type;
                if (valueType == null)
                    return null;

                // Assign type to local variable
                Debug.Assert(variables.Candidates.Count == 1); // TBD: Multiple symbols
                var localVariable = variables.Candidates[0];
                Debug.Assert(localVariable.IsLocal);
                variables.Token.AddInfo(localVariable);
                if (localVariable.Type != null)
                {
                    Reject(variables.Token, "New variables in binary '@' operator must not have types");
                    return null;
                }
                // Implicitly convert Nilable<T> to bool and T
                if (valueType.Parent.FullName == NILABLE_TYPE && valueType.TypeArgs.Length == 1)
                {
                    localVariable.Type = valueType.TypeArgs[0];
                    return new Rval(value.Token) { Type = typeBool };
                }

                localVariable.Type = valueType;
                return value;
            }

            // Still doesn't resolve multiple symbols '@(v1, v2)', etc.
            Rval GenNewVarsUnary(SyntaxExpr ex, Token rejectToken)
            {
                var newSymbols = new List<Symbol>();
                foreach (var e in ex)
                {
                    if (e.Count == 0 || e.Token == "")
                        return null; // Syntax error

                    var local = CreateLocal(e.Token);
                    if (local == null)
                        return null;
                    newSymbols.Add(local);

                    // Resolve type (if given)
                    if (e.Count >= 1 && e[0].Token != "")
                        local.Type = Resolver.Resolve(e[0], table, false, currentFunction, fileUses);
                }

                if (newSymbols.Count == 0)
                    return null; // Syntax error

                if (newSymbols.Count != 1)
                {
                    Reject(rejectToken, $"'{rejectToken}' multiple symbols not supported yet");
                    return null;
                }
                return new Rval(newSymbols[0].Token) { Candidates = newSymbols };
            }

            Rval GenRefOrAddressOf(SyntaxExpr ex)
            {
                if (ex.Count != 1)
                    return null; // Syntax error
                Debug.Assert(ex.Token == "ref" || ex.Token == "&");

                var rval = GenExpr(ex[0]);
                if (rval == null)
                    return null;

                EvalType(rval);
                if (rval.Type == null)
                    return null;

                if (rval.Type.Parent.FullName != REF_TYPE && !rval.IsLocal)
                    Reject(ex.Token, $"The type '{rval.Type} is a value and cannot be converted to a reference'");

                if (ex.Token == "ref")
                {
                    // TBD: The thing should already be a reference, or fail same as addrss off
                    rval.IsExplicitRef = true;
                    if (rval.Type.Parent.FullName != REF_TYPE)
                        MakeIntoRef(rval);
                }
                else
                {
                    rval.Type = DerefRef(rval.Type);
                    MakeIntoRef(rval, true);
                }

                ex.Token.AddInfo(rval.Type);
                return rval;
            }

            // Convert type into reference to type (or raw pointer to type)
            void MakeIntoRef(Rval rval, bool rawPointer = false)
            {
                var type = rval.Type;
                if (type == null)
                    return; // Error already marked

                if (type.IsSpecialized && type.Parent.FullName == REF_TYPE)
                {
                    Reject(rval.Token, "Cannot take address of a reference");
                    return;
                }

                // Ref or address off
                var refType = table.Lookup(rawPointer ? RAW_POINTER_TYPE : REF_TYPE);
                if (refType == null)
                    throw new Exception("Compiler error: MakeIntoRef, undefined type in base library");

                rval.Type = table.CreateSpecializedType(refType, new Symbol[] { rval.Type });
            }


            Rval GenAssign(SyntaxExpr ex)
            {
                if (ex.Count != 2)
                    return null;  // Syntax error

                var left = GenExpr(ex[0]);
                var right = GenExpr(ex[1]);
                EvalType(right);
                var assignedSymbol = EvalType(left, true);
                var rightType = right?.Type;
                if (left == null || right == null || rightType == null)
                    return null;

                // Assign type to local variable (if type not already assigned)
                var isLocal = assignedSymbol != null 
                    && (assignedSymbol.IsLocal || assignedSymbol.IsFunParam);

                if (isLocal && assignedSymbol.Type == null)
                {
                    // Assign untyped local (not a reference unless explicit 'ref')
                    left.Type = right.IsExplicitRef ? rightType : DerefRef(rightType);
                    assignedSymbol.Type = left.Type;
                }

                var leftType = left.Type;
                if (leftType == null)
                {
                    Reject(ex.Token, "Unresolved type");
                    return null;
                }

                // TBD: Calculate const at run time
                // For now, pretend a constant int can be converted to any number type
                if (sIntTypeNames.Contains(DerefRef(leftType).FullName)
                        && right.IsUntypedConst 
                        && sIntTypeNames.Contains(DerefRef(rightType).FullName))
                    rightType = leftType; // Dummy

                // Assigning nil to a pointer is ok
                if (leftType.Parent != null 
                    && DerefRef(leftType).Parent.FullName == RAW_POINTER_TYPE 
                    && rightType.FullName == NIL_TYPE)
                {
                    return null; // Pointer = nil
                }

                // A setter is a function call
                if (left.IsSetter)
                {
                    if (leftType.FullName != table.EmptyTuple.FullName)
                    {
                        // TBD: Enforce by verifier
                        Reject(ex.Token, "Setter must not return a value");
                        return null;
                    }
                    if (!assignedSymbol.IsFun)
                        throw new Exception("Compiler error: GenAssign, setter index out of range or not method");

                    var args = assignedSymbol.FunParamTypes;
                    if (args.Length != 2)
                    {
                        Reject(ex.Token, "Expecting two parameters (function type, and value type), static setters not supported yet");
                        return null;
                    }
                    if (!TypesMatch(args[1], rightType))
                    {
                        Reject(ex.Token, $"Types must match: setter({args[1].FullName}) = ({rightType.FullName})");
                        return null;
                    }
                    // Debug, TBD: Remove or get better compiler feedback system
                    ex.Token.AddInfo($"setter({args[1].FullName}) = ({rightType.FullName})");

                    // TBD: Need to make this into a function call
                    return null;
                }

                if (!isLocal && leftType.Parent.FullName != REF_TYPE)
                {
                    Reject(ex.Token, "A value cannot be assigned.  Left side must be a local variable or a 'mut' reference.  "
                        + $" ({leftType.FullName}) = ({rightType.FullName})");
                    return null;
                }

                if (!TypesMatch(DerefRef(leftType), DerefRef(rightType)))
                {
                    Reject(ex.Token, $"Types must match: ({leftType.FullName}) = ({rightType.FullName})");
                    return null;
                }
                // Debug, TBD: Remove or get better compiler feedback system
                ex.Token.AddInfo($"({leftType.FullName}) = ({rightType.FullName})");
                return null;
            }

            // TBD: Remove this
            Rval DummyFunction(Token token, Symbol returnType, Symbol arg1Type, Symbol arg2Type)
            {
                var opFunc = new Symbol(SymKind.Fun, currentFunction.Parent, token, $"op{token}({arg1Type},{arg2Type})({returnType})");
                opFunc.Type = returnType;
                token.AddInfo(opFunc);
                return new Rval(token) { Type = returnType };
            }

            Rval GenAstart(SyntaxExpr ex)
            {
                //Reject(ex.Token, "Not implemented yet");
                GenCallParams(ex, 0);
                return null;
            }

            Rval GenDefaultOperator(SyntaxExpr ex)
            {
                Reject(ex.Token, "Not compiled yet");
                return null;
            }

            Rval GenErrorOperator(SyntaxExpr ex)
            {
                var op = GenExpr(ex[0]);
                EvalType(op);
                if (op == null || op.Type == null)
                    return null;
                if (op.Type.Parent.FullName != RESULT_TYPE && op.Type.Parent.FullName != NILABLE_TYPE
                        || op.Type.TypeArgs.Length != 1)
                {
                    Reject(ex.Token, $"Expecting 'Result<T>' or 'Nilable<T>', but got '{op.Type}'");
                    return null;
                }
                return new Rval(ex.Token) { Type = op.Type.TypeArgs[0] };
            }


            Rval GenOperator(SyntaxExpr ex)
            {
                if (ex.Count == 1 && ex.Token == "&")
                    return GenRefOrAddressOf(ex);

                var args = GenCallParams(ex, 0);
                if (args == null || args.Count == 0)
                    return null;  // Unresolved type or syntax error

                if (ex.Token == "and" || ex.Token == "or" || ex.Token == "not")
                    return GenBooleanOperator(ex, args);

                if (args.Count == 2 && (DerefRef(args[0].Type).Parent.FullName == RAW_POINTER_TYPE 
                        || DerefRef(args[1].Type).Parent.FullName == RAW_POINTER_TYPE))
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
                            && sIntTypeNames.Contains(DerefRef(left.Type).FullName) && sIntTypeNames.Contains(right.Type.FullName))
                        right.Type = left.Type;
                    if (!right.IsUntypedConst && left.IsUntypedConst 
                            && sIntTypeNames.Contains(DerefRef(right.Type).FullName) && sIntTypeNames.Contains(left.Type.FullName))
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

                // Find static operators from either argument
                var call = new Rval(ex.Token, operatorName) { IsStatic = true };
                var candidates = FindInType(operatorName, DerefRef(args[0].Type));
                if (args.Count >= 2)
                    candidates.AddRange(FindInType(operatorName, DerefRef(args[1].Type)));
                RemoveLastDuplicates(candidates);

                if (candidates.Count == 0)
                {
                    Reject(ex.Token, $"No function '{operatorName}' (operator '{ex.Token}') " 
                        + $"taking '{Rval.ParamTypes(args, call.TypeArgs)}' is in scope.");
                    return null;
                }
                call.Candidates = candidates;
                call.InType = table.WildCard;
                var rval = FindCompatibleFunction(call, args,
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
                var leftType = DerefRef(left.Type);
                var rightType = DerefRef(right.Type);
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
                        if (leftType.Parent.FullName == RAW_POINTER_TYPE && rightType.FullName == NIL_TYPE)
                            return DummyFunction(ex.Token, typeBool, leftType, rightType);
                        if (leftType.FullName == NIL_TYPE && rightType.Parent.FullName == RAW_POINTER_TYPE)
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

                if (call.Candidates != null && call.Candidates.Count == 1 && call.Candidates[0].IsAnyType)
                    call = FindCompatibleConstructor(call, args);
                else
                    call = FindCompatibleFunction(call, args, $"'{call.Token}'");
                if (call == null)
                    return null;

                // TBD: Mark error when getter is called with parenthesis
                //if (symbols[0].IsGetter)
                //    Reject(ex.Token, "Getter cannot be called with parenthesis");

                if (call.Type == null)
                    return null;

                return call;
            }


            Rval GenIfExpr(SyntaxExpr ex)
            {
                if (HasError(ex))
                    return null;
                if (ex.Count != 1 || ex[0].Count != 2)
                    throw new Exception("Compiler error: GenIfExpr, incorrect number of parameters");

                var parameters = ex[0];
                if (HasError(parameters))
                    return null;
                if (parameters[1].Token != ":")
                {
                    Reject(ex.Token, "Expecting second parameter to use ':' for the 'else' part");
                    return null;
                }
                var cond = GenExpr(parameters[0]);
                var condIf = GenExpr(parameters[1][0]);
                var condElse = GenExpr(parameters[1][1]);
                EvalType(cond);
                EvalType(condIf);
                EvalType(condElse);

                if (cond == null || cond.Type == null)
                    return null;
                if (condIf == null || condIf.Type == null)
                    return null;
                if (condElse == null || condElse.Type == null)
                    return null;
                if (!CheckBool(ex.Token, cond.Type, "if"))
                    return null;

                condIf.Type = DerefRef(condIf.Type);
                condElse.Type = DerefRef(condElse.Type);

                // Allow mixing of pointers and nil
                if (condIf.Type.Parent.FullName == RAW_POINTER_TYPE && condElse.Type.FullName == NIL_TYPE)
                    return new Rval(ex.Token) { Type = condIf.Type };
                if (condIf.Type.FullName == NIL_TYPE && condElse.Type.Parent.FullName == RAW_POINTER_TYPE)
                    return new Rval(ex.Token) { Type = condElse.Type };

                if (condIf.Type.FullName != condElse.Type.FullName)
                {
                    Reject(parameters[1].Token, $"Left and right sides must evaluate to same type, but they evaluate to '{condIf.Type}' and '{condElse.Type}'");
                    return null;
                }
                return new Rval(ex.Token) { Type = condIf.Type };
            }

            // Find and call a function (or getter) taking no arguments
            Symbol FindAndCall(Token token, Symbol inType, string name)
            {
                // This marks the local with symbol info, which we don't want here
                var call = new Rval(token, name) { 
                    InType = inType, Candidates = FindInType(name, inType) };
                return FindCompatibleFunction(call, new List<Rval>(),
                    $"'{name}' in the type '{inType}'", false)?.Type;
            }

            Rval FindCompatibleConstructor(Rval call, List<Rval> args)
            {
                var newType = call.Candidates[0];
                call.Token.Type = eTokenType.TypeName;
                if (newType.IsSpecialized)
                    throw new Exception("Compiler error: FindCompatibleConstructor, unexpected specialized type");

                if (newType.IsTypeParam)
                {
                    if (call.TypeArgs.Length != 0)
                        Reject(call.Token, $"Expecting 0 type parameters, but got '{call.TypeArgs.Length}'");
                    if (args.Count != 0)
                        Reject(call.Token, $"New generic type with arguments not supported yet");
                    return new Rval(call.Token) { Type = table.GetGenericParam(newType.Order) };
                }

                // Empty constructor (create a default with any type parameters)
                // TBD: This overrides all users constructors.  Put this below
                //      `FindCompatibleFuncton`, refactor so that function
                //      doesn't mark the error.
                if (args == null || args.Count == 0)
                {
                    if (call.TypeArgs.Length == newType.GenericParamTotal())
                    {
                        // Add supplied type parameters
                        if (call.TypeArgs.Length != 0)
                            newType = table.CreateSpecializedType(newType, call.TypeArgs);
                    }
                    else
                    {
                        Reject(call.Token,
                            $"Expecting {newType.GenericParamTotal()} generic parameter(s), but got {call.TypeArgs.Length});");
                    }
                    call.Token.AddInfo(newType);
                    return new Rval(call.Token) { Type = newType };
                }

                // Search for `new` function
                Debug.Assert(call.InType == null);
                call.InType = table.WildCard;
                call.Name = "new";
                call.IsStatic = true; // Constructor is static
                call.Candidates = FindInType(call.Name, newType);
                call = FindCompatibleFunction(call, args,
                            $"'new' (constructor for '{call.InType}')");
                if (call == null || call.Type == null)
                    return null;

                return call;
            }


            // Given the call and its parameters, find the best matching function.
            // If there is an error, mark it and give feedback on possible matches.
            Rval FindCompatibleFunction(
                Rval call,
                List<Rval> args,
                string rejectName,
                bool addSymbolInfo = true)
            {
                if (call.Type != null)
                {
                    Reject(call.Token, "Function or type name expected");
                    return null;
                }

                // Find all candidate functions in scope
                var candidates = call.Candidates;
                if (candidates == null || candidates.Count == 0)
                {
                    if (call.InType == null)
                        Reject(call.Token, $"'{call.Name} is an undefined symbol");
                    else
                        Reject(call.Token, $"'{call.Name}' is an undefined symbol in the type '{call.InType}'");
                    return null;
                }

                // Unresolved arguments
                if (args == null)
                {
                    // Give some feedback on the functions that could be called
                    if (addSymbolInfo)
                        foreach (var sym in candidates)
                            call.Token.AddInfo(sym);
                    addSymbolInfo = false;

                    // If there is just 1 symbol without generic arguments,
                    // assume that is what was called. This gives better type
                    // inference than making it unresolved.
                    if (candidates.Count != 1 || candidates[0].HasGenericArg)
                        return null;
                    args = new List<Rval>();
                }

                // Exactly 1 primary or multiple functions
                if (RejectAmbiguousPrimary(call.Token, candidates))
                    return null;

                // Insert inType type as receiver parameter
                call.Type = call.InType == null ? table.WildCard : call.InType;
                if (call.InType == null)
                    call.IsStatic = true;
                args.Insert(0, call);

                // Generate list of matching symbols, update
                // the candidates to their specialized form
                var compatibleErrors = CallCompatible.Compatible;
                var matchingFuns = new List<Symbol>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    var (newFun, isCompatible) = IsCallCompatible(candidates[i], call, args);
                    compatibleErrors |= isCompatible;
                    if (newFun != null)
                    {
                        matchingFuns.Add(newFun);
                        candidates[i] = newFun;
                    }
                }

                if (matchingFuns.Count == 0)
                {
                    // Incorrect type
                    if (addSymbolInfo)
                        RejectSymbols(call.Token, candidates, 
                            $"No function {rejectName} taking '{Rval.ParamTypes(args, call.TypeArgs)}'" 
                            + $" in scope: {Print(compatibleErrors)}");
                    addSymbolInfo = false;

                    // If there was just 1 symbol, assume that is what was called.
                    // This gives better type inference than making it unresolved.
                    if (candidates.Count != 1)
                        return null;
                    matchingFuns = new List<Symbol>() { candidates[0] };
                }

                if (matchingFuns.Count != 1)
                {
                    if (addSymbolInfo)
                        RejectSymbols(call.Token, matchingFuns, 
                            $"Multiple functions {rejectName} taking '{Rval.ParamTypes(args, call.TypeArgs)}' in scope");
                    return null;
                }

                var func = matchingFuns[0];
                if (!func.IsFun)
                {
                    Reject(call.Token, $"Expecting a function, but a {matchingFuns[0].Kind} was supplied");
                    return null;
                }

                if (addSymbolInfo)
                    call.Token.AddInfo(func);

                return new Rval(call.Token) { Type = func.FunReturnTupleOrType };
            }

            // Checks if the function call is compatible, return the possibly
            // specialized function.  Returns null if not compatible.
            (Symbol, CallCompatible) IsCallCompatible(Symbol func, Rval call, List<Rval> args)
            {
                if (!func.IsFun)
                    return (null, CallCompatible.NotAFunction);

                if (call.IsStatic && !func.IsStatic)
                    return (null, CallCompatible.StaticCallToNonStaticMethod);
                if (!call.IsStatic && func.IsStatic)
                    return (null, CallCompatible.NonStaticCallToStaticMethod);


                // Find type args supplied by the source code (explicitly or inferred)
                var typeArgs = InferTypeArgs(func, call.TypeArgs, args, func.FunParamTypes);

                // Use type args from function (or constraint) if supplied
                if (func.TypeArgs.Length != 0)
                {
                    if (typeArgs.Length != 0)
                        return (null, CallCompatible.TypeArgsSuppliedByConstraint);
                    typeArgs = func.TypeArgs;
                }

                // Verify number of type arguments
                var typeArgsExpectedCount = func.GenericParamTotal();
                if (typeArgs.Length != typeArgsExpectedCount)
                {
                    if (typeArgs.Length == 0 && typeArgsExpectedCount != 0)
                        return (null, CallCompatible.ExpectingSomeTypeArgs);
                    if (typeArgs.Length != 0 && typeArgsExpectedCount == 0)
                        return (null, CallCompatible.ExpectingNoTypeArgs);
                    return (null, CallCompatible.WrongNumberOfTypeArgs);
                }

                // Convert generic type args to specialized
                if (!func.IsSpecialized && typeArgs.Length != 0)
                    func = table.CreateSpecializedType(
                        func, typeArgs, null, ReplaceGenericTypeParams(func.Type, typeArgs));


                // Don't match receiver of static functions.
                // Also don't match receier of a generic constraint
                // since it's matched by the constaint.
                var funParams = func.FunParamTypes;
                var startIndex = 0;
                if (func.Qualifiers.HasFlag(SymQualifiers.Static))
                    startIndex = 1;
                if (args[0].Type.IsGenericArg && func.Concrete.Parent.IsInterface)
                    startIndex = 1;

                // Match up the arguments (TBD: default parameters)
                if (args.Count != funParams.Length)
                    return (null, CallCompatible.WrongNumberOfParameters);
                for (var i = startIndex; i < funParams.Length; i++)
                {
                    if (funParams[i] == null)
                        return (null, CallCompatible.IncompatibleParameterTypes);

                    var arg = args[i].Type;
                    var param = funParams[i];

                    // Types match whether they are references or not
                    arg = DerefRef(arg);
                    param = DerefRef(param);

                    // Implicit conversion from nil to *Type or from *Type to *void
                    if (param.Parent.FullName == RAW_POINTER_TYPE)
                    {
                        if (arg.FullName == NIL_TYPE)
                            continue;
                        if (arg.Parent.FullName == RAW_POINTER_TYPE && DerefPointers(param) == typeVoid)
                            continue;
                    }

                    if (arg.FullName != param.FullName)
                        return (null, CallCompatible.IncompatibleParameterTypes);
                }
                return (func, CallCompatible.Compatible);
            }



            // Infer the type arguments if not given.
            Symbol[] InferTypeArgs(Symbol func, Symbol []typeArgs, List<Rval> args, Symbol []funParamTypes)
            {
                if (typeArgs.Length != 0)
                    return typeArgs;        // Supplied explicitly by call
                if (func.TypeArgs.Length != 0)
                    return typeArgs;        // Supplied by the function

                var typeArgsNeeded = func.GenericParamTotal();
                if (typeArgsNeeded == 0 || typeArgsNeeded > funParamTypes.Length)
                    return typeArgs;  // Must have enough parameters to make it work

                // Walk through parameters looking for matches
                var inferredTypeArgs = new Symbol[typeArgsNeeded];
                int numArgs = Math.Min(args.Count, funParamTypes.Length);
                for (int paramIndex = 0;  paramIndex < numArgs;  paramIndex++)
                    if (!InferTypeArg(args[paramIndex].Type, funParamTypes[paramIndex], inferredTypeArgs))
                        return typeArgs; // Fail

                // Check if we got them all
                foreach (var type in inferredTypeArgs)
                    if (type == null)
                        return typeArgs;

                return inferredTypeArgs;
            }

            // Infer one type arg, return false if there is an error and it should bail
            bool InferTypeArg(Symbol argType, Symbol funParamType, Symbol[] inferredTypeArgs)
            {
                argType = DerefRef(argType);

                // If it's a generic arg, use the given parameter type
                if (funParamType.IsGenericArg)
                {
                    var order = funParamType.Order;
                    if (order >= inferredTypeArgs.Length)
                        throw new Exception("Compiler error: InferTypeArg, index out of range");

                    if (inferredTypeArgs[order] != null)
                    {
                        // If types do not match, it's a contradiction, e.g. user calls f(0, "x") on f<T>(x T, y T).
                        // TBD: Give better error message (this just fails with 'wrong number of type args')
                        if (!TypesMatch(inferredTypeArgs[order], argType))
                            return false;  // User error
                    }
                    inferredTypeArgs[order] = argType;
                    return true;
                }
                // If they are both the same generic type, check type parameters
                if (funParamType.IsSpecialized
                    && argType.IsSpecialized
                    && funParamType.Parent.FullName == argType.Parent.FullName)
                {
                    Debug.Assert(funParamType.TypeArgs.Length == argType.TypeArgs.Length);
                    for (int i = 0;  i < funParamType.TypeArgs.Length;  i++)
                    {
                        if (!InferTypeArg(argType.TypeArgs[i], funParamType.TypeArgs[i], inferredTypeArgs))
                            return false;
                    }
                }

                return true;
            }

            // Replace the generic type argument with the given argument,
            // return the result, but don't change the original.
            Symbol ReplaceGenericTypeParams(Symbol type, Symbol[] args)
            {
                if (args.Length == 0)
                    return type;

                if (type.IsGenericArg)
                {
                    if (type.Order >= 0 && type.Order < args.Length)
                        return args[type.Order];
                    throw new Exception("Compiler error: ReplaceGenericTypeParams, index out of range");
                }

                if (type.IsSpecialized)
                    return table.CreateSpecializedType(type.Parent,
                        NewGenericTypeParams(type.TypeArgs, args), type.TupleNames);

                return type;
            }

            // Replace the generic type argument with the given argument,
            // return the result, but don't change the original.
            Symbol[] NewGenericTypeParams(Symbol[] types, Symbol[] args)
            {
                if (types == null || types.Length == 0)
                    return types;
                var newTypes = new Symbol[types.Length];
                for (int i = 0; i < types.Length; i++)
                    newTypes[i] = ReplaceGenericTypeParams(types[i], args);
                return newTypes;
            }

            bool TypesMatch(Symbol a, Symbol b)
                => a.FullName == b.FullName;

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
                    EvalType(rval);
                    if (rval != null && rval.Type != null)
                        funArgs.Add(rval);
                    else
                        paramHasError = true;
                }
                return paramHasError ? null : funArgs;
            }

            // Top level statement evaluation
            void EvalTypeStatement(Rval rval)
            {
                var symbol = EvalType(rval, true);
                if (rval == null || symbol == null)
                    return;
                
                if (symbol.IsFun)
                {
                    // TBD: Mark an error for non-mut function calls
                    if (symbol.IsGetter || symbol.IsSetter)
                        Reject(rval.Token, "Top level statement cannot be a getter or setter");
                }
            }

            // Set return type of symbol, or null if symbol is not found,
            // is unresolved, or ambiguous.
            // `assignmentTarget` allows the symbol to be an unresolved local
            // and is also used to resolve ambiguity between getter/setter.
            // Mark an error when there is no match.
            // Returns the symbol that generated the type (or null if
            // there wasn't one)
            Symbol EvalType(Rval rval, 
                bool assignmentTarget = false, 
                bool allowStaticType = false)
            {
                if (rval == null)
                    return null;

                // Done if we already have return type
                if (rval.Type != null)
                    return null;

                var candidates = rval.Candidates;
                if (candidates == null || candidates.Count == 0)
                {
                    if (rval.InType == null)
                        Reject(rval.Token, $"'{rval.Name} is an undefined symbol");
                    else
                        Reject(rval.Token, $"'{rval.Name}' is an undefined symbol in the type '{rval.InType}'");
                    return null;
                }

                if (RejectAmbiguousPrimary(rval.Token, candidates))
                    return null;

                // Filter out functions, except getters and setters
                var oldCalls = candidates.ToArray();
                candidates.RemoveAll(callFunc => callFunc.IsFun && !(callFunc.IsGetter || callFunc.IsSetter));
                var token = rval.Token;

                // If there is a tie between a getter and setter, choose based `assignmentTarget`
                if (candidates.Count == 2 && (candidates[0].IsSetter == candidates[1].IsGetter))
                {
                    candidates.RemoveAll(callFunc => assignmentTarget ? callFunc.IsGetter : callFunc.IsSetter);
                    if (candidates.Count == 1 && candidates[0].IsSetter)
                        rval.IsSetter = true;  
                }

                if (candidates.Count == 0)
                {
                    RejectSymbols(token, oldCalls,
                        $"'{token}' can't find variable, field, or getter function");
                    return null;
                }

                // Don't allow multiple symbols
                if (candidates.Count != 1)
                {
                    RejectSymbols(token, candidates,  $"'{token}' is ambiguous");
                    return null;
                }

                var sym = candidates[0];

                // Generic parameter subsitution
                if ( (sym.IsFun || sym.IsField)
                    && sym.Type != null && rval.InType != null && rval.InType.TypeArgs.Length != 0)
                {
                    sym = table.CreateSpecializedType(sym, rval.InType.TypeArgs, null,
                        ReplaceGenericTypeParams(sym.Type, rval.InType.TypeArgs));
                }

                token.AddInfo(sym);

                if (sym.IsAnyTypeOrModule)
                {
                    token.Type = eTokenType.TypeName;
                    if (allowStaticType || sym.FullName == NIL_TYPE)
                    {
                        // Substitute generic parameter
                        if (sym.IsTypeParam)
                            rval.Type = table.GetGenericParam(sym.Order);
                        else
                            rval.Type = sym;

                        rval.IsStatic = true;
                        return sym;
                    }
                    else
                    {
                        Reject(token, $"'{token}' is a {sym.KindName}, which is not valid when used like this");
                        return sym;
                    }
                }

                if (sym.IsLocal)
                {
                    if (sym.Type == null && !assignmentTarget)
                    {
                        Reject(token, $"'{token}' has an unresolved type");
                        Reject(sym.Token, $"'{token}' has an unresolved type");
                    }
                    rval.Type = sym.Type;
                    rval.IsLocal = true;
                    return sym;
                }
                if (sym.IsFunParam)
                {
                    if (sym.Type == null)
                        Reject(token, $"'{token}' has an unresolved type");
                    rval.Type = sym.Type;
                    rval.IsLocal = true;
                    return sym;
                }
                if (sym.IsField)
                {
                    if (sym.Type == null)
                    {
                        Reject(token, $"'{token}' has an unresolved type");
                        return sym;
                    }
                    Debug.Assert(sym.Type.Parent.FullName != REF_TYPE);
                    rval.Type = sym.Type;

                    // A field is the same thing as a getter returning a mutable ref
                    MakeIntoRef(rval);
                    return sym;
                }
                if (sym.IsFun)
                {
                    rval.Type = sym.FunReturnTupleOrType;
                    return sym;
                }
                Reject(token, $"'{token}' compiler failure: '{sym}' is {sym.KindName}");
                Debug.Assert(false);
                return sym;
            }

            /// <summary>
            /// Find symbols in the type, including functions defined in
            /// the type's module, this module, or use statements.
            /// Returns an empty symbols list if none found.
            /// </summary>
            List<Symbol> FindInType(string name, Symbol inType)
            {
                var symbols = new List<Symbol>();
                AddSymbolsNamedInType(name, inType, symbols);
                AddFunctionsNamedInModule(name, inType.Parent, inType, symbols);
                AddFunctionsNamedInModule(name, currentFunction.Parent, inType, symbols);

                // Search 'use' symbol
                if (fileUses.UseSymbols.TryGetValue(name, out var useSymbols))
                {
                    foreach (var sym2 in useSymbols)
                        if (sym2.IsMethod && sym2.IsFun)
                        {
                            var funParams = sym2.FunParamTypes;
                            if (funParams.Length != 0 && funParams[0].FullName == inType.FullName)
                                symbols.Add(sym2);
                        }
                }

                AddGenericConstraints(name, inType, symbols);
                RemoveLastDuplicates(symbols);
                return symbols;
            }

            // Collect constraints on generic argument inType.  Generic
            // arguments of the function are replaced with the constraint
            // generic arguments.
            void AddGenericConstraints(string name, Symbol inType, List<Symbol> symbols)
            {
                if (currentFunction.Constraints == null || !inType.IsGenericArg)
                    return;
                if (!currentFunction.Constraints.TryGetValue(inType.ToString(), out var constraints))
                    return;
                foreach (var constraintStr in constraints)
                {
                    var constraint = table.Lookup(constraintStr);
                    if (constraint == null || !constraint.IsInterface)
                    {
                        Debug.Assert(false);  // Compiler shouldn't allow this
                        continue;
                    }
                    var i = symbols.Count;
                    AddSymbolsNamedInType(name, constraint, symbols);

                    // TBD: Working on this
                    // Replace generic type args with the given type arguments
                    while (i < symbols.Count)
                    {
                        var s = symbols[i];
                        if (s.GenericParamTotal() == constraint.TypeArgs.Length)
                        {
                            symbols[i] = table.CreateSpecializedType(s, constraint.TypeArgs, null,
                                ReplaceGenericTypeParams(s.Type, constraint.TypeArgs));
                        }
                        i++;
                    }
                }
            }

            // Add all children with the given name (including specialized type)
            void AddSymbolsNamedInType(string name, Symbol inType, List<Symbol> symbols)
            {
                AddSymbolsNamedConcrete(name, inType, symbols);
                if (inType.IsSpecialized)
                    AddSymbolsNamedConcrete(name, inType.Parent, symbols);
            }

            // Add all children with the given name (primary or non-extension method)
            void AddSymbolsNamedConcrete(string name, Symbol inType, List<Symbol> symbols)
            {
                if (inType.TryGetPrimary(name, out Symbol sym))
                    symbols.Add(sym);
                if (inType.HasFunNamed(name))
                    foreach (var child in inType.Children)
                        if (child.IsFun && child.Token == name)
                            symbols.Add(child);
            }

            // Walk up `inModule` to find the module, then collect functions `inType`
            void AddFunctionsNamedInModule(string name, Symbol inModule, Symbol inType, List<Symbol> symbols)
            {
                while (inModule != null && !inModule.IsModule)
                    inModule = inModule.Parent;
                if (inModule == null || !inModule.HasFunNamed(name))
                    return;

                // Ignore mut, etc., then just compare the non-specialized type.
                if (inType.IsSpecialized)
                    inType = inType.Parent;

                foreach (var child in inModule.Children)
                {
                    if (!child.IsFun || !child.IsMethod || child.Token != name)
                        continue;
                    var parameters = child.FunParamTypes;
                    if (parameters.Length == 0)
                        continue;

                    // Compare the non-specialized type
                    //      e.g: List<#1> matches List<byte> so we get all functions
                    var paramType = parameters[0];
                    if (paramType.IsSpecialized)
                        paramType = paramType.Parent;
                   
                    if (paramType.FullName != inType.FullName)
                        continue;

                    symbols.Add(child);
                }
            }

            // Create a new variable in the local scope, return null if it already exists.
            // Don't allow shadowing.
            Symbol CreateLocal(Token name)
            {
                if (currentFunction.TryGetPrimary(name, out var primary))
                {
                    Reject(name, $"'{name}' is already defined as a local parameter.");
                    return null;
                }
                if (locals.TryGetValue(name, out var localSymbol))
                {
                    if (localSymbol.Symbol != null)
                    {
                        Reject(name, $"'{name}' is already defined as a local variable in this scope.");
                        return null;
                    }

                    if (localSymbol.ScopeNum > scopeNum)
                    {
                        Reject(name, $"'{name}' is already defined as a local variable in a previous scope.");
                        return null;
                    }
                }
                var local = new Symbol(SymKind.Local, null, name);
                locals[name] = new LocalSymbol(scopeNum, local);
                return local;
            }

            /// <summary>
            /// Find symbols in the local/global scope that match this
            /// token.  If it's a local or parameter in the current
            /// function, stop searching.  Otherwise find a list of matching
            /// symbols in the current module or use symbols.
            /// Returns NULL and rejects token on error.
            /// </summary>
            List<Symbol> FindGlobal(Token token, string name)
            {
                // Find local
                if (currentFunction.TryGetPrimary(name, out var localParam))
                    return new List<Symbol>() { localParam };
                if (locals.TryGetValue(name, out var local))
                {
                    if (local.Symbol != null)
                        return new List<Symbol>() { local.Symbol };
                    Reject(token, $"'{name}' is an out of scope local variable");
                    return null;
                }

                // Find global symbols in this module
                var module = currentFunction.Parent;
                var symbols = new List<Symbol>();
                if (module.TryGetPrimary(name, out Symbol sym1))
                    symbols.Add(sym1);
                if (module.HasFunNamed(name))
                    foreach (var child in module.Children)
                        if (child.IsFun && child.Token == name && !child.IsMethod)
                            symbols.Add(child);

                // Search 'use' symbol
                if (fileUses.UseSymbols.TryGetValue(name, out var useSymbols))
                {
                    foreach (var sym2 in useSymbols)
                        if (!sym2.IsMethod)
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

            void BeginLocalScope()
            {
                scopeNum++;
            }

            void EndLocalScope()
            {
                foreach (var local in locals.Values)
                {
                    if (local.ScopeNum == scopeNum)
                        local.Symbol = null;
                    else if (local.ScopeNum > scopeNum)
                        local.ScopeNum = scopeNum;
                }
                scopeNum--;
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
                var functions = 0;
                var primaries = 0;
                foreach (var symbol in symbols)
                    if (symbol.IsFun)
                        functions++;
                    else
                        primaries++;

                var reject = primaries >= 2 || primaries == 1 && functions >= 1;
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

            void RejectExpr(SyntaxExpr ex, string message) 
            {
                table.Reject(ex.Token, message);
                foreach (var e in ex)
                    RejectExpr(e, message);
            }


        }

        static string Print(CallCompatible c)
        {
            if (c == 0)
                return "Compatible";
            var errors = new List<string>();
            if (c.HasFlag(CallCompatible.NotAFunction))
                errors.Add("Not a function");
            if (c.HasFlag(CallCompatible.StaticCallToNonStaticMethod))
                errors.Add("Static call to non static method (receiver must be a value, not a type)");
            if (c.HasFlag(CallCompatible.NonStaticCallToStaticMethod))
                errors.Add("Non-static to static function call (receiver must be a type, not a value)");
            if (c.HasFlag(CallCompatible.ExpectingSomeTypeArgs))
                errors.Add("Expecting some type arguments, but none supplied");
            if (c.HasFlag(CallCompatible.ExpectingNoTypeArgs))
                errors.Add("Expecting no type arguments, but some were supplied");
            if (c.HasFlag(CallCompatible.WrongNumberOfTypeArgs))
                errors.Add("Wrong number of type parameters");
            if (c.HasFlag(CallCompatible.WrongNumberOfParameters))
                errors.Add("Wrong number of parameters");
            if (c.HasFlag(CallCompatible.IncompatibleParameterTypes))
                errors.Add("Incompatible parameter types");
            if (c.HasFlag(CallCompatible.TypeArgsSuppliedByConstraint))
                errors.Add("Non-generic function cannot take type arguments");
            return string.Join(",", errors.ToArray());
        }



    }
}
