using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using Zurfur.Lex;
using Zurfur.Jit;

namespace Zurfur.Compiler
{
    /// <summary>
    /// Compile the code given the output of header file generation.
    /// </summary>
    static class CompileCode
    {
        static WordSet sOperators = new WordSet("+ - * / % & | ~ == != >= <= > < << >> and or not in |= &= += -= <<= >>= .. ..+ ]");
        static WordSet sCmpOperators = new WordSet("== != >= <= > <");
        static WordSet sIntTypeNames = new WordSet("Zurfur.int Zurfur.u64 Zurfur.i32 Zurfur.u32");
        static WordSet sDerefRef = new WordSet("Zurfur.Unsafe.Ref`1");
        static WordSet sDerefPointers = new WordSet("Zurfur.Unsafe.RawPointer`1 Zurfur.Pointer`1");
        public static WordMap<string> OpNames = new WordMap<string> {
            {"+", "_opAdd"}, {"+=", "_opAdd"}, {"-", "_opSub"}, {"-=", "_opSub"},
            {"*", "_opMul"}, {"*=", "_opMul"}, {"/", "_opDiv"}, {"/=", "_opDiv"},
            {"%","_opRem" }, {"%=","_opRem" },
            {"..", "_opRange" }, {"..+", "_opRange" },
            {"==", "_opEq"}, {"!=", "_opEq" }, {"in", "_opIn" },
            {">", "_opCmp" }, {">=", "_opCmp" }, {"<", "_opCmp" }, {"<=", "_opCmp" },
            {"]", "_opIndex" },

            // Include in the list of operator names
            {"%%U1", "_opNeg" }, { "%%U2", "_opBitNot"},

            // TBD: Bit operators should be restricted to i32, u32, int, u64 types
            {"<<", "_opBitShl"}, {"<<=", "_opBitShl"}, {">>", "_opBitShr"}, {">>=", "_opBitShr"},
            {"&", "_opBitAnd"}, {"&=", "_opBitAnd"}, {"|", "_opBitOr"}, {"|=", "_opBitOr"},
            {"~", "_opBitXor"}, {"~=", "_opBitXor"},

            // TBD: Logical operators should be restricted to bool type
            {"and", "_opLogicalAnd"}, {"or", "_opLogicalOr"}, {"not", "_opLogicalNot"}
        };

        static List<Rval> EmptyCallParams
            => new List<Rval>();

        record struct CallMatch(Symbol? SpecializedFun, CallCompatible Compatibility);

        class LocalSymbol
        {
            public Symbol? Symbol;
            public int ScopeNum;
            public int LocalNum;

            public LocalSymbol(Symbol symbol, int scopeNum, int localNum)
            {
                ScopeNum = scopeNum;
                Symbol = symbol;
                LocalNum = localNum;
            }
        }

        class Rval
        {
            public Token Token;
            public string Name;
            public Symbol? Type;
            public Symbol[] TypeArgs = Array.Empty<Symbol>();
            public Symbol? InType;

            public bool IsUntypedConst; // NOTE: `3 int` is a typed const
            public bool IsLocal;
            public bool IsExplicitRef;
            public bool IsStatic;
            public bool AssignmentTarget;
            public bool DontAddCallInfo;
            public bool Invoked;            // Call has '()', so invoke the lambda
            public bool Dot;                // Allow static


            /// <summary>
            /// When `Type` is $lambda, store the expression for later compilation
            /// </summary>
            public SyntaxExpr? LambdaSyntax;

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
                var paramStr = $"{typeParams}({string.Join(",", args.Select(a => a.Type == null ? "(?)" : a.Type.FullName))})";
                return paramStr;
            }
        }

        static public Assembly GenerateCode(
            Dictionary<string, SyntaxFile> synFiles,
            SymbolTable table,
            Dictionary<SyntaxScope, Symbol> syntaxToSymbol,
            UseSymbols allFileUses)
        {
            var assembly = new Assembly();
            assembly.Types.AddOrFind(table.EmptyTuple);
            assembly.Types.AddOrFind(table.Lookup(SymTypes.Nil)!);
            assembly.Types.AddOrFind(table.Lookup(SymTypes.Bool)!);
            assembly.Types.AddOrFind(table.Lookup(SymTypes.Int)!);
            assembly.Types.AddOrFind(table.Lookup(SymTypes.Float)!);
            assembly.Types.AddOrFind(table.Lookup(SymTypes.Str)!);

            assembly.Types.AddOrFind(table.EmptyTuple);
            foreach (var synFile in synFiles)
            {
                var fileUses = allFileUses.Files[synFile.Key];
                foreach (var synFunc in synFile.Value.Functions)
                {
                    // Get current function
                    if (!syntaxToSymbol.TryGetValue(synFunc, out var currentFunction))
                        continue; // Syntax error
                    Debug.Assert(currentFunction.IsFun);
                    GenFunction(synFile.Value, synFunc, table, fileUses, currentFunction, assembly);
                }
            }
            return assembly;
        }

        static void GenFunction(
            SyntaxFile synFile,
            SyntaxFunc synFunc,
            SymbolTable table,
            UseSymbolsFile fileUses,
            Symbol function,
            Assembly assembly)
        {
            var typeVoid = table.Lookup(SymTypes.Void);
            var typeNil = table.Lookup(SymTypes.Nil);
            var typeInt = table.Lookup(SymTypes.Int);
            var typeU64 = table.Lookup(SymTypes.U64);
            var typeI32 = table.Lookup(SymTypes.I32);
            var typeU32 = table.Lookup(SymTypes.U32);
            var typeStr = table.Lookup(SymTypes.Str);
            var typeBool = table.Lookup(SymTypes.Bool);
            var typeByte = table.Lookup(SymTypes.Byte);
            var typeFloat = table.Lookup(SymTypes.Float);
            var typeF32 = table.Lookup(SymTypes.F32);
            
            Debug.Assert(typeVoid != null 
                && typeNil != null
                && typeInt != null
                && typeU64 != null
                && typeI32 != null
                && typeStr != null 
                && typeBool != null
                && typeByte != null
                && typeFloat != null
                && typeF32 != null);

            Debug.Assert(function.IsFun);

            // TBD: Remove null forgiving operator below when the compiler
            //      is improved:  https://github.com/dotnet/roslyn/issues/29892
            var commentLines = new Dictionary<int, bool>();
            var localsByName = new Dictionary<string, LocalSymbol>();
            var localsByIndex = new List<Symbol>();
            var scopeToken = new Dictionary<int, Token>();
            var scopeNum = 0;
            var newVarScope = -1;       // Put the new var in scope above (if and while)
            var newVarCodeIndex = -1;    // Put the use variable at this assembly index

            // Function prototype comment
            var y1 = synFunc.Keyword.Y;
            var y2 = Math.Min(Math.Max(y1, synFunc.Statements?.Token.Y??0), synFile.Lexer.LineCount-1);
            for (var y = y1;  y <= y2; y++)
            {
                commentLines[y] = true;
                assembly.AddOpComment(synFunc.Keyword, synFile.Lexer.GetLine(y));
            }

            // Generate code for this function
            var opStartIndex = assembly.Code.Count;
            assembly.AddOpBeginFun(function);
            BeginLocalScope(function.Token);

            // Add input parameters as locals
            foreach (var p in function.FunParamTuple.TupleSymbols)
                localsByIndex.Add(p);

            if (synFunc.Statements != null)
                GenStatements(synFunc.Statements);
            EndLocalScope();

            // Replace local symbol index with the type.  This is done because
            // the type wasn't known when it was created.
            for (var i = opStartIndex; i < assembly.Code.Count; i++)
            {
                var op = assembly.Code[i];
                if (op.Op == Op.Loc)
                {
                    var symType = localsByIndex[(int)op.Operand].Type;
                    if (symType == null)
                        symType = typeVoid;
                    assembly.Code[i] = new AsOp(Op.Loc, assembly.Types.AddOrFind(symType));
                }
            }

            return;


            void GenStatementsWithScope(SyntaxExpr ex, Token token)
            {
                BeginLocalScope(token);
                GenStatements(ex);
                EndLocalScope();
            }

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
                    GenStatementsWithScope(s, s.Token);
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
                else if (name == "ret" || name == "yield")
                    GenReturnStatement(s);
                else
                {
                    // Generate top level expression, e.g. f(x), etc.
                    var rval = GenExpr(s);
                    if (rval != null)
                        rval.AssignmentTarget = true;
                    var symbol = EvalCall(rval, EmptyCallParams);
                    if (rval != null && symbol != null && symbol.IsFun)
                    {
                        // TBD: Mark an error for non-mut function calls
                        if (symbol.IsGetter || symbol.IsSetter)
                            Reject(rval.Token, "Top level statement cannot be a getter or setter");
                    }
                }
            }

            void GenWhileStatement(SyntaxExpr ex)
            {
                // Condition
                newVarScope = scopeNum;     // Locals in condition go in outer scope
                newVarCodeIndex = assembly.Code.Count;
                BeginLocalScope(ex.Token);
                GenBoolCondition(ex, "while");
                assembly.AddOp(Op.Brnif, ex.Token, 1);
                newVarScope = -1;           // End special locals condition scope
                newVarCodeIndex = -1;

                // Statements
                GenStatementsWithScope(ex[1], ex.Token);
                assembly.AddOp(Op.Br, ex.Token, -1);

                EndLocalScope();
            }

            int GenDoStatement(SyntaxExpr s, int i)
            {
                BeginLocalScope(s.Token);
                GenStatements(s[i]);
                if (i+1 < s.Count && s[i+1].Token == "dowhile")
                {
                    i += 1;
                    var cond = GenExpr(s[i][0]);
                    EvalCall(cond, EmptyCallParams);
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
                // If condition
                newVarScope = scopeNum;         // Locals in condition go in outer scope
                newVarCodeIndex = assembly.Code.Count;
                BeginLocalScope(s[i].Token);    // If...elif...else scope
                BeginLocalScope(s[i].Token);    // If... scope
                GenBoolCondition(s[i], "if");
                assembly.AddOp(Op.Brnif, s[i].Token, 1);
                newVarScope = -1;               // End special locals condition scope
                newVarCodeIndex = -1;

                // If statements
                GenStatements(s[i][1]);
                assembly.AddOp(Op.Br, s[i].Token, 2);
                EndLocalScope();


                // ...elif...
                while (i+1 < s.Count && s[i+1].Token == "elif")
                {
                    i += 1;
                    BeginLocalScope(s[i].Token);
                    GenBoolCondition(s[i], "if");
                    assembly.AddOp(Op.Brnif, s[i].Token, 1);
                    GenStatements(s[i][1]);
                    assembly.AddOp(Op.Br, s[i].Token, 2);
                    EndLocalScope();
                }

                // ...else
                if (i + 1 < s.Count && s[i+1].Token == "else")
                {
                    i += 1;
                    GenStatementsWithScope(s[i][0], s[i].Token);
                }
                EndLocalScope();
                return i;
            }

            void GenBoolCondition(SyntaxExpr ex, string keyword)
            {
                var cond = GenExpr(ex[0]);
                EvalCall(cond, EmptyCallParams);
                CheckBool(ex[0].Token, cond?.Type, keyword);
            }

            bool CheckBool(Token token, Symbol? conditionType, string name)
            {
                if (conditionType == null)
                    return false;
                if (DerefRef(conditionType).FullName != SymTypes.Bool)
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

                BeginLocalScope(ex.Token);

                var local = CreateLocal(ex[0].Token);
                if (local != null)
                    local.Token.AddInfo(local);
                var rval = GenExpr(ex[1]);
                EvalCall(rval, EmptyCallParams);

                if (rval != null && rval.Type != null)
                {
                    var iterType = GenForIterator(ex[0].Token, rval.Type);
                    if (iterType != null && local != null)
                        local.Type = iterType;
                }

                var loopExpr = GenExpr(ex[2]);
                if (loopExpr != null)
                    EvalCall(loopExpr, EmptyCallParams);

                EndLocalScope();
            }

            // Get the for loop iterator type, e.g. T in object.iterator.next() -> Maybe<T>
            Symbol? GenForIterator(Token token, Symbol inType)
            {
                var getIter = GenFindAndCall(token, DerefRef(inType), "getIterator");
                if (getIter == null)
                    return null;
                var getNext = GenFindAndCall(token, getIter, "next");
                if (getNext == null)
                    return null;
                if (!getNext.IsSpecialized
                    || getNext.TypeArgs.Length != 1
                    || getNext.Parent!.FullName != SymTypes.Maybe)
                {
                    Reject(token, $"Expecting the function '{getIter}.next()' to return a Maybe<T>, but it returns '{getNext}'");
                    return null;
                }
                return getNext.TypeArgs[0];
            }

            // Find and call a function (or getter) taking no arguments
            Symbol? GenFindAndCall(Token token, Symbol inType, string name)
            {
                var call = new Rval(token, name) { InType = inType };
                call.DontAddCallInfo = true;
                return EvalCallExpectFun(call, new List<Rval>(), $"'{name}' in the type '{inType}'")
                    ?.FunReturnType;
            }

            Rval? GenReturnStatement(SyntaxExpr ex)
            {
                var returns = GenCallParams(ex, 0);
                if (returns == null)
                    return null;
                if (returns.Count >= 2)
                {
                    Reject(ex.Token, "Use parenthesis to return multiple values.");
                    return null;
                }

                var rval = returns.Count == 0 ? new Rval(ex.Token) { Type = table.EmptyTuple } : returns[0];
                EvalCall(rval, EmptyCallParams);
                if (rval == null || rval.Type == null)
                    return null;

                ex.Token.AddInfo(rval.Type);
                var funReturnType = function.FunReturnType;

                // TBD: This is to temporarily gloss over pointers, Maybe, and nil
                //      Implicit conversion from nil to *T or Maybe<T>
                if (rval.Type.FullName == SymTypes.Nil
                    && (funReturnType.Parent!.FullName == SymTypes.RawPointer)
                        || funReturnType.Parent!.FullName == SymTypes.Maybe)
                    return rval;

                IsParamConvertableReject(ex.Token, rval.Type, funReturnType);
                assembly.AddOp(Op.Ret, ex.Token, 0);

                return rval;
            }

            // Evaluate an expression.  When null is returned, the error is already marked.
            Rval? GenExpr(SyntaxExpr ex)
            {
                var token = ex.Token;
                var name = token.Name;
                if (name == "")
                {
                    Reject(token, "Compiler error: GenExpr, not compiled");
                    return null;  // Syntax error should already be marked
                }

                // Add source code comments
                if (!commentLines!.ContainsKey(token.Y))
                {
                    commentLines[token.Y] = true;
                    assembly.AddOpComment(token, synFile.Lexer.GetLine(token.Y));
                }

                // Terminals: Number, string, identifier
                if (char.IsDigit(name[0]))
                    return GenConstNumber(ex);
                else if (name == "\"" || name == "\"\"\"")
                    return GenStr(ex);
                else if (name == "nil")
                    return new Rval(token) { Type = typeNil };
                else if (name == "my")
                    return GenIdentifier(ex);
                else if (name == "require")
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
                else if (name == "??")
                    return GenTernary(ex);
                else if (name == "&*")
                    return GenRefOrAddressOf(ex);
                else if (name == "sizeof")
                    return new Rval(token) { Type = typeInt };
                else if (name == "true" || name == "false")
                    return new Rval(token) { Type = typeBool };
                else if (name == "astart")
                    return GenAstart(ex);
                else if (name == "?")
                    return GenDefaultOperator(ex);
                else if (name == "!" || name == "!!")
                    return GenErrorOperator(ex);
                else if (name == ";" || name == "{")
                    GenStatements(ex);
                else if ((char.IsLetter(name[0]) || name[0] == '_') && !ParseZurf.ReservedWords.Contains(name))
                    return GenIdentifier(ex);
                else if (name == "=>")
                    return GenLambda(ex);
                else if (name == "todo")
                { } // TBD: Call require(false)
                else if (name == "nop")
                { } // TBD: Send to assembly for break point
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

            Rval? GenConstNumber(SyntaxExpr ex)
            {
                // Get type (int, Float, or custom)
                var numberType = ex.Token.Name.Contains(".") ? typeFloat : typeInt;

                // For now, only int and Float are supported
                // TBD: Parse decimal and check for errors
                if (ex.Token.Name.Contains("."))
                {
                    // Store double as IEEE 754 long
                    double.TryParse(ex.Token, out var number);
                    assembly.AddOp(Op.Float, ex.Token, BitConverter.DoubleToInt64Bits(number));
                }
                else
                {
                    long.TryParse(ex.Token, out var number);
                    assembly.AddOp(Op.I64, ex.Token, number);
                }

                var untypedConst = true;
                if (ex.Count == 1)
                {
                    // TBD: Allow user defined custom types
                    // TBD: Call conversion to type
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
                    else if (customType == "float")
                        numberType = typeFloat;
                    else if (customType == "f32")
                        numberType = typeF32;
                    else if (customType == "byte")
                        numberType = typeByte;
                    else
                        Reject(ex[0].Token, $"'{ex[0].Token}' undefined number type");
                }
                return new Rval(ex.Token) { Type = numberType, IsUntypedConst = untypedConst };
            }

            Rval GenStr(SyntaxExpr ex)
            {
                assembly.AddOpStr(ex.Token, ex[0].Token);
                foreach (var interp in ex[0])
                    assembly.AddOpNoImp(ex.Token, "Interpolate: " + interp.ToString());
                return new Rval(ex.Token) { Type = typeStr };
            }


            Rval GenIdentifier(SyntaxExpr ex)
            {
                return new Rval(ex.Token);
            }

            // Type or function call (e.g. List<int>(), f<int>(), etc)
            Rval? GenTypeArgs(SyntaxExpr ex)
            {
                if (HasError(ex))
                    return null;
                var symbols = GenExpr(ex[0]);
                var typeParams = Resolver.ResolveTypeArgs(ex, table, function, fileUses);
                if (symbols == null || typeParams == null)
                    return null;
                symbols.TypeArgs = typeParams.ToArray();
                return symbols;
            }

            Rval? GenParen(SyntaxExpr ex)
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
                var tuple = table.CreateTuple(types!);
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


            Rval? GenDot(SyntaxExpr ex)
            {
                if (ex.Count != 2)
                    return null;  // Syntax error

                var leftDot = GenExpr(ex[0]);
                if (leftDot == null)
                    return null; // Error already marked
                leftDot.Dot = true;
                EvalCall(leftDot, EmptyCallParams);
                var leftType = leftDot.Type;
                if (leftType == null)
                    return null;

                // Automatically dereference pointers, etc
                var identifier = ex[1].Token;
                leftType = DerefRef(leftType);
                leftType = DerefPointers(leftType);

                return new Rval(identifier) { 
                    InType = leftType, 
                    IsStatic = leftDot.IsStatic
                };
            }

            Symbol DerefRef(Symbol type) => Deref(type, sDerefRef);
            Symbol DerefPointers(Symbol type) => Deref(type, sDerefPointers);

            // Dereference the given type names
            Symbol Deref(Symbol type, WordSet typeNames)
            {
                // Auto-dereference pointers and references
                if (type.IsSpecialized
                    && type.TypeArgs.Length != 0
                    && typeNames.Contains(type.Parent!.FullName))
                {
                    // Move up to non-generic concrete type
                    // TBD: Preserve concrete type parameters
                    type = type.TypeArgs[0];
                }
                return type;
            }

            Rval? GenDotStar(SyntaxExpr ex)
            {
                if (ex.Count != 1)
                    return null; // Syntax error
                var left = GenExpr(ex[0]);
                EvalCall(left, EmptyCallParams);
                if (left == null || left.Type == null)
                    return null;

                left.Type = DerefRef(left.Type);
                if (left.Type.Parent!.FullName != SymTypes.RawPointer)
                {
                    Reject(ex.Token, $"Only pointers may be dereferenced, but the type is '${left.Type}'");
                    return null;
                }
                left.Type = DerefPointers(left.Type);
                MakeIntoRef(left);
                ex.Token.AddInfo(left.Type);
                return new Rval(ex.Token) { Type = left.Type };
            }

            Rval? GenNewVarsOperator(SyntaxExpr ex)
            {
                ex.Token.Type = eTokenType.NewVarSymbol;
                if (ex.Count == 0)
                    return null;  // Syntax error
                if (ex.Count == 1)
                    return GenNewVarsUnary(ex[0], ex.Token);
                return GenNewVarsBinary(ex);
            }

            Rval? GenNewVarsBinary(SyntaxExpr ex)
            {
                // Binary operator, capture variabe
                var value = GenExpr(ex[0]);
                var variables = GenNewVarsUnary(ex[1], ex.Token);

                if (value == null || variables == null)
                    return null;

                EvalCall(value, EmptyCallParams);
                var valueType = value.Type;
                if (valueType == null)
                    return null;

                // Assign type to local variable
                var local = FindLocal(variables.Token).sym;
                Debug.Assert(local != null && local.IsLocal);
                local.Token.AddInfo(local);
                if (local.Type != null)
                {
                    Reject(variables.Token, "New variables in binary '@' operator must not have types");
                    return null;
                }
                // Implicitly convert Maybe<T> to bool and T
                if (valueType.Parent!.FullName == SymTypes.Maybe && valueType.TypeArgs.Length == 1)
                {
                    local.Type = valueType.TypeArgs[0];
                    return new Rval(value.Token) { Type = typeBool };
                }
                local.Type = valueType;
                return value;
            }

            // Still doesn't resolve multiple symbols '@(v1, v2)', etc.
            Rval? GenNewVarsUnary(SyntaxExpr ex, Token rejectToken)
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
                        local.Type = Resolver.Resolve(e[0], table, false, function, fileUses);
                }

                if (newSymbols.Count == 0)
                    return null; // Syntax error

                if (newSymbols.Count != 1)
                {
                    Reject(rejectToken, $"'{rejectToken}' multiple symbols not supported yet");
                    return null;
                }
                return new Rval(newSymbols[0].Token);
            }

            // TBD: Refactor to consolidate with or use GenNewVarsUnary
            List<Symbol> NewVarsForLambda(SyntaxExpr ex)
            {
                var newSymbols = new List<Symbol>();
                if (ex.Count == 0)
                    return newSymbols;
                foreach (var e in ex[0])
                {
                    if (e.Count == 0 || e.Token == "")
                        continue; // Syntax error

                    var local = CreateLocal(e.Token, true);
                    if (local == null)
                        continue;

                    newSymbols.Add(local);

                    // Resolve type (if given)
                    if (e.Count >= 1 && e[0].Token != "")
                        local.Type = Resolver.Resolve(e[0], table, false, function, fileUses);
                }
                return newSymbols;
            }

            Rval? GenRefOrAddressOf(SyntaxExpr ex)
            {
                if (ex.Count != 1)
                    return null; // Syntax error
                Debug.Assert(ex.Token == "&" || ex.Token == "&*");

                var rval = GenExpr(ex[0]);
                if (rval == null)
                    return null;

                EvalCall(rval, EmptyCallParams);
                if (rval.Type == null)
                    return null;

                if (rval.Type.Parent!.FullName != SymTypes.Ref && !rval.IsLocal)
                    Reject(ex.Token, $"The type '{rval.Type} is a value and cannot be converted to a reference'");

                if (ex.Token == "&")
                {
                    // TBD: The thing should already be a reference, or fail same as addrss off
                    rval.IsExplicitRef = true;
                    if (rval.Type.Parent.FullName != SymTypes.Ref)
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

                if (type.IsSpecialized && type.Parent!.FullName == SymTypes.Ref)
                {
                    Reject(rval.Token, "Cannot take address of a reference");
                    return;
                }

                rval.Type = table.CreateRef(type, rawPointer);
            }


            Rval? GenAssign(SyntaxExpr ex)
            {
                if (ex.Count != 2)
                    return null;  // Syntax error

                var left = GenExpr(ex[0]);
                var right = GenExpr(ex[1]);
                EvalCall(right, EmptyCallParams);
                if (left != null)
                    left.AssignmentTarget = true;
                var assignedSymbol = EvalCall(left, right == null ? EmptyCallParams : new List<Rval> { right });
                var rightType = right?.Type;
                if (left == null || right == null || rightType == null)
                    return null;

                // Assign type to local variable (if type not already assigned)
                if (assignedSymbol != null && assignedSymbol.Type == null
                    && (assignedSymbol.IsLocal || assignedSymbol.IsFunParam))
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

                // Implicit conversion from nil to *T
                if (leftType.Parent != null 
                    && DerefRef(leftType).Parent!.FullName == SymTypes.RawPointer 
                    && rightType.FullName == SymTypes.Nil)
                {
                    return null; // Pointer = nil
                }

                // A setter is a function call
                if (assignedSymbol != null && assignedSymbol.IsSetter)
                {
                    if (leftType.FullName != table.EmptyTuple.FullName)
                    {
                        // TBD: Enforce by verifier
                        Reject(ex.Token, "Setter must not return a value");
                        return null;
                    }
                    if (assignedSymbol == null || !assignedSymbol.IsFun)
                        throw new Exception("Compiler error: GenAssign, setter index out of range or not method");

                    var args = assignedSymbol.FunParamTypes;
                    if (args.Length != 2)
                    {
                        Reject(ex.Token, "Expecting two parameters (function type, and value type), static setters not supported yet");
                        return null;
                    }
                    
                    if (!IsParamConvertableReject(ex.Token, rightType, args[1]))
                        return null;

                    // Debug, TBD: Remove or get better compiler feedback system
                    ex.Token.AddInfo($"setter({args[1].FullName}) = ({rightType.FullName})");

                    assembly.AddOpNoImp(ex.Token, $"setter assignment");

                    // TBD: Need to make this into a function call
                    return null;
                }

                // TBD: A local or parameter will produce a reference
                var isLocal = assignedSymbol != null
                    && (assignedSymbol.IsLocal || assignedSymbol.IsFunParam);
                if (!isLocal && leftType.Parent!.FullName != SymTypes.Ref)
                {
                    Reject(ex.Token, "A value cannot be assigned.  Left side must be a local variable or a 'mut' reference.  "
                        + $" ({leftType.FullName}) = ({rightType.FullName})");
                    return null;
                }

                if (!IsParamConvertableReject(ex.Token, rightType, leftType))
                    return null;

                // Debug, TBD: Remove or get better compiler feedback system
                ex.Token.AddInfo($"({leftType.FullName}) = ({rightType.FullName})");

                assembly.AddOp(Op.Setr, ex.Token, 0);

                return null;
            }

            Rval? GenAstart(SyntaxExpr ex)
            {
                //Reject(ex.Token, "Not implemented yet");
                GenCallParams(ex, 0);
                return null;
            }

            Rval? GenDefaultOperator(SyntaxExpr ex)
            {
                Reject(ex.Token, "Not compiled yet");
                return null;
            }

            Rval? GenErrorOperator(SyntaxExpr ex)
            {
                if (HasError(ex))
                    return null;
                var op = GenExpr(ex[0]);
                EvalCall(op, EmptyCallParams);
                if (op == null || op.Type == null)
                    return null;
                if (op.Type.Parent!.FullName != SymTypes.Result && op.Type.Parent.FullName != SymTypes.Maybe
                        || op.Type.TypeArgs.Length != 1)
                {
                    Reject(ex.Token, $"Expecting 'Result<T>' or 'Maybe<T>', but got '{op.Type}'");
                    return null;
                }
                return new Rval(ex.Token) { Type = op.Type.TypeArgs[0] };
            }


            Rval? GenOperator(SyntaxExpr ex)
            {
                var token = ex.Token;
                if (ex.Count == 1 && token == "&")
                    return GenRefOrAddressOf(ex);

                var args = GenCallParams(ex, 0);
                if (args == null || args.Count == 0)
                    return null;  // Unresolved type or syntax error

                // Implicit conversion of untyped constant to integer types
                // TBD: Calculate constant during compilation and do range checks.
                //      Also, this probably belongs in FindCompatibleFunction so it applies to functions
                if (args.Count == 2 && token != "<<" && token != ">>" && token != "]")
                {
                    // Most operators want both sides to be of same type
                    var left = args[0];
                    var right = args[1];
                    if (right.IsUntypedConst && !left.IsUntypedConst 
                            && sIntTypeNames.Contains(DerefRef(left.Type!).FullName) && sIntTypeNames.Contains(right.Type!.FullName))
                        right.Type = left.Type;
                    if (!right.IsUntypedConst && left.IsUntypedConst 
                            && sIntTypeNames.Contains(DerefRef(right.Type!).FullName) && sIntTypeNames.Contains(left.Type!.FullName))
                        left.Type = right.Type;
                }

                var operatorName = OpNames[token];
                if (args.Count == 1)
                {
                    if (token == "-")
                        operatorName = "_opNeg";
                    else if (token == "~")
                        operatorName = "_opBitNot";
                }

                // Find static operators from either argument
                var call = new Rval(token, operatorName);
                if (call != null)
                    call.Invoked = true;
                var funType = EvalCallExpectFun(call, args, $" '{operatorName}' (operator '{token}')")
                    ?.FunReturnType;

                if (funType == null)
                    return null;

                if (sCmpOperators.Contains(token))
                {
                    var returnType = token == "==" || token == "!=" ? typeBool : typeInt;
                    if (funType != returnType)
                    {
                        Reject(token, $"Expecting operator to return '{returnType}'");
                        return null;
                    }
                    if (token == "<")
                        assembly.AddOp(Op.Lt, token, 0);
                    else if (token == "<=")
                        assembly.AddOp(Op.Le, token, 0);
                    else if (token == ">")
                        assembly.AddOp(Op.Gt, token, 0);
                    else if (token == ">=")
                        assembly.AddOp(Op.Ge, token, 0);

                    funType = typeBool;
                }

                return new Rval(call.Token) { Type = funType };
            }

            Rval? GenCall(SyntaxExpr ex)
            {
                if (ex.Count == 0)
                    return null; // Syntax error

                // Generate function call and then parameters
                var call = GenExpr(ex[0]);
                var args = GenCallParams(ex, 1);
                if (call == null)
                    return null;  // Undefined symbol or error evaluating left side

                call.Invoked = true;
                var funType = EvalCallExpectFun(call, args, $"'{call.Token}'")
                    ?.FunReturnType;

                if (funType == null)
                    return null;

                return new Rval(call.Token) { Type = funType };
            }


            // Parameters are evaluated for the types. 
            // If any parameter can't be evaluated, NULL is returned.
            // A non-null value means that all Rval's have a valid Type.
            List<Rval>? GenCallParams(SyntaxExpr ex, int startParam)
            {
                var funArgs = new List<Rval>();
                var paramHasError = false;
                for (int i = startParam; i < ex.Count; i++)
                {
                    var rval = GenExpr(ex[i]);
                    EvalCall(rval, EmptyCallParams);
                    if (rval != null && rval.Type != null)
                        funArgs.Add(rval);
                    else
                        paramHasError = true;
                }
                return paramHasError ? null : funArgs;
            }

            Rval? GenTernary(SyntaxExpr ex)
            {
                if (HasError(ex))
                    return null;

                var cond = GenExpr(ex[0]);
                var condIf = GenExpr(ex[1]);
                var condElse = GenExpr(ex[2]);
                EvalCall(cond, EmptyCallParams);
                EvalCall(condIf, EmptyCallParams);
                EvalCall(condElse, EmptyCallParams);

                if (cond == null || cond.Type == null)
                    return null;
                if (condIf == null || condIf.Type == null)
                    return null;
                if (condElse == null || condElse.Type == null)
                    return null;
                if (!CheckBool(ex.Token, cond.Type, "??"))
                    return null;

                condIf.Type = DerefRef(condIf.Type);
                condElse.Type = DerefRef(condElse.Type);

                // Allow mixing of pointers and nil
                if (condIf.Type.Parent!.FullName == SymTypes.RawPointer && condElse.Type.FullName == SymTypes.Nil)
                    return new Rval(ex.Token) { Type = condIf.Type };
                if (condIf.Type.FullName == SymTypes.Nil && condElse.Type.Parent!.FullName == SymTypes.RawPointer)
                    return new Rval(ex.Token) { Type = condElse.Type };

                if (condIf.Type.FullName != condElse.Type.FullName)
                {
                    Reject(ex.Token, $"'if' and 'else' parts must evaluate to same type, but they evaluate to '{condIf.Type}' and '{condElse.Type}'");
                    return null;
                }
                return new Rval(ex.Token) { Type = condIf.Type };
            }

            // Returns the concretete lambda type which will match all lambda
            // types during function resolution.  The lambda expression is
            // compiled after its type can be inferred.
            // TBD: If type parameters are supplied, use a specialized lambda,
            //      which could allow eliminating ambiguous lambda matches
            //      during function resolution.
            Rval GenLambda(SyntaxExpr ex)
            {
                return new Rval(ex.Token) { Type = table.LambdaType, LambdaSyntax = ex };
            }

            // Called after function resolution, when the lambda
            // parameter types have been determined.
            void PostGenLambda(SyntaxExpr ex, Symbol []lambdaParams)
            {
                if (ex.Count != 2)
                    return;  // Syntax error

                BeginLocalScope(ex.Token);
                var lambdaLocals = NewVarsForLambda(ex[0]);

                // TBD: It would be better to try to compile
                //      so we can give the user some feadback.
                //      Or, just gray out the whole lambda expression.
                if (lambdaLocals.Count != lambdaParams.Length)
                {
                    EndLocalScope();
                    Reject(ex.Token, "Incorrect number of parameters");
                    return;
                }
                // Assign local variable types
                for (int i = 0;  i < lambdaParams.Length;  i++)
                {
                    if (lambdaLocals[i].Type != null)
                        Reject(lambdaLocals[i].Token, "TBD: Lambda's can't have explicit types yet");
                    lambdaLocals[i].Type = lambdaParams[i];
                    lambdaLocals[i].Token.AddInfo(lambdaLocals[i]);
                }
                GenCallParams(ex, 1);
                EndLocalScope();
            }

            Symbol? EvalCallExpectFun(
                Rval? call,
                List<Rval>? args,
                string rejectName = "")
            {
                if (call == null)
                    return null;

                var s = EvalCall(call, args, rejectName);
                if (s == null || s.IsFun || s.IsLambda)
                    return s;

                Reject(call.Token, "Expecting function or lambda return type");
                return null;
            }

            // Set return type of symbol, or null if symbol is not found,
            // is unresolved, or ambiguous.
            // `call.AssignmentTarget` allows the symbol to be an unresolved
            // local and is also used to resolve ambiguity between getter/setter.
            // `call.Invoked` is true when called as a function, and is used
            // to determine if a lambda is invoked or just passed as a variable.
            // Marks an error when there is no match.
            // Returns the symbol that generated the type (or null if
            // there wasn't one)
            Symbol? EvalCall(
                Rval? call,
                List<Rval>? args,
                string rejectName = "")
            {
                if (call == null)
                    return null;

                // Done if we already have return type
                if (call.Type != null)
                    return null;

                // Find in tuple
                var inType = call.InType;
                var token = call.Token;
                if (inType != null && inType.IsTuple && inType.IsSpecialized)
                {
                    if (inType.TupleSymbols.Length == 0)
                    {
                        Reject(token, $"The type '{inType}' is an anonymous type without field names, so cannot be resolved with '.'");
                        return null;
                    }
                    var i = Array.FindIndex(inType.TupleSymbols, f => f.SimpleName == token.Name);
                    if (i < 0)
                    {
                        Reject(token, $"'{token}' is an undefined symbol in the named tuple '{inType}'");
                        return null;
                    }
                    call.Type = inType.TypeArgs[i];
                    if (i < inType.TupleSymbols.Length)
                        token.AddInfo(inType.TupleSymbols[i]);
                    assembly.AddOpNoImp(token, $"tuple {token}");
                    return null;
                }

                // Find local
                var localInfo = inType == null ? FindLocal(call.Token) : (null, 0);
                var local = localInfo.sym;
                if (local != null && (local.IsLocal || local.IsFunParam))
                {
                    token.AddInfo(local);
                    assembly.AddOpLdlr(local.Token, localInfo.index);
                    if (local.IsLocal && local.Type == null && !call.AssignmentTarget)
                    {
                        Reject(token, $"'{token}' has an unresolved type");
                        Reject(local.Token, $"'{token}' has an unresolved type");
                    }
                    if (local.IsFunParam && local.Type == null)
                    {
                        Reject(token, $"'{token}' has an unresolved type");
                    }
                    // When invoking a lambda, resolve the function call
                    // in FindCompatibleFunction below.
                    if (local.Type == null 
                        || !local.Type.IsFun && !local.Type.IsLambda
                        || !call.Invoked)
                    {
                        call.Type = local.Type;
                        call.IsLocal = true;
                        return local;
                    }
                }

                // Find primary in the local/global scope
                var primary = local;
                if (primary == null)
                {
                    if (inType == null)
                        primary = GetPrimaryInType(call.Name, function.ParentModule);
                    else
                        primary = GetPrimaryInType(call.Name, inType);
                }

                List<Symbol> candidates;
                if (local != null)
                    candidates = new List<Symbol>() { local };
                else if (primary != null)
                    candidates = new List<Symbol>() { primary };
                else if (inType == null)
                    candidates = FindGlobal(call.Name);
                else
                    candidates = FindInType(call.Name, inType);

                // Exactly 1 primary or multiple functions
                if (RejectUndefinedOrAmbiguousPrimary(call, candidates))
                    return null;

                // Handle fields, types, modules, and setter
                if (candidates.Count == 1
                    && (candidates[0].IsField || candidates[0].IsAnyTypeOrModule))
                {
                    var candidate = candidates[0];
                    if (call.IsStatic != candidate.IsStatic)
                    {
                        Reject(call.Token, "Static mismatch");
                        return null;
                    }

                    if (candidate.IsAnyType && call.Invoked)
                        return FindCompatibleConstructor(call, candidate, args);

                    if (candidate.IsField && candidate.Type != null && inType != null && inType.TypeArgs.Length != 0)
                        candidate = table.CreateSpecializedType(candidate, inType.TypeArgs, null);
                    token.AddInfo(candidate);

                    // Static type or module, e.g. int.MinValue, or Log.info,
                    // but not int().MinValue
                    if (candidate.IsAnyTypeOrModule)
                    {
                        token.Type = eTokenType.TypeName;
                        if (call.Dot || candidate.FullName == SymTypes.Nil)
                        {
                            // Substitute generic parameter
                            if (candidate.IsTypeParam)
                                call.Type = table.GetGenericParam(candidate.GenericParamNum());
                            else
                                call.Type = candidate;

                            call.IsStatic = true;
                            return candidate;
                        }
                        else
                        {
                            Reject(token, $"'{token}' is a {candidate.KindName}, which is not valid when used like this");
                            return candidate;
                        }
                    }
                    // Field
                    if (candidate.IsField)
                    {
                        if (candidate.Type == null)
                        {
                            Reject(token, $"'{token}' has an unresolved type");
                            return candidate;
                        }
                        Debug.Assert(candidate.Type.Parent!.FullName != SymTypes.Ref);
                        call.Type = candidate.Type;

                        // A field is the same thing as a getter returning a mutable ref
                        MakeIntoRef(call);
                        assembly.AddOpNoImp(candidate.Token, $"field {candidate.FullName}");
                        return candidate;
                    }
                }

                // Find the function to call
                var compatibleFun = FindCompatibleFunction(call, candidates, args, rejectName);

                if (compatibleFun != null)
                {
                    if (!call.DontAddCallInfo)
                        call.Token.AddInfo(compatibleFun);
                    call.Type = compatibleFun.FunReturnType;
                    assembly.AddOpCall(call.Token, compatibleFun);
                    return compatibleFun;
                }
                return null;
            }

            // Find a compatible constructor function
            Symbol? FindCompatibleConstructor(Rval call, Symbol newType, List<Rval>? args)
            {
                call.Token.Type = eTokenType.TypeName;
                if (newType.IsSpecialized)
                    throw new Exception("Compiler error: FindCompatibleConstructor, unexpected specialized type");

                if (newType.IsTypeParam)
                {
                    // A type parameter returns a function returning itself
                    if (call.TypeArgs.Length != 0)
                        Reject(call.Token, $"Expecting 0 type parameters, but got '{call.TypeArgs.Length}'");
                    if (args != null && args.Count != 0)
                        Reject(call.Token, $"New generic type with arguments not supported yet");
                    call.Type = table.GetGenericParamConstructor(newType.GenericParamNum());
                    return call.Type;
                }

                // Search for `new` function
                Debug.Assert(call.InType == null);
                call.Name = "new";
                call.InType = newType;
                return EvalCall(call, args, $"'new' (constructor for '{newType}')");
            }

            // Given the call and its parameters, find the best matching function.
            // If there is an error, mark it and give feedback on possible matches.
            // When args is null, it means the there was an error evaluating the
            // parameter types, so just try to give good feedback.
            // Returns the specialized function or NULL if no matches found.
            Symbol? FindCompatibleFunction(
                Rval call,
                List<Symbol> candidates,
                List<Rval> ?args,
                string rejectName)
            {
                if (call.Type != null)
                {
                    Reject(call.Token, "Function or type name expected");
                    return null;
                }

                // Unresolved arguments
                if (args == null)
                {
                    // Give some feedback on the functions that could be called
                    foreach (var sym in candidates)
                        call.Token.AddInfo(sym);

                    // If there is just 1 symbol without generic arguments,
                    // assume that is what was called. This gives better type
                    // inference than making it unresolved.
                    if (candidates.Count != 1 || candidates[0].HasGenericArg)
                        return null;
                    args = new List<Rval>();
                }

                // Insert inType type as receiver parameter
                if (call.InType != null && !call.IsStatic)
                {
                    call.Type = call.InType;
                    args.Insert(0, call);
                }

                // Generate list of matching symbols, update
                // the candidates to their specialized form
                var compatibleErrors = CallCompatible.Compatible;
                var matchingFuns = new List<Symbol>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    var compat = IsCallCompatible(call, candidates[i], args);
                    compatibleErrors |= compat.Compatibility;
                    if (compat.SpecializedFun != null)
                    {
                        matchingFuns.Add(compat.SpecializedFun);
                        candidates[i] = compat.SpecializedFun;
                    }
                }

                if (matchingFuns.Count == 0)
                {
                    // Incorrect type
                    RejectSymbols(call.Token, candidates, 
                        $"No function {rejectName} taking '{Rval.ParamTypes(args, call.TypeArgs)}'" 
                        + $" in scope: {PrintCompatibleError(compatibleErrors)}");

                    // If there was just 1 symbol, assume that is what was called.
                    // This gives better type inference than making it unresolved.
                    if (candidates.Count != 1)
                        return null;
                    matchingFuns = new List<Symbol>() { candidates[0] };
                }

                // In case of tie between getter/setter, remove based on AssignmentTarget
                if (matchingFuns.Count == 2 && matchingFuns[0].IsGetter == matchingFuns[1].IsSetter)
                    matchingFuns = matchingFuns.Where(s => call.AssignmentTarget == s.IsSetter).ToList();

                if (matchingFuns.Count != 1)
                {
                    RejectSymbols(call.Token, matchingFuns, 
                        $"Multiple functions {rejectName} taking '{Rval.ParamTypes(args, call.TypeArgs)}' in scope");
                    return null;
                }

                // Compile lambdas now that we can infer the type
                var func = matchingFuns[0];
                for (int i = 0; i < args.Count; i++)
                {
                    var arg = args[i];
                    if (arg.LambdaSyntax != null)
                    {
                        // Hm, there is a compiler error here, TBD, don't have time now
                        if (func.FunParamTypes.Length <= i || !func.FunParamTypes[i].IsLambda)
                        {
                            Reject(args[i].Token, "Compiler error: Lambda resolution failure");
                            return null;
                        }

                        Debug.Assert(func.FunParamTypes[i].IsLambda);
                        var lambda = func.FunParamTypes[i];
                        PostGenLambda(arg.LambdaSyntax, lambda.FunParamTypes);
                    }
                }

                // This is either a function or lambda.  The function has the
                // type directly, but the lambda is a specialization of $lambda<T>.
                // TBD: Make function a specialization of $fun<T> so lambda and
                //      function have the same layout.
                var lambdaOrFunType = func.IsFun ? func : func.Type;
                if (lambdaOrFunType == null)
                {
                    Reject(call.Token, "Compiler error, invalid lambda type");
                    return null;
                }
                return lambdaOrFunType;
            }

            // Checks if the function call is compatible, return the possibly
            // specialized function.  Returns null if not compatible.
            CallMatch IsCallCompatible(Rval call, Symbol func, List<Rval> args)
            {
                if (!func.IsFun)
                    return IsLambdaCompatible(call, func, args);

                if (func.IsMethod && call.InType != null)
                {
                    if (call.IsStatic && !func.IsStatic)
                        return new CallMatch(null, CallCompatible.StaticCallToNonStaticMethod);
                    if (!call.IsStatic && func.IsStatic)
                        return new CallMatch(null, CallCompatible.NonStaticCallToStaticMethod);
                }

                // Find type args supplied by the source code (explicitly or inferred)
                var typeArgs = InferTypeArgs(func, call.TypeArgs, args, func.FunParamTypes);

                // Use type args from function (or constraint) if supplied
                if (func.TypeArgs.Length != 0)
                {
                    if (typeArgs.Length != 0)
                        return new CallMatch(null, CallCompatible.TypeArgsSuppliedByConstraint);
                    typeArgs = func.TypeArgs;
                }

                // Verify number of type arguments
                var typeArgsExpectedCount = func.GenericParamCount();
                if (typeArgs.Length != typeArgsExpectedCount)
                {
                    if (typeArgs.Length == 0 && typeArgsExpectedCount != 0)
                        return new CallMatch(null, CallCompatible.ExpectingSomeTypeArgs);
                    if (typeArgs.Length != 0 && typeArgsExpectedCount == 0)
                        return new CallMatch(null, CallCompatible.ExpectingNoTypeArgs);
                    return new CallMatch(null, CallCompatible.WrongNumberOfTypeArgs);
                }

                // Convert generic type args to specialized
                if (!func.IsSpecialized && typeArgs.Length != 0)
                    func = table.CreateSpecializedType(func, typeArgs, null);

                // Special case: "new" constructor was not specialized
                // because it bypassed eval in FindCompatibleConstructor
                if (call.Name == "new" && typeArgs.Length != 0
                    && args.Count != 0 && !args[0].Type.IsSpecialized
                    && args[0].Type.GenericParamCount() == typeArgs.Length)
                {
                    args[0].Type = table.CreateSpecializedType(args[0].Type, typeArgs);
                }


                // Don't consider first parameter of static method
                var funParams = new Span<Symbol>(func.FunParamTypes);
                if (func.IsMethod && func.IsStatic)
                    funParams = funParams.Slice(1);

                return AreParamsCompatible(call, func, args, funParams);
            }


            CallMatch IsLambdaCompatible(Rval call, Symbol variable, List<Rval> args)
            {
                if (!variable.IsFunParam && !variable.IsLocal && !variable.IsField)
                    return new CallMatch(null, CallCompatible.NotAFunction);
                
                var lambda = variable.Type;
                if (lambda == null 
                        || !lambda.IsLambda
                        || lambda.TypeArgs.Length != 1)
                    return new CallMatch(null, CallCompatible.NotAFunction);
                
                // Get lambda parameters
                var funParams = lambda.TypeArgs[0];
                if (funParams.TypeArgs.Length != 2)
                    return new CallMatch(null, CallCompatible.NotAFunction);

                return AreParamsCompatible(call, variable, args, funParams.TypeArgs[0].TypeArgs);
            }

            CallMatch AreParamsCompatible(Rval call, Symbol func, List<Rval> args, Span<Symbol> funParams)
            {
                if (call.AssignmentTarget && func.IsGetter && args.Count != 0)
                {
                    args = new List<Rval>(args);
                    args.RemoveAt(args.Count - 1);
                }
                
                // Match up the arguments (TBD: default parameters)
                if (args.Count != funParams.Length)
                    return new CallMatch(null, CallCompatible.WrongNumberOfParameters);
                for (var i = 0; i < funParams.Length; i++)
                {
                    // Receiver for generic interface always matches
                    // since it came from the constaint
                    if (i == 0
                            && func.Concrete.Parent!.IsInterface
                            && !func.IsStatic
                            && args[i].Type!.IsGenericArg)
                        continue;

                    // Auto-deref references
                    var arg = args[i];
                    var param = funParams[i];

                    var compat = IsParamConvertable(arg.Type!, param);
                    if (compat != CallCompatible.Compatible)
                        return new CallMatch(null, compat);
                }
                return new CallMatch(func, CallCompatible.Compatible);
            }

            bool IsParamConvertableReject(Token t, Symbol argType, Symbol paramType)
            {
                var compat = IsParamConvertable(argType, paramType);
                if (compat == CallCompatible.Compatible)
                    return true;
                Reject(t, $"Cannot convert type '{argType}' to '{paramType}'. {PrintCompatibleError(compat)}");
                return false;
            }


            // Can the given argument be converted to the parameter type?
            // NOTE: Match all lambda's since we haven't compiled them yet
            CallCompatible IsParamConvertable(Symbol argType, Symbol paramType)
            {
                argType = DerefRef(argType);
                paramType = DerefRef(paramType);

                // The generic lambda type matches all lambdas because
                // the lambda type is set later when it is compiled.
                if (paramType.IsLambda && argType.IsLambda)
                    return CallCompatible.Compatible;

                // An exact match on the parameters
                if (TypesMatch(argType, paramType))
                    return CallCompatible.Compatible;

                // Implicit conversion from nil to *T or from *T to *void
                if (paramType.Parent!.FullName == SymTypes.RawPointer)
                {
                    if (argType.FullName == SymTypes.Nil)
                        return CallCompatible.Compatible;
                    if (argType.Parent!.FullName == SymTypes.RawPointer && DerefPointers(paramType) == typeVoid)
                        return CallCompatible.Compatible;
                }

                // Implicit conversion to interface type
                if (paramType.IsInterface)
                {
                    var iface = ConvertToInterfaceInfo(argType, paramType);

                    // TBD: Need to pass interface table up to generate call parameter.
                    //      Currently it just spits it out here (after parameters were
                    //      emitted, before we even know if this interface is taken
                    return iface.Compat;
                }

                return CallCompatible.IncompatibleParameterTypes;
            }



            // If possible, convert concrete type to an interface.
            // If not possible, generate a list of what's missing.
            InterfaceInfo ConvertToInterfaceInfo(Symbol concrete, Symbol iface)
            {
                // Generic arguments must be resolved
                Debug.Assert(iface.GenericParamCount() == 0 || iface.IsSpecialized);
                Debug.Assert(concrete.GenericParamCount() == 0 || concrete.IsSpecialized);
                Debug.Assert(iface.IsInterface);

                var conversionName = InterfaceInfo.GetName(concrete, iface);
                if (table.InterfaceInfos.TryGetValue(conversionName, out var info))
                    return info;

                if (TypesMatch(concrete, iface))
                {
                    var ifaceIdentity = new InterfaceInfo(concrete, iface, new List<Symbol>(),
                        CallCompatible.Compatible);
                    table.InterfaceInfos[conversionName] = ifaceIdentity;
                    return ifaceIdentity;
                }

                if (concrete.IsInterface)
                {
                    var ifaceFail = new InterfaceInfo(concrete, iface, new List<Symbol>(),
                        CallCompatible.InterfaceToInterfaceConversionNotSupportedYet);
                    table.InterfaceInfos[conversionName] = ifaceFail;
                    return ifaceFail;
                }

                // Default to fail if we recursively re-encounter this interface
                table.InterfaceInfos[conversionName] = new InterfaceInfo(concrete, iface, 
                    new(), CallCompatible.InterfaceGenerating);

                // Find list of implemented/failed functions
                var failedFuns = new List<Symbol>();        // From interface
                var implementedFuns = new List<Symbol>();   // From concrete
                foreach (var ifaceFun in iface.Concrete.Children)
                {
                    if (!ifaceFun.IsFun)
                        continue; // Skip type parameters
                    if (ifaceFun.IsStatic)
                        continue;  // TBD: Deal with static

                    // Find functions in the concrete type or its module
                    var concreteFuns = new List<Symbol>();
                    AddSymbolsNamedInType(ifaceFun.SimpleName, concrete, concreteFuns);
                    AddMethodsInModuleWithType(ifaceFun.SimpleName, concrete.ParentModule, concrete, concreteFuns);

                    // Specialize interface and functions.
                    // NOTE: Specialized interfaces don't currently carry
                    //       the specialized function signatures.
                    //       See TBD in InterfaceInfo
                    Symbol ifaceFunSpecialized = ifaceFun;
                    if (iface.IsSpecialized)
                        ifaceFunSpecialized = table.CreateSpecializedType(ifaceFun, iface.TypeArgs);
                    if (concrete.IsSpecialized)
                        for (var i = 0; i < concreteFuns.Count; i++)
                            concreteFuns[i] = table.CreateSpecializedType(concreteFuns[i], concrete.TypeArgs);
                        
                    // Find interface with exact matching parametets
                    var noMatch = true;
                    foreach (var f in concreteFuns)
                    {
                        if (!f.IsFun)
                            continue;  // TBD: Match interface getter/setter with concrete type

                        if (f.IsStatic)
                            continue; // TBD: Deal with static

                        // Verify parameters
                        // NOTE: Ignore the first parameter because it is the interface
                        // NOTE: Verifier should prevent duplicate functions
                        //       with exactly matching parameter types
                        var ifaceParams = ifaceFunSpecialized.FunParamTypes;
                        var funParams = f.FunParamTypes;
                        Debug.Assert(funParams.Length != 0);
                        Debug.Assert(funParams[0].Concrete.FullName == concrete.Concrete.FullName);
                        Debug.Assert(ifaceParams[0].Concrete.FullName == iface.Concrete.FullName);
                        var match = funParams.Length == ifaceParams.Length;
                        for (int i = 1; i < funParams.Length && match; i++)
                            if (IsParamConvertable(funParams[i], ifaceParams[i]) != CallCompatible.Compatible)
                                match = false;

                        // Verify returns
                        var ifaceReturns = ifaceFunSpecialized.FunReturnTypes;
                        var funReturns = f.FunReturnTypes;
                        if (ifaceReturns.Length != funReturns.Length)
                            match = false;
                        for (int i = 0; i < funReturns.Length && match; i++)
                            if (IsParamConvertable(funReturns[i], ifaceReturns[i]) != CallCompatible.Compatible)
                                match = false;

                        if (match)
                        {
                            implementedFuns.Add(f);
                            noMatch = false;
                            break;
                        }
                    }
                    if (noMatch)
                        failedFuns.Add(ifaceFunSpecialized);
                }

                var pass = failedFuns.Count == 0 && implementedFuns.Count != 0;
                var ifaceInfo = new InterfaceInfo(concrete, iface, 
                    pass ? implementedFuns : failedFuns, 
                    pass ? CallCompatible.Compatible : CallCompatible.InterfaceNotImplementedByType);
                table.InterfaceInfos[ifaceInfo.ToString()] = ifaceInfo;
                return ifaceInfo;
            }


            // Infer the type arguments if not given.
            Symbol[] InferTypeArgs(Symbol func, Symbol []typeArgs, List<Rval> args, Symbol []funParamTypes)
            {
                if (typeArgs.Length != 0)
                    return typeArgs;        // Supplied explicitly by call
                if (func.TypeArgs.Length != 0)
                    return typeArgs;        // Supplied by the function

                var typeArgsNeeded = func.GenericParamCount();
                if (typeArgsNeeded == 0)
                    return typeArgs;

                // Walk through parameters looking for matches
                var inferredTypeArgs = new Symbol[typeArgsNeeded];
                int numArgs = Math.Min(args.Count, funParamTypes.Length);
                for (int paramIndex = 0;  paramIndex < numArgs;  paramIndex++)
                    if (!InferTypeArg(args[paramIndex].Type!, funParamTypes[paramIndex], inferredTypeArgs))
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
                funParamType = DerefRef(funParamType);

                // If it's a generic arg, use the given parameter type
                if (funParamType.IsGenericArg)
                {
                    var paramNum = funParamType.GenericParamNum();
                    if (paramNum >= inferredTypeArgs.Length)
                        throw new Exception("Compiler error: InferTypeArg, index out of range");

                    if (inferredTypeArgs[paramNum] != null)
                    {
                        // If types do not match, it's a contradiction, e.g. user calls f(0, "x") on f<T>(x T, y T).
                        // TBD: Give better error message (this just fails with 'wrong number of type args')
                        if (!TypesMatch(inferredTypeArgs[paramNum], argType))
                            return false;  // User error
                    }
                    inferredTypeArgs[paramNum] = argType;
                    return true;
                }

                // TBD: Interface conversion
                if (funParamType.IsInterface)
                {
                    // TBD: Can we infer type args when passing into an interface?
                    var argType2 = ConvertToInterfaceInfo(argType, funParamType);
                    //return true;
                }

                // If they are both the same generic type, check type parameters
                if (funParamType.IsSpecialized
                    && argType.IsSpecialized
                    && (funParamType.Parent!.FullName == argType.Parent!.FullName
                        || funParamType.IsInterface && !argType.IsInterface 
                            && ConvertToInterfaceInfo(argType, funParamType).Pass))
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

            /// <summary>
            /// Check to see if the symbol types match, ignoring tuple names
            /// and references.
            /// </summary>
            bool TypesMatch(Symbol a, Symbol b)
            {
                a = DerefRef(a);
                b = DerefRef(b);
                if (a.FullName == b.FullName)
                    return true;
                if (!a.IsSpecialized
                        || !b.IsSpecialized
                        || a.Parent!.FullName != b.Parent!.FullName
                        || a.TypeArgs.Length != b.TypeArgs.Length)
                    return false;
                for (int i = 0; i < a.TypeArgs.Length; i++)
                    if (!TypesMatch(a.TypeArgs[i], b.TypeArgs[i]))
                        return false;
                return true;
            }


            // Finds a local or parameter.  Returns null if not found.
            (Symbol? sym, int index) FindLocal(Token token)
            {
                // Find local parameters
                var name = token.Name;
                var pi = Array.FindIndex(function.FunParamTuple.TupleSymbols, f => f.SimpleName == name);
                if (pi >= 0)
                    return (function.FunParamTuple.TupleSymbols[pi], 0);
                var ri = Array.FindIndex(function.FunReturnTuple.TupleSymbols, f => f.SimpleName == name);
                if (ri >= 0)
                {
                    Reject(token, "Invalid use of return parameter");
                    return (null, 0);
                }

                // Local type parameter
                if (function.TryGetPrimary(name, out var localParam))
                    if (localParam!.IsTypeParam)
                        return (localParam, 0);

                // Find local
                if (localsByName!.TryGetValue(token, out var local))
                {
                    if (local.Symbol == null)
                        Reject(token, $"'{token}' is an out of scope local variable");
                    return (local.Symbol, local.LocalNum);
                }
                return (null, 0);
            }

            /// <summary>
            /// Find symbols in the global scope that match this token.
            /// Searches the current module or use symbols.
            /// </summary>
            List<Symbol> FindGlobal(string name)
            {
                // Find global symbols in this module
                var symbols = new List<Symbol>();
                AddFunctionsNamedInModule(name, function.ParentModule, symbols, false);

                // Search 'use' symbol for non-methods
                if (fileUses.UseSymbols.TryGetValue(name, out var useSymbols))
                    symbols.AddRange(useSymbols.Where(s => !s.IsMethod));

                // Add global constaints
                if (function.Constraints != null)
                {
                    var cons = new List<Symbol>();
                    foreach (var constraints in function.Constraints.Values)
                        AddGenericConstraints(name, constraints, cons);
                    symbols.AddRange(cons.Where(s => s.IsStatic));
                }

                RemoveLastDuplicates(symbols);
                return symbols;
            }

            /// <summary>
            /// Find symbols in the type, including functions defined in
            /// the type's module, this module, or use statements.
            /// Returns an empty symbols list if none found.
            /// </summary>
            List<Symbol> FindInType(string name, Symbol inType)
            {
                var symbols = new List<Symbol>();
                AddFunctionsNamedInModule(name, function.ParentModule, symbols, true);
                AddSymbolsNamedInType(name, inType, symbols);
                AddMethodsInModuleWithType(name, inType.ParentModule, inType, symbols);

                // Search 'use' symbol for methods with first parameter that matches
                if (fileUses.UseSymbols.TryGetValue(name, out var useSymbols))
                    symbols.AddRange(useSymbols.Where(s => s.IsMethod));

                // Add constraints when the receiver is generic
                if (function.Constraints != null
                        && inType.IsGenericArg
                        && function.Constraints.TryGetValue(inType.ToString(), out var constraints))
                    AddGenericConstraints(name, constraints, symbols);

                RemoveLastDuplicates(symbols);
                return symbols;
            }

            void AddFunctionsNamedInModule(string name, Symbol inModule, List<Symbol> symbols, bool methods)
            {
                foreach (var child in inModule.ChildrenNamed(name))
                    if (child.IsFun && child.IsMethod == methods)
                        symbols.Add(child);
            }

            // Add methods with first parameter of inType
            void AddMethodsInModuleWithType(string name, Symbol inModule, Symbol inType, List<Symbol> symbols)
            {
                // Ignore mut, etc., then just compare the non-specialized type.
                inType = inType.Concrete;
                foreach (var child in inModule.ChildrenNamed(name))
                {
                    if (!child.IsFun || !child.IsMethod)
                        continue;

                    // Compare the non-specialized type
                    //      e.g: List<#1> matches List<byte> so we get all functions
                    var parameters = child.FunParamTypes;
                    if (parameters.Length != 0 && parameters[0].Concrete.FullName == inType.FullName)
                        symbols.Add(child);
                }
            }

            // Collect constraints on generic argument inType.  Generic
            // arguments of the function are replaced with the constraint
            // generic arguments.
            void AddGenericConstraints(string name, Symbol []constraints, List<Symbol> symbols)
            {
                foreach (var constraint in constraints)
                {
                    Debug.Assert(constraint.IsInterface);
                    var i = symbols.Count;
                    AddSymbolsNamedInType(name, constraint, symbols);

                    // Replace generic type args with the given type arguments
                    while (i < symbols.Count)
                    {
                        var s = symbols[i];
                        if (s.GenericParamCount() == constraint.TypeArgs.Length)
                        {
                            symbols[i] = table.CreateSpecializedType(s, constraint.TypeArgs, null);
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
                    AddSymbolsNamedConcrete(name, inType.Parent!, symbols);
            }

            // Add all children with the given name (primary or non-extension method)
            void AddSymbolsNamedConcrete(string name, Symbol inType, List<Symbol> symbols)
            {
                foreach (var child in inType.ChildrenNamed(name))
                    if (child.IsFun)
                        symbols.Add(child);
            }

            // Return a primary (non-function) symbol, field
            // type, interface, etc., or null if not found
            Symbol? GetPrimaryInType(string name, Symbol inType)
            {
                if (inType.TryGetPrimary(name, out var s1))
                    return s1;
                if (inType.IsSpecialized && inType.Parent!.TryGetPrimary(name, out var s2))
                    return s2;
                return null;
            }

            // Create a new variable in the local scope, return null if it
            // already exists.  Don't allow shadowing.
            // NOTE: The symbol is stored, then later, replaced by its type.
            //       This is done because the type is not always known here.
            Symbol? CreateLocal(Token token, bool forLambda = false)
            {
                var pi = Array.FindIndex(function.FunParamTuple.TupleSymbols, f => f.SimpleName == token);
                var ri = Array.FindIndex(function.FunReturnTuple.TupleSymbols, f => f.SimpleName == token);
                if (pi >= 0 || ri >= 0)
                {
                    Reject(token, $"'{token}' is already defined as a local parameter");
                    return null;
                }

                if (function.TryGetPrimary(token, out var primary))
                {
                    Reject(token, $"'{token}' is already defined as a local parameter.");
                    return null;
                }
                if (localsByName!.TryGetValue(token, out var localSymbol))
                {
                    if (localSymbol.Symbol != null)
                    {
                        Reject(token, $"'{token}' is already defined as a local variable in this scope.");
                        return null;
                    }

                    if (localSymbol.ScopeNum > scopeNum)
                    {
                        Reject(token, $"'{token}' is already defined as a local variable in a previous scope.");
                        return null;
                    }
                }

                var local = new Symbol(SymKind.Local, null, token);
                var localScope = newVarScope >= 0 && !forLambda ? newVarScope : scopeNum;
                localsByName![token] = new LocalSymbol(local, localScope, localsByIndex!.Count);
                assembly.AddOpUseLocal(token, localsByIndex.Count, newVarCodeIndex);
                localsByIndex.Add(local);
                return local;
            }

            void BeginLocalScope(Token token)
            {
                scopeNum++;
                assembly.AddOp(Op.BeginScope, token, 0);
                scopeToken![scopeNum] = token;
            }

            void EndLocalScope()
            {
                foreach (var local in localsByName!.Values)
                {
                    if (local.ScopeNum == scopeNum)
                        local.Symbol = null;
                    else if (local.ScopeNum > scopeNum)
                        local.ScopeNum = scopeNum;
                }
                assembly.AddOp(Op.EndScope, scopeToken![scopeNum], 0);
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
            // TBD: Only allow one getter/setter pair
            bool RejectUndefinedOrAmbiguousPrimary(Rval call, List<Symbol> candidates)
            {
                // Undefined symbol
                if (candidates == null || candidates.Count == 0)
                {
                    if (call.InType == null)
                        Reject(call.Token, $"'{call.Name} is an undefined symbol in the local or global scope");
                    else
                        Reject(call.Token, $"'{call.Name}' is an undefined symbol in the {call.InType.Kind} '{call.InType}'");
                    return true;
                }

                // Ambiguous primary symbol
                var functions = 0;
                var primaries = 0;
                foreach (var symbol in candidates)
                    if (symbol.IsFun)
                        functions++;
                    else
                        primaries++;

                var reject = primaries >= 2 || primaries == 1 && functions >= 1;
                if (reject)
                    RejectSymbols(call.Token, candidates,
                        $"'{call.Token}' is ambiguous because there are both functions and types (or fields) with the same name");
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

            void RejectExpr(SyntaxExpr ex, string message) 
            {
                table.Reject(ex.Token, message);
                foreach (var e in ex)
                    RejectExpr(e, message);
            }

            static string PrintCompatibleError(CallCompatible c)
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
                if (c.HasFlag(CallCompatible.InterfaceToInterfaceConversionNotSupportedYet))
                    errors.Add("Interface to interface conversion not supported yet");
                if (c.HasFlag(CallCompatible.InterfaceNotImplementedByType))
                    errors.Add("The type doesn't implement the interface");
                if (c.HasFlag(CallCompatible.InterfaceGenerating))
                    errors.Add("The interface was used while being generated");
                return string.Join(",", errors.ToArray());
            }

        }

    }
}
