using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;

using Zurfur.Lex;

namespace Zurfur.Jit
{
    /// <summary>
    /// Assembly function
    /// </summary>
    public class AsFun
    {
        public readonly AsPackage Assembly;
        public readonly string Name;
        public readonly List<AsOp> Code = new();
        public readonly List<(int opCodeIndex, string errorMessage)> Errors = new();

        // Length must be either 0 or same as `Code`.
        public readonly List<Token> DebugTokens = new();
        public readonly List<int> DebugLineNumbers = new();

        public AsFun(AsPackage assembly, string name)
        {
            Assembly = assembly;
            Name = name;
        }

        public override string ToString()
        {
            return Name;
        }

        public void Add(Op op, Token token, long operand)
        {
            Code.Add(new AsOp(op, operand));
            DebugTokens.Add(token);
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
                DebugTokens.Add(token);
            }
            else
            {
                Code.Insert(codeIndex, asOp);
                DebugTokens.Insert(codeIndex, token);
            }
        }

        // Load local reference
        public void Ldlr(Token token, int localIndex)
        {
            Code.Add(new AsOp(Op.Ldlr, localIndex));
            DebugTokens.Add(token);
        }

        public void Print(List<string> sb, AsTracer tracer)
        {
            tracer.BeginFun(this);


            DebugLineNumbers.Clear();
            sb.Add("");
            sb.Add($"fun {Name}");

            int opIndex = -1;
            foreach (var op in Code)
            {
                DebugLineNumbers.Add(sb.Count);
                opIndex++;
                var tokenName = TokenName(opIndex);
                if (op.Op == Op.Begin)
                {
                    sb.Add($"{opIndex.ToString() + ":",-4} {{      # begin {tokenName}");
                    tracer.Trace();
                    continue;
                }
                if (op.Op == Op.End)
                {
                    sb.Add($"{opIndex.ToString() + ":",-4} }}      # end {tokenName}");
                    tracer.Trace();
                    continue;
                }
                if (op.Op == Op.Comment)
                {
                    var comment = Assembly.Comments[op.OperInt];
                    if (comment == "")
                    {
                        sb.Add("");
                    }
                    else
                    {
                        sb.Add("");
                        sb.Add($"# {comment}");
                        sb.Add("");
                    }
                    tracer.Trace();
                    continue;
                }

                // Regular instruction - print op code
                var opCode = $"    {op.Op.ToString().ToLower(),-8}";

                // For instructions that have one, print the operand
                if (op.Op != Op.Setr && op.Op != Op.Getr
                        && op.Op != Op.Gt && op.Op != Op.Ge
                        && op.Op != Op.Lt && op.Op != Op.Le)
                    opCode += op.Oper;

                // print comment 
                if (op.Op == Op.String)
                    sb.Add($"{opCode} # \"{JsonEncodedText.Encode(Assembly.Strings[op.OperInt])}\"");
                else if (op.Op == Op.Call)
                    sb.Add($"{opCode} # {Assembly.Calls[op.OperInt].FullName}");
                else if (op.Op == Op.F64)
                    sb.Add($"{opCode} # {BitConverter.Int64BitsToDouble(op.Oper)}");
                else if (op.Op == Op.Ldlr)
                    sb.Add($"{opCode} # {tokenName} {tracer.Locals[op.OperInt]}");
                else if (op.Op == Op.Loc)
                    sb.Add($"{opCode} # [{tracer.Locals.Count}] {tokenName} {Assembly.Types[op.OperInt]}");
                else if (op.Op == Op.NoImp)
                    sb.Add($"{opCode} # {Assembly.Comments[op.OperInt]}");
                else if (op.Op == Op.Br || op.Op == Op.Brif || op.Op == Op.Brnif)
                    sb.Add($"{opCode} # {tracer.BranchTarget()}");

                tracer.Trace();
            }
        }

        /// <summary>
        /// Returns either "'name'" of token, or "" if there aren't any tokens
        /// </summary>
        string TokenName(int i)
        {
            Debug.Assert(DebugTokens.Count == 0 || DebugTokens.Count == Code.Count);
            if (DebugTokens.Count == 0)
                return "";
            return "'" + DebugTokens[i].Name + "'";
        }

    }

}
