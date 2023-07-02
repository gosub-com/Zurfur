using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;

using Zurfur.Lex;

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

    public class AsFun
    {
        public readonly Assembly Assembly;
        public readonly string Name;
        public readonly Token Token;
        public readonly List<AsOp> Code = new();

        /// <summary>
        /// Length must be either 0 or same as `Code`.
        /// </summary>
        public readonly List<Token> CodeTokens = new();

        public AsFun(Assembly assembly, string name, Token token)
        {
            Assembly = assembly;
            Name = name;
            Token = token;
        }

        public void Add(Op op, Token token, long operand)
        {
            Code.Add(new AsOp(op, operand));
            CodeTokens.Add(token);
        }

        public void AddComment(Token token, string str)
        {
            Add(Op.Comment, token, Assembly.Comments.Count);
            Assembly.Comments.Add(str);
        }

        public void AddNoImp(Token token, string str)
        {
            Add(Op.NoImp, token, Assembly.Comments.Count);
            Assembly.Comments.Add(str);
        }

        public void AddStr(Token token, string str)
        {
            Add(Op.String, token, Assembly.Strings.AddOrFind(str));
        }

        public void AddCall(Token token, Symbol fun)
        {
            Debug.Assert(fun.IsFun || fun.IsLambda);
            Add(Op.Call, token, Assembly.Calls.AddOrFind(fun));
        }

        // New local variable within current scope, returns local index
        public void UseLocal(Token token, int localIndex, int codeIndex = -1)
        {
            var asOp = new AsOp(Op.Loc, localIndex);
            if (codeIndex < 0)
            {
                Code.Add(asOp);
                CodeTokens.Add(token);
            }
            else
            {
                Code.Insert(codeIndex, asOp);
                CodeTokens.Insert(codeIndex, token);
            }
        }

        // Load local reference
        public void Ldlr(Token token, int localIndex)
        {
            Code.Add(new AsOp(Op.Ldlr, localIndex));
            CodeTokens.Add(token);
        }

        public void Print(StringBuilder sb)
        {
            sb.Append("\r\n\r\n");
            sb.Append($"fun {Name}\r\n");

            int level = 0;
            int opIndex = -1;
            var useTypes = new List<Symbol>();
            foreach (var op in Code)
            {
                opIndex++;
                var tokenName = TokenName(opIndex);
                if (op.Op == Op.Begin)
                {
                    sb.Append($"{opIndex.ToString() + ":",-4}");
                    sb.Append($" {{      # begin {tokenName} (level={level})\r\n");
                    level++;
                    continue;
                }
                if (op.Op == Op.End)
                {
                    level--;
                    sb.Append($"{opIndex.ToString() + ":",-4}");
                    sb.Append($" }}      # end {tokenName} (level={level})\r\n");
                    continue;
                }
                if (op.Op == Op.Comment)
                {
                    var comment = Assembly.Comments[op.OperInt];
                    if (comment == "")
                    {
                        sb.Append("\r\n");
                    }
                    else
                    {
                        sb.Append("\r\n");
                        sb.Append($"# {comment}\r\n");
                        sb.Append("\r\n");
                    }
                    continue;
                }

                // Regular instruction - print op code
                sb.Append($"    {op.Op.ToString().ToLower(),-8}");

                // For instructions that have one, print the operand
                if (op.Op != Op.Setr && op.Op != Op.Getr
                        && op.Op != Op.Gt && op.Op != Op.Ge
                        && op.Op != Op.Lt && op.Op != Op.Le)
                    sb.Append(op.Oper);

                // print comment 
                if (op.Op == Op.String)
                    sb.Append($" # \"{JsonEncodedText.Encode(Assembly.Strings[op.OperInt])}\"");
                else if (op.Op == Op.Call)
                    sb.Append($" # {Assembly.Calls[op.OperInt].FullName}");
                else if (op.Op == Op.F64)
                    sb.Append($" # {BitConverter.Int64BitsToDouble(op.Oper)}");
                else if (op.Op == Op.Ldlr)
                    sb.Append($" # {tokenName} {useTypes[op.OperInt]}");
                else if (op.Op == Op.Loc)
                    sb.Append($" # [{useTypes.Count}] {tokenName} {Assembly.Types[op.OperInt]}");
                else if (op.Op == Op.NoImp)
                    sb.Append($" # {Assembly.Comments[op.OperInt]}");
                else if (op.Op == Op.Br || op.Op == Op.Brif || op.Op == Op.Brnif)
                    sb.Append($" # {FindBranchTarget(opIndex)}");
                sb.Append($"\r\n");

                if (op.Op == Op.Loc)
                    useTypes.Add(Assembly.Types[op.OperInt]);
            }
        }

        /// <summary>
        /// Returns either "'name'" of token, or "" if there aren't any tokens
        /// </summary>
        string TokenName(int i)
        {
            Debug.Assert(CodeTokens.Count == 0 || CodeTokens.Count == Code.Count);
            if (CodeTokens.Count == 0)
                return "";
            return "'" + CodeTokens[i].Name + "'";
        }

        // Opcode must be br, brif, or brnif.  Code must be well formed.
        int FindBranchTarget(int opIndex)
        {
            var op = Code[opIndex];
            Debug.Assert(op.Op == Op.Br || op.Op == Op.Brif || op.Op == Op.Brnif);
            if (op.Oper == 0)
                return opIndex;

            var level = op.OperInt;
            if (level < 0)
            {
                // Search level up
                while (--opIndex >= 0)
                {
                    if (Code[opIndex].Op == Op.End)
                        level--;
                    else if (Code[opIndex].Op == Op.Begin)
                    {
                        level++;
                        if (level == 0)
                            return opIndex;
                    }
                }
            }
            else
            {
                // Search level down
                while (++opIndex < Code.Count)
                {
                    if (Code[opIndex].Op == Op.Begin)
                        level++;
                    else if (Code[opIndex].Op == Op.End)
                    {
                        level--;
                        if (level == 0)
                            return opIndex;
                    }
                }
            }
            Debug.Assert(false);
            throw new Exception($"Illegal assembly branch target in '{Name}'");
        }
    }

}
