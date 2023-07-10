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
    /// </summary>
    public class AsTracer
    {
        public AsPackage Assembly { get; private set; }
        public AsFun Fun { get; private set; }
        public int OpIndex { get; private set; }

        public List<Symbol> Locals = new();
        public List<Symbol> Stack = new();
        public List<Branch> BranchStack = new();

        int _beginCount;

        // For each begin opcode, give the end index
        List<int> _beginToEndIndices = new();

        
        public AsTracer() 
        {
        }

        /// <summary>
        /// Start tracing a function
        /// </summary>
        public void BeginFun(AsFun fun)
        {
            Fun = fun;
            Assembly = fun.Assembly;

            OpIndex = 0;
            Stack.Clear();
            BranchStack.Clear();
            Locals.Clear();

            // Build the begin/end index list 
            _beginCount = 0;
            _beginToEndIndices.Clear();
            for (int i = 0;  i < fun.Code.Count; i++)
            {
                var op = fun.Code[i].Op;
                if (op == Op.Begin)
                {
                    BranchStack.Add(new() { StackIndex = _beginToEndIndices.Count });
                    _beginToEndIndices.Add(0);
                }
                else if (op == Op.End && BranchStack.Count != 0)
                {
                    _beginToEndIndices[BranchStack.Pop().StackIndex] = i;
                }
            }
            BranchStack.Clear();
        }

        /// <summary>
        /// Call after `AtEnd` is true, check result for errors.
        /// </summary>
        /// <returns></returns>
        public string ?EndFun()
        {
            if (OpIndex != Fun.Code.Count)
                return "Not at end of function";
            if (BranchStack.Count != 0)
                return "Missing scope end";
            return null;
        }


        public bool AtEnd => OpIndex >= Fun.Code.Count;

        /// <summary>
        /// Execute instruction, keeping track of stack, locals, branches, etc.
        /// Returns NULL if everything is ok, or a string with error message
        /// </summary>
        public string? Trace()
        {
            var opCode = Fun.Code[OpIndex++];
            switch (opCode.Op)
            {
                case Op.Nop: return null;
                case Op.Comment: return null;
                case Op.NoImp: return "Not implemented";
                case Op.Begin: return Begin();
                case Op.End: return End();
                case Op.Loc: return Loc();
                default: return "Invalid opcode";
            }

            string ?Begin()
            {
                BranchStack.Add(new() {
                    BeginIndex = OpIndex-1, 
                    EndIndex = _beginToEndIndices[_beginCount++],
                    StackIndex = Stack.Count 
                });
                return null;
            }

            string ?End()
            {
                if (BranchStack.Count == 0)
                    return "End scope has no matching scope";
                var branch = BranchStack.Pop();
                //TypeStack.RemoveRange(branch.StackIndex, BranchStack.Count - branch.StackIndex);
                return null;
            }

            string ?Loc()
            {
                Locals.Add(Assembly.Types[opCode.OperInt]);
                return null;
            }

        }

        /// <summary>
        /// Returns the branch target (if taken) opcode index, or -1 for error.  
        /// </summary>
        public int BranchTarget()
        {
            var opCode = Fun.Code[OpIndex];
            var op = opCode.Op;
            if (op != Op.Br && op != Op.Brif && op != Op.Brnif)
                return -1;
            var oper = opCode.OperInt;
            var up = oper < 0;
            oper = Math.Abs(oper);
            var index = BranchStack.Count - oper;
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
