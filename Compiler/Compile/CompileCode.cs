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
        static WordSet sDerefRef = new WordSet("Zurfur.Ref`1");
        static WordSet sDerefPointers = new WordSet("Zurfur.RawPointer`1 Zurfur.Pointer`1");
        static WordMap<string> sBinOpNames = new WordMap<string> {
            {"+", "_opAdd"}, {"+=", "_opAdd"}, {"-", "_opSub"}, {"-=", "_opSub"},
            {"*", "_opMul"}, {"*=", "_opMul"}, {"/", "_opDiv"}, {"/=", "_opDiv"},
            {"%","_opRem" }, {"%=","_opRem" },
            {"..", "_opRange" }, {"..+", "_opRange" },
            {"==", "_opEq"}, {"!=", "_opEq" }, {"in", "_opIn" },
            {">", "_opCmp" }, {">=", "_opCmp" }, {"<", "_opCmp" }, {"<=", "_opCmp" },
            {"]", "_opIndex" },

            // TBD: Bit operators should be restricted to i32, u32, int, u64 types
            {"<<", "_opBitShl"}, {"<<=", "_opBitShl"}, {">>", "_opBitShr"}, {">>=", "_opBitShr"},
            {"&", "_opBitAnd"}, {"&=", "_opBitAnd"}, {"|", "_opBitOr"}, {"|=", "_opBitOr"},
            {"~", "_opBitXor"}, {"~=", "_opBitXor"},

            // TBD: Logical operators should be restricted to bool type
            {"and", "_opLogicalAnd"}, {"or", "_opLogicalOr"}, {"not", "_opLogicalNot"}
        };

        static bool TypesMatch(Symbol a, Symbol b)
            => Symbol.TypesMatch(a, b);


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
            public bool IsSetter;
            public bool IsLocal;
            public bool IsExplicitRef;
            public bool IsStatic;

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
            var y2 = Math.Max(y1, synFunc.Statements == null ? 0 : synFunc.Statements.Token.Y);
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
                else if (name == "ret" || name == "yld")
                    GenReturnStatement(s);
                else
                {
                    // Generate top level expression, e.g. f(x), etc.
                    var rval = GenExpr(s);
                    var symbol = EvalType(rval, true);
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
                EvalType(cond);
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

            // Get the for loop iterator type, e.g. T in object.iterator.next() -> Maybe<T>
            Symbol? GenForIterator(Token token, Symbol inType)
            {
                var getIter = GenFindAndCall(token, DerefRef(inType), "iterator");
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
                // This marks the local with symbol info, which we don't want here
                var call = new Rval(token, name) { InType = inType };
                return FindCompatibleFunction(call, FindInType(name, inType), new List<Rval>(),
                    $"'{name}' in the type '{inType}'", false);
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
                EvalType(rval);
                if (rval == null || rval.Type == null)
                    return null;

                ex.Token.AddInfo(rval.Type);
                var functionType = function.FunReturnType;

                // TBD: This is to temporarily gloss over pointers, Maybe, and nil
                //      Implicit conversion from nil to *T or Maybe<T>
                if (rval.Type.FullName == SymTypes.Nil
                    && (functionType.Parent!.FullName == SymTypes.RawPointer)
                        || functionType.Parent!.FullName == SymTypes.Maybe)
                    return rval;

                if (!TypesMatch(DerefRef(functionType), DerefRef(rval.Type)))
                {
                    Reject(ex.Token, $"Incorrect return type, expecting '{functionType}', got '{rval.Type}'");
                }
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
                {
                    ex.Token.Type = eTokenType.ReservedVar;
                    return GenIdentifier(ex);
                }
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
                var rval = new Rval(ex.Token) { Type = numberType, IsUntypedConst = untypedConst };

                return rval;
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
                EvalType(leftDot, false, true);
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
                EvalType(left);
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

                EvalType(value);
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

                EvalType(rval);
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
                EvalType(right);
                var assignedSymbol = EvalType(left, true);
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
                if (left.IsSetter)
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
                    if (!TypesMatch(args[1], rightType))
                    {
                        Reject(ex.Token, $"Types must match: setter({args[1].FullName}) = ({rightType.FullName})");
                        return null;
                    }
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

                if (!TypesMatch(DerefRef(leftType), DerefRef(rightType)))
                {
                    Reject(ex.Token, $"Types must match: ({leftType.FullName}) = ({rightType.FullName})");
                    return null;
                }
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
                EvalType(op);
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

                var operatorName = sBinOpNames[token];
                if (args.Count == 1)
                {
                    if (token == "-")
                        operatorName = "_opNeg";
                    else if (token == "~")
                        operatorName = "_opBitNot";
                }

                // Find static operators from either argument
                var call = new Rval(token, operatorName) { IsStatic = true };
                var candidates = FindInType(operatorName, DerefRef(args[0].Type!));
                if (args.Count >= 2)
                    candidates.AddRange(FindInType(operatorName, DerefRef(args[1].Type!)));
                RemoveLastDuplicates(candidates);

                if (candidates.Count == 0)
                {
                    Reject(token, $"No function '{operatorName}' (operator '{token}') " 
                        + $"taking '{Rval.ParamTypes(args, call.TypeArgs)}' is in scope.");
                    return null;
                }
                var funType = FindCompatibleFunction(call, candidates, args,
                                $" '{operatorName}' (operator '{token}')");
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

                var candidates = FindCandidates(call);
                Symbol? funType;
                if (candidates.Count == 1 && candidates[0].IsAnyType)
                    funType = FindCompatibleConstructor(call, candidates, args);
                else
                    funType = FindCompatibleFunction(call, candidates, args, $"'{call.Token}'");
                if (funType == null)
                    return null;

                return new Rval(call.Token) { Type = funType};
            }

            Symbol? FindCompatibleConstructor(Rval call, List<Symbol> candidates, List<Rval> ?args)
            {
                var newType = candidates[0];
                call.Token.Type = eTokenType.TypeName;
                if (newType.IsSpecialized)
                    throw new Exception("Compiler error: FindCompatibleConstructor, unexpected specialized type");

                if (newType.IsTypeParam)
                {
                    if (call.TypeArgs.Length != 0)
                        Reject(call.Token, $"Expecting 0 type parameters, but got '{call.TypeArgs.Length}'");
                    if (args != null && args.Count != 0)
                        Reject(call.Token, $"New generic type with arguments not supported yet");
                    return table.GetGenericParam(newType.Order);
                }

                // Search for `new` function
                Debug.Assert(call.InType == null);
                call.Name = "new";
                call.IsStatic = true; // Constructor is static
                return FindCompatibleFunction(call, FindInType(call.Name, newType), args,
                            $"'new' (constructor for '{call.InType}')");
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
                    EvalType(rval);
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
                EvalType(cond);
                EvalType(condIf);
                EvalType(condElse);

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

            // Set return type of symbol, or null if symbol is not found,
            // is unresolved, or ambiguous.
            // `assignmentTarget` allows the symbol to be an unresolved local
            // and is also used to resolve ambiguity between getter/setter.
            // Mark an error when there is no match.
            // Returns the symbol that generated the type (or null if
            // there wasn't one)
            //
            // TBD: This needs to be moved so lambda's can be called
            //      properly
            Symbol? EvalType(Rval? rval,
                bool assignmentTarget = false,
                bool allowStaticType = false)
            {
                if (rval == null)
                    return null;

                // Done if we already have return type
                if (rval.Type != null)
                    return null;

                // Check for local first
                var inType = rval.InType;
                var token = rval.Token;
                if (inType == null)
                {
                    var local = FindLocal(rval.Token);
                    if (local.sym != null)
                    {
                        if (local.sym.IsLocal)
                        {
                            token.AddInfo(local.sym);
                            if (local.sym.Type == null && !assignmentTarget)
                            {
                                Reject(token, $"'{token}' has an unresolved type");
                                Reject(local.sym.Token, $"'{token}' has an unresolved type");
                            }
                            rval.Type = local.sym.Type;
                            rval.IsLocal = true;
                            assembly.AddOpLdlr(local.sym.Token, local.index);
                            return local.sym;
                        }
                        if (local.sym.IsFunParam)
                        {
                            token.AddInfo(local.sym);
                            if (local.sym.Type == null)
                                Reject(token, $"'{token}' has an unresolved type");
                            rval.Type = local.sym.Type;
                            rval.IsLocal = true;
                            assembly.AddOpLdlr(local.sym.Token, local.index);
                            return local.sym;
                        }
                    }
                }

                // Find in tuple
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
                    rval.Type = inType.TypeArgs[i];
                    if (i < inType.TupleSymbols.Length)
                        token.AddInfo(inType.TupleSymbols[i]);
                    assembly.AddOpNoImp(token, $"tuple {token}");
                    return null;
                }


                var candidates = FindCandidates(rval);

                // Exactly 1 primary or multiple functions
                if (RejectUndefinedOrAmbiguousPrimary(rval, candidates))
                    return null;

                // Filter out functions, except getters and setters
                var oldCalls = candidates.ToArray();
                candidates.RemoveAll(callFunc => callFunc.IsFun && !(callFunc.IsGetter || callFunc.IsSetter));

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
                    RejectSymbols(token, candidates, $"'{token}' is ambiguous");
                    return null;
                }

                // Generic parameter subsitution
                var sym = candidates[0];
                Debug.Assert(!sym.IsLocal && !sym.IsFunParam);

                if ((sym.IsFun || sym.IsField)
                    && sym.Type != null && inType != null && inType.TypeArgs.Length != 0)
                {
                    sym = table.CreateSpecializedType(sym, inType.TypeArgs, null);
                }

                token.AddInfo(sym);

                if (sym.IsAnyTypeOrModule)
                {
                    token.Type = eTokenType.TypeName;
                    if (allowStaticType || sym.FullName == SymTypes.Nil)
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

                if (sym.IsField)
                {
                    if (sym.Type == null)
                    {
                        Reject(token, $"'{token}' has an unresolved type");
                        return sym;
                    }
                    Debug.Assert(sym.Type.Parent!.FullName != SymTypes.Ref);
                    rval.Type = sym.Type;

                    // A field is the same thing as a getter returning a mutable ref
                    MakeIntoRef(rval);
                    assembly.AddOpNoImp(sym.Token, $"field {sym.FullName}");
                    return sym;
                }

                if (sym.IsFun)
                {
                    rval.Type = sym.FunReturnType;
                    assembly.AddOpCall(sym.Token, sym);
                    return sym;
                }
                Reject(token, $"'{token}' compiler failure: '{sym}' is {sym.KindName}");
                Debug.Assert(false);
                return sym;
            }

            List<Symbol> FindCandidates(Rval rval)
            {
                if (rval.InType == null)
                    return FindGlobal(rval.Token);
                else
                    return FindInType(rval.Token, rval.InType);
            }

            // Given the call and its parameters, find the best matching function.
            // If there is an error, mark it and give feedback on possible matches.
            // When args is null, it means the there was an error evaluating the
            // parameter types, so just try to give good feedback.
            Symbol? FindCompatibleFunction(
                Rval call,
                List<Symbol> candidates,
                List<Rval> ?args,
                string rejectName,
                bool addSymbolInfo = true)
            {
                if (call.Type != null)
                {
                    Reject(call.Token, "Function or type name expected");
                    return null;
                }

                // Exactly 1 primary or multiple functions
                if (RejectUndefinedOrAmbiguousPrimary(call, candidates))
                    return null;

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
                    var (specializedFun, isCompatible) = IsCallCompatible(candidates[i], call, args);
                    compatibleErrors |= isCompatible;
                    if (specializedFun != null)
                    {
                        matchingFuns.Add(specializedFun);
                        candidates[i] = specializedFun;
                    }
                }

                if (matchingFuns.Count == 0)
                {
                    // Incorrect type
                    if (addSymbolInfo)
                        RejectSymbols(call.Token, candidates, 
                            $"No function {rejectName} taking '{Rval.ParamTypes(args, call.TypeArgs)}'" 
                            + $" in scope: {PrintCompatibleError(compatibleErrors)}");
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
                if (addSymbolInfo)
                    call.Token.AddInfo(lambdaOrFunType);
                assembly.AddOpCall(call.Token, lambdaOrFunType!);
                return lambdaOrFunType!.FunReturnType;
            }

            // Checks if the function call is compatible, return the possibly
            // specialized function.  Returns null if not compatible.
            (Symbol?, CallCompatible) IsCallCompatible(Symbol func, Rval call, List<Rval> args)
            {
                if (!func.IsFun)
                    return IsLambdaCompatible(func, call, args);

                if (func.IsMethod)
                {
                    if (call.IsStatic && !func.IsStatic)
                        return (null, CallCompatible.StaticCallToNonStaticMethod);
                    if (!call.IsStatic && func.IsStatic)
                        return (null, CallCompatible.NonStaticCallToStaticMethod);
                }

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
                var typeArgsExpectedCount = func.GenericParamCount();
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
                    func = table.CreateSpecializedType(func, typeArgs, null);

                // Don't consider first parameter of static method
                var funParams = new Span<Symbol>(func.FunParamTypes);
                if (func.IsMethod && func.IsStatic)
                    funParams = funParams.Slice(1);

                return AreParamsCompatible(func, args, funParams);
            }

            (Symbol?, CallCompatible) IsLambdaCompatible(Symbol variable, Rval call, List<Rval> args)
            {
                if (!variable.IsFunParam && !variable.IsLocal && !variable.IsField)
                    return (null, CallCompatible.NotAFunction);
                
                var lambda = variable.Type;
                if (lambda == null 
                        || !lambda.IsLambda
                        || lambda.TypeArgs.Length != 1)
                    return (null, CallCompatible.NotAFunction);
                
                // Get lambda parameters
                var funParams = lambda.TypeArgs[0];
                if (funParams.TypeArgs.Length != 2)
                    return (null, CallCompatible.NotAFunction);

                return AreParamsCompatible(variable, args, funParams.TypeArgs[0].TypeArgs);
            }

            (Symbol?, CallCompatible) AreParamsCompatible(Symbol func, List<Rval> args, Span<Symbol> funParams)
            {
                // Match up the arguments (TBD: default parameters)
                if (args.Count != funParams.Length)
                    return (null, CallCompatible.WrongNumberOfParameters);
                for (var i = 0; i < funParams.Length; i++)
                {
                    // Auto-deref references
                    var argType = DerefRef(args[i].Type!);
                    var param = DerefRef(funParams[i]);

                    // Receiver for generic interface always matches
                    if (i == 0 && func.Concrete.Parent!.IsInterface && argType.IsGenericArg)
                        continue;

                    // The generic lambda type matches all lambdas because
                    // the lambda type is set later when it is compiled.
                    if (param.IsLambda && argType.IsLambda)
                        continue;

                    // Implicit conversion from nil to *T or from *T to *void
                    if (param.Parent!.FullName == SymTypes.RawPointer)
                    {
                        if (argType.FullName == SymTypes.Nil)
                            continue;
                        if (argType.Parent!.FullName == SymTypes.RawPointer && DerefPointers(param) == typeVoid)
                            continue;
                    }

                    if (!TypesMatch(argType, param))
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

                var typeArgsNeeded = func.GenericParamCount();
                if (typeArgsNeeded == 0 || typeArgsNeeded > funParamTypes.Length)
                    return typeArgs;  // Must have enough parameters to make it work

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
                    && funParamType.Parent!.FullName == argType.Parent!.FullName)
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
            /// Find symbols in the type, including functions defined in
            /// the type's module, this module, or use statements.
            /// Returns an empty symbols list if none found.
            /// </summary>
            List<Symbol> FindInType(string name, Symbol inType)
            {
                var symbols = new List<Symbol>();
                AddSymbolsNamedInType(name, inType, symbols);
                AddFunctionsNamedInModule(name, inType.Parent!, inType, symbols);
                AddFunctionsNamedInModule(name, function.Parent!, inType, symbols);

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
                if (function.Constraints == null || !inType.IsGenericArg)
                    return;
                if (!function.Constraints.TryGetValue(inType.ToString(), out var constraints))
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
                if (inType.TryGetPrimary(name, out Symbol? sym))
                    symbols.Add(sym!);
                foreach (var child in inType.ChildrenNamed(name))
                    if (child.IsFun && child.SimpleName == name)
                        symbols.Add(child);
            }

            // Walk up `inModule` to find the module, then collect functions `inType`
            void AddFunctionsNamedInModule(string name, Symbol inModule, Symbol inType, List<Symbol> symbols)
            {
                while (inModule != null && !inModule.IsModule)
                    inModule = inModule.Parent!;

                // Ignore mut, etc., then just compare the non-specialized type.
                if (inType.IsSpecialized)
                    inType = inType.Parent!;

                foreach (var child in inModule.ChildrenNamed(name))
                {
                    if (!child.IsFun || !child.IsMethod || child.SimpleName != name)
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

            /// <summary>
            /// Find symbols in the local/global scope that match this
            /// token.  If it's a local or parameter in the current
            /// function, stop searching.  Otherwise find a list of matching
            /// symbols in the current module or use symbols.
            /// Returns NULL and rejects token on error.
            /// </summary>
            List<Symbol> FindGlobal(Token token)
            {
                // Find local
                var (local, _) = FindLocal(token);
                if (local != null)
                    return new List<Symbol> { local };

                var symbols = new List<Symbol>();
                if (token == "my")
                {
                    Reject(token, $"'my' cannot be used in this context (static function, etc.)");
                    return symbols;
                }

                // Find global symbols in this module
                var name = token.Name;
                var module = function.Parent!;
                if (module.TryGetPrimary(name, out Symbol? sym1))
                    symbols.Add(sym1!);
                foreach (var child in module.ChildrenNamed(name))
                    if (child.IsFun && child.SimpleName == name && !child.IsMethod)
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
                    return symbols;
                }

                RemoveLastDuplicates(symbols);
                return symbols;
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
            bool RejectUndefinedOrAmbiguousPrimary(Rval call, List<Symbol> candidates)
            {
                // Undefined symbol
                if (candidates == null || candidates.Count == 0)
                {
                    if (call.InType == null)
                        Reject(call.Token, $"'{call.Name} is an undefined symbol");
                    else
                        Reject(call.Token, $"'{call.Name}' is an undefined symbol in the type '{call.InType}'");
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
                return string.Join(",", errors.ToArray());
            }

        }

    }
}
