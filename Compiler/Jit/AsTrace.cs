using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zurfur.Jit
{
    /// <summary>
    /// Trace a function, keeping track of the stack, locals, and branches.
    /// Generate error for invalid instructions or type system violation.
    /// </summary>
    public class AsTrace
    {
        public Assembly Assembly { get; }
        public SymbolTable Table { get; }

        // Index of next opcode to execute
        public int OpIndex { get; private set; }

        public List<Symbol> Locals = new();
        public List<Symbol> Stack = new();
        public List<Branch> BranchStack = new();

        int _beginCount;

        // For each begin opcode, give the end index
        List<int> _beginToEndIndices = new();

        
        public AsTrace(Assembly assembly, SymbolTable table) 
        {
            Assembly = assembly;
            Table = table;
        }

        /// <summary>
        /// Trace next instruction at OpIndex, keeping track of stack, locals,
        /// branches, etc..  Returns NULL if everything is ok, or a string with
        /// error message
        /// </summary>
        public string? Trace()
        {
            if (OpIndex < 0 || OpIndex >= Assembly.Code.Count)
                return $"Invalid instruction index {OpIndex}";

            var opCode = Assembly.Code[OpIndex++];
            switch (opCode.Op)
            {
                case Op.Nop: return null;
                case Op.Comment: return null;
                case Op.NoImp: return "Not implemented";
                case Op.BeginFun: return BeginFun();
                case Op.BeginScope: return BeginScope();
                case Op.EndScope: return EndScope();
                case Op.I64: return AddFundamentalType(SymTypeId.Int);
                case Op.Float: return AddFundamentalType(SymTypeId.Float);
                case Op.Str: return AddFundamentalType(SymTypeId.Str);
                case Op.Loc: return OpLoc();
                case Op.Call: return OpCall();
                case Op.Ge:
                case Op.Gt:
                case Op.Lt:
                case Op.Le:
                    return OpCmp();
                case Op.Br:
                    return OpBr();
                case Op.Brif:
                case Op.Brnif:
                    return OpBrif();
                case Op.Ldlr:
                    return OpLdlr();

                default: return $"Invalid opcode {opCode}";
            }

            /// <summary>
            /// Start tracing a function.
            /// </summary>
            string? BeginFun()
            {
                Stack.Clear();
                BranchStack.Clear();
                Locals.Clear();

                // Add parameter locals
                foreach (var localType in Assembly.Functions[opCode.OperandInt].FunParamTypes)
                    Locals.Add(localType);
 
                // Build the begin/end index list 
                _beginCount = 0;
                _beginToEndIndices.Clear();
                for (int i = OpIndex; i < Assembly.Code.Count; i++)
                {
                    var op = Assembly.Code[i].Op;
                    if (op == Op.BeginFun)
                        break;
                    if (op == Op.BeginScope)
                    {
                        BranchStack.Add(new() { StackIndex = _beginToEndIndices.Count });
                        _beginToEndIndices.Add(0);
                    }
                    else if (op == Op.EndScope && BranchStack.Count != 0)
                    {
                        _beginToEndIndices[BranchStack.Pop().StackIndex] = i;
                    }
                }
                BranchStack.Clear();
                return null;
            }


            string? BeginScope()
            {
                BranchStack.Add(new() {
                    BeginIndex = OpIndex-1, 
                    EndIndex = _beginToEndIndices[_beginCount++],
                    StackIndex = Stack.Count 
                });
                return null;
            }

            string ?EndScope()
            {
                if (BranchStack.Count == 0)
                    return "End scope has no matching begin scope";
                var branch = BranchStack.Pop();
                //TypeStack.RemoveRange(branch.StackIndex, BranchStack.Count - branch.StackIndex);
                return null;
            }


            string? AddFundamentalType(SymTypeId id)
            {
                if ((int)id >= Assembly.Types.Count)
                    return $"Missing fundamental type {id}";
                Stack.Add(Assembly.Types[(int)id]);
                return null;
            }

            string? OpLoc()
            {
                Locals.Add(Assembly.Types[opCode.OperandInt]);
                return null;
            }

            string ?OpCall()
            {
                var fun = Assembly.Functions[opCode.OperandInt];
                if (!fun.IsFun)
                    return "Invalid opcode: Not a function";

                // Verify function parameters
                string? error = null;
                var paramTypes = fun.FunParamTypes;
                for (var i = paramTypes.Length-1; i >= 0 && error == null; i--)
                    error = VerifyTop(paramTypes[i].FullName);

                // Push function returns
                foreach (var returnType in fun.FunReturnTypes)
                    Stack.Add(returnType);

                return error;
            }

            string ?VerifyTop(string typeName)
            {
                if (Stack.Count == 0)
                    return $"Expecting '{typeName}', stack has no patametets";
                var stackType = Stack.Pop().FullName;
                if (stackType != typeName)
                    return $"Expecting '{typeName}', stack has '{stackType}'";
                return null;
            }

            string ?OpCmp()
            {
                var error = VerifyTop(SymTypes.Int);
                if (Assembly.Types.Count > (int)SymTypeId.Int)
                    Stack.Add(Assembly.Types[(int)SymTypeId.Int]);
                return error;
            }


            string ?OpBr()
            {
                if (BranchTarget(opCode.OperandInt) < 0)
                    return "Illegal branch target";
                return null;
            }

            string ?OpBrif()
            {
                if (BranchTarget(opCode.OperandInt) < 0)
                    return "Illegal branch target";
                return null;
            }

            string ?OpLdlr()
            {
                var type = Locals[opCode.OperandInt];
                Stack.Add(Table.CreateRef(type));
                return null;
            }

        }

        /// <summary>
        /// Returns the branch target (if taken) opcode index, or -1 for error. 
        /// The operand is the level to jump (negative=up, positive=down).
        /// </summary>
        public int BranchTarget(int operand)
        {
            var up = operand < 0;
            operand = Math.Abs(operand);
            var index = BranchStack.Count - operand;
            if (index < 0 || index >= BranchStack.Count)
                return -1;
            return up ? BranchStack[index].BeginIndex : BranchStack[index].EndIndex;
        }
    }


    public struct Branch
    {
        public int BeginIndex;
        public int EndIndex;
        public int StackIndex;

        public override string ToString()
        {
            return $"({nameof(BeginIndex)}:{BeginIndex},{nameof(EndIndex)}:{EndIndex},{nameof(StackIndex)}:{StackIndex})";
        }
    }
}
