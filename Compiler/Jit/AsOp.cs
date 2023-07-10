using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zurfur.Jit
{
    public enum Op
    {
        // Meta
        Nop,    // No op
        Comment,// Operand is index into comments
        NoImp,  // Not implemented, operand is index into comments

        // Flow control
        Begin,  // Begin scope
        End,    // End scope
        Br,     // Branch
        Brif,   // Branch if, operand is target level (negative is up)
        Brnif,  // Branch not if, operand is targer level (negative is up) 
        Ret,    // Return

        // Conditions
        Gt,     // ">" i32->bool
        Ge,     // ">=" i32->bool
        Lt,     // "<" i32->bool
        Le,     // "<=" i32->bool

        // Constants
        I64,    // Operand is I64
        F64,    // Operand is IEEE 754 encoded as int64
        String, // Operand is index into string table

        // Code
        Loc,    // Create a local in this scope, operand is an index into types
        Call,   // Operand is index into symbols
        Ldlr,   // Load local ref, operand is an index into locals
        Setr,    // Store into reference (value, ref)
        Getr,   // Get from reference
    }

    public struct AsOp
    {
        public readonly Op Op;
        public long Oper { get; private set; }

        public AsOp(Op op, long operand)
        {
            Op = op;
            Oper = operand;
        }

        public int OperInt
            => (int)Oper;

        public override string ToString()
            => $"({Op},{Oper})";
    }
}
