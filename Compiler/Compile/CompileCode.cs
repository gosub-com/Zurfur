using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

using Gosub.Lex;
using Zurfur.Vm;
using static Zurfur.Compiler.CodeLib;

namespace Zurfur.Compiler;


static class ArrayHelper
{
    public static T[] Clone2<T>(this T[] array)
    {
        return (T[])(Array)array.Clone();
    }

}

/// <summary>
/// Compile the code given the output of header file generation.
/// </summary>
static class CompileCode
{
    static WordSet s_operators = new WordSet("+ - * / % & | ~ == != >= <= > < << >> and or not in |= &= += -= <<= >>= .. ..+ ]");
    static WordSet s_cmpOperators = new WordSet("== != >= <= > <");
    static WordSet s_intTypeNames = new WordSet("Zurfur.Int Zurfur.U64 Zurfur.I32 Zurfur.U32");

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

    record struct CallMatch(CallCompatible Compatibility, Symbol[] TypeArgs);

    record struct ParamMatch(
        CallCompatible Compatibility,
        Symbol[] TypeArgs,
        Symbol? FunConversion = null,
        InterfaceInfo ?InterfaceConversion = null);

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

    enum EvalFlags
    {
        None = 0,
        AssignmentTarget = 1,
        AllowStatic = 2,
        Invoked = 4,
        DontAddCallInfo = 8
    }

    class Rval
    {
        public Token Token;
        public string Name;
        public Symbol? Type;    // Type of expression (null if un-resolved)
        public Symbol? Symbol;  // The symbol that generated the type (null if not present)
        public Symbol[] TypeArgs = Array.Empty<Symbol>();
        public Symbol? InType;

        public bool IsUntypedConst; // NOTE: `3 int` is a typed const
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
                  + (Type == null ? "" : $", Type: {Type.FullName}");
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

        // Map "{interface}->{concrete}" to the interface info
        var interfaces = new Interfaces(table);
        var implicitCache = new Dictionary<string, Symbol[]>();

        foreach (var synFile in synFiles)
        {
            var fileUses = allFileUses.Files[synFile.Key];
            foreach (var synFunc in synFile.Value.Functions)
            {
                // Get current function
                if (!syntaxToSymbol.TryGetValue(synFunc, out var currentFunction))
                    continue; // Syntax error
                Debug.Assert(currentFunction.IsFun);
                GenFunction(synFile.Value, synFunc, table, fileUses, currentFunction, assembly, interfaces, implicitCache);
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
        Assembly assembly,
        Interfaces interfaces,
        Dictionary<string, Symbol[]> implicitCache)
    {
        var path = synFile.Lexer.Path;
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
            else if (name == "return" || name == "yield")
                GenReturnStatement(s);
            else
            {
                // Generate top level expression, e.g. f(x), etc.
                var rval = GenExpr(s);
                EvalCall(rval, EmptyCallParams, EvalFlags.AssignmentTarget);
                if (rval != null && rval.Symbol != null && rval.Symbol.IsFun)
                {
                    // TBD: Mark an error for non-mut function calls
                    if (rval.Symbol.IsGetter || rval.Symbol.IsSetter)
                        Reject(rval.Symbol.Token, "Top level statement cannot be a getter or setter");
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
            // TBD: This generates a crummy error message "No function '_for' in the type..."
            return GenFindAndCall(token, DerefRef(inType), "_for");
        }

        // Find and call a function (or getter) taking no arguments,
        // return the type of the function that was called
        Symbol? GenFindAndCall(Token token, Symbol inType, string name)
        {
            var call = new Rval(token, name) { InType = inType };
            EvalCall(call, EmptyCallParams, EvalFlags.DontAddCallInfo, $"'{name}' in the type '{inType}'");
            return call?.Type;
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

            IsFunParamConvertableReject(ex.Token, rval.Type, funReturnType);
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
            else if (name == "@" || name == "let" || name == "var")
                return GenNewVarsOperator(ex);
            else if (name == "=")
                return GenAssign(ex);
            else if (s_operators.Contains(name))
                return GenOperator(ex);
            else if (name == "ife")
                return GenTernary(ex);
            else if (name == "&*")
                return GenRefOrAddressOf(ex);
            else if (name == "sizeof")
                return new Rval(token) { Type = typeInt };
            else if (name == "true" || name == "false")
                return new Rval(token) { Type = typeBool };
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
                ex[0].Token.Type = TokenType.TypeName;
                var customType = ex[0].Token;
                if (customType == "Int")
                    numberType = typeInt;
                else if (customType == "U64")
                    numberType = typeU64;
                else if (customType == "I32")
                    numberType = typeI32;
                else if (customType == "U32")
                    numberType = typeU32;
                else if (customType == "Float")
                    numberType = typeFloat;
                else if (customType == "F32")
                    numberType = typeF32;
                else if (customType == "Byte")
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


        Rval? GenIdentifier(SyntaxExpr ex)
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

            var left = GenExpr(ex[0]);
            EvalCall(left, EmptyCallParams, EvalFlags.AllowStatic);

            var leftType = left?.Type;
            if (left == null || leftType == null)
                return null;

            // Automatically dereference pointers, etc
            // TBD: Belongs in function resolution or (find in type)?
            leftType = DerefRef(leftType);
            leftType = DerefPointers(leftType);

            // Find in tuple
            var token = ex[1].Token;
            if (leftType.IsTuple && leftType.IsSpecialized)
            {
                if (leftType.TupleSymbols.Length == 0)
                {
                    Reject(token, $"The type '{leftType}' is an anonymous type without field names, so cannot be resolved with '.'");
                    return null;
                }
                var i = Array.FindIndex(leftType.TupleSymbols, f => f.SimpleName == token.Name);
                if (i < 0)
                {
                    Reject(token, $"'{token}' is an undefined symbol in the named tuple '{leftType}'");
                    return null;
                }
                if (i < leftType.TupleSymbols.Length)
                    token.AddInfo(leftType.TupleSymbols[i]);
                assembly.AddOpNoImp(token, $"tuple {token}");
                return new Rval(token) { 
                    Type = leftType.TypeArgs[i], 
                    Symbol = leftType.TupleSymbols[i] };
            }

            return new Rval(token) {
                InType = leftType,
                IsStatic = left.Symbol?.IsAnyTypeOrModule ?? false,
            };
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
            ex.Token.Type = TokenType.NewVarSymbol;
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
            return new Rval(newSymbols[0].Token)
            {
                Symbol = newSymbols[0],
            };
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

            var isLocal = rval.Symbol != null && rval.Symbol.IsLocal;
            if (rval.Type.Parent!.FullName != SymTypes.Ref && !isLocal)
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


        Rval? GenAssign(SyntaxExpr ex)
        {
            if (ex.Count != 2)
                return null;  // Syntax error

            var left = GenExpr(ex[0]);
            EvalCall(left, EmptyCallParams, EvalFlags.AssignmentTarget);

            var right = GenExpr(ex[1]);
            EvalCall(right, EmptyCallParams);

            var rightType = right?.Type;
            if (left == null || right == null || rightType == null || left.Symbol == null)
                return null;

            // Assign type to local variable (if type not already assigned)
            var assignedSymbol = left.Symbol;
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
            if (s_intTypeNames.Contains(DerefRef(leftType).FullName)
                    && right.IsUntypedConst 
                    && s_intTypeNames.Contains(DerefRef(rightType).FullName))
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
                
                if (!IsFunParamConvertableReject(ex.Token, rightType, args[1]))
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

            if (!IsFunParamConvertableReject(ex.Token, rightType, leftType))
                return null;

            // Debug, TBD: Remove or get better compiler feedback system
            ex.Token.AddInfo($"({leftType.FullName}) = ({rightType.FullName})");

            assembly.AddOp(Op.Setr, ex.Token, 0);

            return null;
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
                        && s_intTypeNames.Contains(DerefRef(left.Type!).FullName) && s_intTypeNames.Contains(right.Type!.FullName))
                    right.Type = left.Type;
                if (!right.IsUntypedConst && left.IsUntypedConst 
                        && s_intTypeNames.Contains(DerefRef(right.Type!).FullName) && s_intTypeNames.Contains(left.Type!.FullName))
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
            EvalCall(call, args, EvalFlags.Invoked,  $" '{operatorName}' (operator '{token}')");
            var funType = call?.Type;
            if (funType == null)
                return null;

            if (s_cmpOperators.Contains(token))
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

            EvalCall(call, args, EvalFlags.Invoked,  $"'{call.Token}'");
            if (call == null || call.Type == null)
                return null;

            return new Rval(call.Token) { Type = call.Type };
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

        // Set call.Type to return type of symbol, or null if symbol is not
        // found, is unresolved, or ambiguous.  Set call.SymbolType to the
        // the symbol that generated the type.
        // `call.AssignmentTarget` allows the symbol to be an unresolved
        // local and is also used to resolve ambiguity between getter/setter.
        // `call.Invoked` is true when called as a function, and is used
        // to determine if a lambda is invoked or just passed as a variable.
        // Marks an error when there is no match.
        // Returns the symbol that generated the type (or null if
        // there wasn't one)
        void EvalCall(
            Rval? call,
            List<Rval>? args,
            EvalFlags flags = EvalFlags.None,
            string rejectName = "")
        {
            if (call == null)
                return;

            // Done if we already have return type
            if (call.Type != null)
                return;

            // Find symbol
            var token = call.Token;
            List<Symbol> candidates;
            if (call.InType == null)
                candidates = FindGlobalIdentifier(token, call.Name);
            else
                candidates = FindInType(call.Name, call.InType);

            // Exactly 1 primary or multiple functions
            if (RejectUndefinedOrAmbiguousPrimary(call, candidates))
                return;


            var local = candidates.Count == 1 ? candidates[0] : null;
            if (local != null && (local.IsLocal || local.IsFunParam))
            {
                if (local.IsLocal && local.Type == null && !flags.HasFlag(EvalFlags.AssignmentTarget))
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
                    || !flags.HasFlag(EvalFlags.Invoked))
                {
                    call.Type = local.Type;
                    call.Symbol = local;
                    return;
                }
            }

            // Handle fields, types, modules, and setter
            if (candidates.Count == 1
                && (candidates[0].IsField || candidates[0].IsAnyTypeOrModule))
            {
                var candidate = candidates[0];
                if (call.IsStatic != candidate.IsStatic)
                {
                    Reject(call.Token, "Static mismatch");
                    return;
                }

                if (candidate.IsAnyType && flags.HasFlag(EvalFlags.Invoked))
                {
                    call.Symbol = FindCompatibleConstructor(call, candidate, args);
                    call.Type = call.Symbol?.FunReturnType;
                    return;
                }

                var inType = call.InType;
                if (candidate.IsField && candidate.Type != null && inType != null && inType.TypeArgs.Length != 0)
                    candidate = table.CreateSpecializedType(candidate, inType.TypeArgs, null);
                token.AddInfo(candidate);

                // Static type or module, e.g. int.MinValue, or Log.info, but not int().MinValue
                if (candidate.IsAnyTypeOrModule)
                {
                    token.Type = TokenType.TypeName;
                    if (flags.HasFlag(EvalFlags.AllowStatic) || candidate.FullName == SymTypes.Nil)
                    {
                        // Substitute generic parameter
                        if (candidate.IsTypeParam)
                            call.Type = table.GetGenericParam(candidate.GenericParamNum());
                        else
                            call.Type = candidate;

                        call.Symbol = candidate;
                        return;
                    }
                    else
                    {
                        Reject(token, $"'{token}' is a {candidate.KindName}, which is not valid when used like this");
                        call.Symbol = candidate;
                        return;
                    }
                }
                // Field
                if (candidate.IsField)
                {
                    if (candidate.Type == null)
                    {
                        Reject(token, $"'{token}' has an unresolved type");
                        call.Symbol = candidate;
                        return;
                    }
                    Debug.Assert(candidate.Type.Parent!.FullName != SymTypes.Ref);
                    call.Type = candidate.Type;

                    // A field is the same thing as a getter returning a mutable ref
                    MakeIntoRef(call);
                    assembly.AddOpNoImp(candidate.Token, $"field {candidate.FullName}");
                    call.Symbol = candidate;
                    return;
                }
            }

            // Find the function to call
            call.Symbol = FindCompatibleFunction(call, candidates, args, flags.HasFlag(EvalFlags.AssignmentTarget), rejectName);
            call.Type = call.Symbol?.FunReturnType;

            if (call.Symbol != null)
            {
                if (!flags.HasFlag(EvalFlags.DontAddCallInfo))
                    call.Token.AddInfo(call.Symbol);
                assembly.AddOpCall(call.Token, call.Symbol);
            }
        }

        // Find a compatible constructor function.
        Symbol? FindCompatibleConstructor(Rval call, Symbol newType, List<Rval>? args)
        {
            call.Token.Type = TokenType.TypeName;
            if (newType.IsSpecialized)
                throw new Exception("Compiler error: FindCompatibleConstructor, unexpected specialized type");

            if (newType.IsTypeParam)
            {
                // A type parameter returns a function returning itself
                if (call.TypeArgs.Length != 0)
                {
                    Reject(call.Token, $"Expecting 0 type parameters, but got '{call.TypeArgs.Length}'");
                    return null;
                }
                if (args != null && args.Count != 0)
                {
                    Reject(call.Token, $"New generic type with arguments not supported yet");
                    return null;
                }
                Debug.Assert(call.InType == null);
                return table.GetGenericParamConstructor(newType.GenericParamNum());
            }

            // Search for `new` function
            Debug.Assert(call.InType == null);
            call.InType = newType;
            call.Type = null;
            call.Name = "new";
            EvalCall(call, args, EvalFlags.None, $"'new' (constructor for '{newType}')");
            return call.Symbol;
        }

        // Given the call and its parameters, find the best matching function.
        // If there is an error, mark it and give feedback on possible matches.
        // When args is null, it means that there was an error evaluating the
        // parameter types, so just try to give good feedback.
        Symbol? FindCompatibleFunction(
            Rval call,
            List<Symbol> candidates,
            List<Rval> ?args,
            bool assignmentTarget,
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

            // Generate list of matching symbols, update the candidates to their specialized form
            var compatibleErrors = new Dictionary<CallCompatible, bool>();
            var matchingFuns = new List<Symbol>();
            foreach (var candidate in candidates)
            {
                var callMatch = IsCallCompatible(call, candidate, args);

                if (callMatch.Compatibility == CallCompatible.Compatible)
                {
                    if (candidate.IsFun)
                    {
                        if (candidate.IsSpecialized)
                            matchingFuns.Add(candidate); // constraint
                        else
                            matchingFuns.Add(table.CreateSpecializedType(candidate, callMatch.TypeArgs));
                    }
                    else
                        matchingFuns.Add(candidate); // Lambda
                }
                else
                {
                    compatibleErrors[callMatch.Compatibility] = true;
                }
            }

            if (matchingFuns.Count == 0)
            {
                // Incorrect type
                RejectSymbols(call.Token, candidates, 
                    $"No function {rejectName} taking '{Rval.ParamTypes(args, call.TypeArgs)}'" 
                    + $" in scope: {PrintCompatibleErrors(compatibleErrors.Keys)}");

                // TBD: We want better type inferrence here, but not like this
                //      1. The list of functions is too big now, it includes
                //         everything, even methods not for this type
                //      2. Errors
                // If there was just 1 symbol, assume that is what was called.
                // This gives better type inference than making it unresolved.
                //if (candidates.Count != 1)
                //    return null;
                //matchingFuns = new List<Symbol>() { candidates[0] };
                return null;
            }

            // In case of tie between getter/setter, remove based on AssignmentTarget
            if (matchingFuns.Count == 2
                    && (matchingFuns[0].IsGetter || matchingFuns[0].IsSetter)
                    && (matchingFuns[1].IsGetter || matchingFuns[1].IsSetter)
                    && (matchingFuns[0].IsGetter == matchingFuns[1].IsSetter))
                matchingFuns = matchingFuns.Where(s => assignmentTarget == s.IsSetter).ToList();

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
            if (func.IsFun)
            {
                // Return the function
                return func;
            }
            else
            {
                if (func.Type == null)
                    Reject(call.Token, "Compiler error, invalid lambda type");
                return func.Type;
            }

        }       


        // Checks if the function call is compatible, return the inferred type arguments if compatible
        CallMatch IsCallCompatible(Rval call, Symbol func, List<Rval> args)
        {
            if (!func.IsFun)
                return IsLambdaCompatible(call.Name, func, args);

            if (func.IsMethod && call.InType != null)
            {
                if (call.IsStatic && !func.IsStatic)
                    return new CallMatch(CallCompatible.StaticCallToNonStaticMethod, []);
                if (!call.IsStatic && func.IsStatic)
                    return new CallMatch(CallCompatible.NonStaticCallToStaticMethod, []);
            }

            var typeArgsExpectedCount = func.GenericParamCount();
            Symbol[] typeArgs;
            if (func.TypeArgs.Length != 0)
            {
                // Type args from constraint
                typeArgs = func.TypeArgs;
                if (call.TypeArgs.Length != 0)
                    return new CallMatch(CallCompatible.TypeArgsSuppliedByConstraint, []);
            }
            else if (call.TypeArgs.Length != 0)
                typeArgs = call.TypeArgs;   // Type args supplied by user
            else
                typeArgs = table.CreateUnresolvedArray(typeArgsExpectedCount); // Infer type args

            // Verify number of type arguments
            if (typeArgs.Length != typeArgsExpectedCount)
            {
                if (typeArgs.Length == 0 && typeArgsExpectedCount != 0)
                    return new CallMatch(CallCompatible.ExpectingSomeTypeArgs, []);
                if (typeArgs.Length != 0 && typeArgsExpectedCount == 0)
                    return new CallMatch(CallCompatible.ExpectingNoTypeArgs, []);
                else
                    return new CallMatch(CallCompatible.WrongNumberOfTypeArgs, []);
            }

            func = func.Concrete;
            var funParams = new Span<Symbol>(func.FunParamTypes);
            if (func.IsMethod && func.IsStatic)
                funParams = funParams.Slice(1);
           
            var match = AreFunParamsCompatible(call.Name, func, args, funParams, typeArgs);
            if (match.Compatibility != CallCompatible.Compatible)
                return match;

            // TBD: This will be moved to caller so we can infer lambda returns
            if (match.TypeArgs.Any(s => s.IsUnresolved))
                return new CallMatch(CallCompatible.TypeArgsNotInferrable, []);

            return match;
        }

        CallMatch IsLambdaCompatible(string callName, Symbol variable, List<Rval> args)
        {
            if (!variable.IsFunParam && !variable.IsLocal && !variable.IsField)
                return new CallMatch(CallCompatible.NotAFunction, []);

            var lambda = variable.Type;
            if (lambda == null
                    || !lambda.IsLambda
                    || lambda.TypeArgs.Length != 1)
                return new CallMatch(CallCompatible.NotAFunction, []);

            // Get lambda parameters
            var funParams = lambda.TypeArgs[0];
            if (funParams.TypeArgs.Length != 2)
                return new CallMatch(CallCompatible.NotAFunction, []);

            return AreFunParamsCompatible(callName, variable, args, funParams.TypeArgs[0].TypeArgs, []);
        }


        // Match parameters to arguments and infer unresolved type args.
        // Previously resolved type args are verified and stay un-changed.
        // On success, some type args may still be un-resolved because
        // lambdas have not beeen compiled yet, and lambda return values
        // have not been accounted for.
        // typeArgs is not modified
        // TBD: Review similarity to AreInterfaceParamsCompatible
        CallMatch AreFunParamsCompatible(
            string callName, 
            Symbol func, 
            List<Rval> args, 
            Span<Symbol> funParams, 
            Symbol[] typeArgs)
        {
            // Match up the arguments
            for (var i = 0; i < Math.Max(funParams.Length, args.Count); i++)
            {
                // Ignore setter parameter type since it is checked by the assignment
                if (i == 1 && func.IsSetter && funParams.Length == 2)
                    return new CallMatch(CallCompatible.Compatible, typeArgs);

                // TBD: Default parameters, etc.
                if (i >= args.Count || i >= funParams.Length)
                    return new CallMatch(CallCompatible.WrongNumberOfParameters, []);

                var arg = args[i];
                var param = funParams[i];

                // Special cases for first parameter
                if (i == 0)
                {
                    // Receiver for generic interface always matches
                    // since it came from the constraint
                    // TBD: This needs to work so other parameters
                    //      can accept generic interface arguments
                    if (func.Concrete.Parent!.IsInterface
                            && !func.IsStatic
                            && arg.Type!.IsGenericArg)
                        continue;

                    // First parameter of the constructor is the type,
                    // which was not specialized because it bypassed
                    // eval in FindCompatibleConstructor
                    // TBD: Consider making `new` a regular global function
                    //      in the form of `Type$new`
                    if (callName == "new" && func.SimpleName == "new")
                    {
                        if (arg.Type!.Concrete.FullName == param.Concrete.FullName)
                            continue;
                        else
                            return new CallMatch(CallCompatible.IncompatibleParameterTypes, []);
                    }
                }

                var paramMatch = IsFunParamConvertable(arg.Type!, param, typeArgs);
                var compat = paramMatch.Compatibility;
                if (compat != CallCompatible.Compatible)
                    return new CallMatch(compat, []);

                // Accept inferred type arguments
                typeArgs = paramMatch.TypeArgs;
            }

            return new CallMatch(CallCompatible.Compatible, typeArgs);
        }

        bool IsFunParamConvertableReject(Token t, Symbol argType, Symbol paramType)
        {
            var compat = IsFunParamConvertable(argType, paramType, Array.Empty<Symbol>());
            if (compat.Compatibility == CallCompatible.Compatible)
                return true;
            Reject(t, $"Cannot convert type '{argType}' to '{paramType}'. {PrintCompatibleError(compat.Compatibility)}");
            return false;
        }

        // Can the given argument be converted to the parameter type?
        // If so, update typeArgs to be inferred.
        // NOTE: Match all lambda's since we haven't compiled them yet
        // typeArgs is not modified
        ParamMatch IsFunParamConvertable(Symbol argType, Symbol paramType, Symbol[] typeArgs)
        {
            argType = DerefRef(argType);
            paramType = DerefRef(paramType);

            // The generic concrete lambda type matches all lambdas
            // because its type is set later when it is compiled.
            // TBD: Need to resolve here by walking down tree
            if (argType.IsLambda && !argType.IsSpecialized && paramType.IsLambda)
                return new(CallCompatible.Compatible, typeArgs);


            var (match, inferred) = InferTypesMatch(argType, paramType, typeArgs);
            if (match)
                return new(CallCompatible.Compatible, inferred);

            // Implicit conversion from nil to *T or from *T to *void
            if (paramType.Parent!.FullName == SymTypes.RawPointer)
            {
                // TBD: Need return type inference to make implicit conversion work here
                if (argType.FullName == SymTypes.Nil)
                    return new(CallCompatible.Compatible, typeArgs, null, null);
            }


            // TBD: Interface to interface conversion not supported yet
            if (argType.IsInterface && paramType.IsInterface)
                return new(CallCompatible.InterfaceToInterfaceConversionNotSupported, typeArgs);

            // ----------------------------------------------------------
            // Implicit conversion from concrete type to interface type
            // ----------------------------------------------------------
            InterfaceInfo? ifaceConversion = null;
            if (!argType.IsInterface && paramType.IsInterface)
            {
                ifaceConversion = interfaces.ConvertToInterfaceInfo(table, argType, paramType, typeArgs);
                if (ifaceConversion.Compatibility == CallCompatible.Compatible)
                    return new(CallCompatible.Compatible, ifaceConversion.TypeArgs, null, ifaceConversion);
            }

            // ------------------------------------
            // Implcit Conversion to exact match
            // ------------------------------------
            
            // Retrieve implicit conversions from argType, parmType, and current function modules
            var conversions = new List<Symbol>();
            PushImplicits(argType, conversions);
            if (paramType.ParentModule.FullName != argType.ParentModule.FullName)
                PushImplicits(paramType, conversions);
            if (function.ParentModule.FullName != argType.ParentModule.FullName && function.ParentModule.FullName != paramType.ParentModule.FullName)
                PushImplicits(function, conversions);

            var callableConversions = new List<(Symbol function, Symbol[] inferred)>();
            var exactConversions = new List<Symbol[]>();
            foreach (var conversion in conversions)
            {
                var parameters = conversion.FunParamTypes;
                var returns = conversion.FunReturnTypes;
                if (parameters.Length != 1 || parameters[0].IsInterface || returns.Length != 1 || returns[0].IsInterface)
                    continue; // These should fail during validation
                
                // Can we call it with the given parameters?
                var (matchConversion, inferredConversionTypes) = InferTypesMatch(argType, parameters[0], 
                    table.CreateUnresolvedArray(conversion.GenericParamCount()));

                if (!matchConversion)
                    continue;  // Not callable
                
                // Specialize the function to get return type 
                Debug.Assert( (argType.IsSpecialized || argType.TypeArgs.Length == 0) && !conversion.IsSpecialized);
                var specializedConversion = table.CreateSpecializedType(conversion, inferredConversionTypes);
                callableConversions.Add((specializedConversion, inferredConversionTypes));
                
                // Find matching return type
                var (match2, inferredReturns) = InferTypesMatch(specializedConversion.FunReturnType, paramType, typeArgs);
                if (match2)
                    exactConversions.Add(inferredReturns);
            }

            // No conversions at all
            if (callableConversions.Count == 0)
            {
                return ifaceConversion == null 
                    ? new(CallCompatible.IncompatibleParameterTypes, typeArgs) : new(ifaceConversion.Compatibility, typeArgs);
            }

            // Exactly one perfect match
            if (exactConversions.Count == 1)
            {
                return new(CallCompatible.Compatible, exactConversions[0]);
            }

            // Ambiguous conversion
            if (exactConversions.Count > 1)
            {
                // TBD: Ruling out here because of ambiguous could be problematic because it can hide problems above.
                //      Need to rull out ambiguous at function candidate level unless another parameter fails completely.
                //      ACCEPTABLE, BUT AMBIGUOUS - SHOULD FAIL AT CANDIDATE LEVEL
                return new(CallCompatible.ImplicitConversionAmbiguous, exactConversions[0]);
            }

            // ------------------------------------
            // Implcit Conversion to interface?
            // ------------------------------------
            if (!paramType.IsInterface)
                return ifaceConversion == null
                    ? new(CallCompatible.IncompatibleParameterTypes, typeArgs) : new(ifaceConversion.Compatibility, typeArgs);

            var ifaceConversions = new List<Symbol[]>();
            foreach (var conversion in callableConversions)
            {

                var ifaceConversion2 = interfaces.ConvertToInterfaceInfo(table, conversion.function.FunReturnType, paramType, typeArgs);
                if (ifaceConversion2.Compatibility == CallCompatible.Compatible)
                    ifaceConversions.Add(ifaceConversion2.TypeArgs);                
            }

            if (ifaceConversions.Count == 1)
                return new(CallCompatible.Compatible, ifaceConversions[0]);

            if (ifaceConversions.Count > 1)
            {
                // TBD: ACCEPTABLE, BUT AMBIGUOUS - SHOULD FAIL AT CANDIDATE LEVEL
                return new(CallCompatible.ImplicitConversionToInterfaceAmbiguous, typeArgs);
            }

            return ifaceConversion == null
                ? new(CallCompatible.IncompatibleParameterTypes, typeArgs) : new(ifaceConversion.Compatibility, typeArgs);
        }

        // Retrieve implicit functions from the symbol's module (cache them in implicitCache for speed)
        void PushImplicits(Symbol symbol, List<Symbol> implicits)
        {
            if (!implicitCache.TryGetValue(symbol.ParentModule.FullName, out var implicitsList))
            {
                implicitsList = symbol.ParentModule.Children.Where(s => s.IsImplicit && s.IsFun && s.IsMethod).ToArray();
                implicitCache[symbol.ParentModule.FullName] = implicitsList;
            }
            foreach (var impl in implicitsList)
                implicits.Add(impl);
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

        List<Symbol> FindGlobalIdentifier(Token token, string name)
        {
            // Find local
            var local = FindLocal(token);
            if (local.sym != null)
            {
                token.AddInfo(local.sym);
                assembly.AddOpLdlr(local.sym.Token, local.index);
                return new List<Symbol>() { local.sym };
            }

            // Find primary in this module
            if (function.ParentModule.TryGetPrimary(name, out var primary) && primary != null)
                return new List<Symbol>() { primary };

            // Find function in module, use, or constraints
            return FindGlobal(name);
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
            // Return a primary (non-function) symbol, field
            // type, interface, etc., or null if not found
            if (inType.TryGetPrimary(name, out var s1) && s1 != null)
                return new List<Symbol>() { s1 };
            if (inType.IsSpecialized && inType.Parent!.TryGetPrimary(name, out var s2) && s2 != null)
                return new List<Symbol>() { s2 };

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

            var local = new Symbol(SymKind.Local, null, path, token);
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
                    Reject(call.Token, $"'{call.Name} is an undefined symbol in the local, module, 'use', or constraint scopes");
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

        static string PrintCompatibleErrors(IEnumerable<CallCompatible> c)
        {
            return string.Join(",", c.Select(s => PrintCompatibleError(s)));
        }

        static string PrintCompatibleError(CallCompatible c)
        {
            return c switch
            {
                CallCompatible.Compatible => "Compatible",
                CallCompatible.NotAFunction => "Not a function",
                CallCompatible.StaticCallToNonStaticMethod => "Static call to non static method (receiver must be a value, not a type)",
                CallCompatible.NonStaticCallToStaticMethod => "Non-static to static function call (receiver must be a type, not a value)",
                CallCompatible.ExpectingSomeTypeArgs => "Expecting some type arguments, but none supplied",
                CallCompatible.ExpectingNoTypeArgs => "Expecting no type arguments, but some were supplied",
                CallCompatible.WrongNumberOfTypeArgs => "Wrong number of type parameters",
                CallCompatible.WrongNumberOfParameters => "Wrong number of parameters",
                CallCompatible.IncompatibleParameterTypes => "Incompatible parameter types",
                CallCompatible.TypeArgsSuppliedByConstraint => "Non-generic function cannot take type arguments",
                CallCompatible.InterfaceToInterfaceConversionNotSupported => "Interface to interface conversion not supported yet",
                CallCompatible.InterfaceNotImplementedByType => "The type doesn't implement the interface",
                CallCompatible.InterfaceGenerating => "The interface was used while being generated",
                CallCompatible.TypeArgsNotInferrable => "Type arguments cannot be inferred",
                CallCompatible.TypeArgsNotInferrableFromInterfaceParameter => "Type arguments on interface cannot be inferred",
                _ => c.ToString()
            };
        }

    }

}
