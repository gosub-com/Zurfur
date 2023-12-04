using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Zurfur.Lex;

namespace Zurfur.Jit
{
    /// <summary>
    /// Assembly package
    /// </summary>
    public class Assembly
    {
        /// <summary>
        /// All assemblies must start with the fundamental types:
        ///     0: ()
        ///     1: Nil
        ///     2: Bool
        ///     3: Int
        ///     4: Float
        ///     5: Str
        /// </summary>
        public readonly ConsolidatedList<Symbol> Types = new();

        public readonly List<string> Comments = new();
        public readonly ConsolidatedList<Symbol> Functions = new();
        public readonly ConsolidatedList<string> Strings = new();
        public readonly ConsolidatedList<string> Translated = new();
        public readonly ConsolidatedList<InterfaceInfo> Interfaces = new();
        public readonly List<AsOp> Code = new();

        public readonly List<ErrorInfo> Errors = new();

        // Length must be either 0 or same as `Code`.
        public readonly List<Token> DebugTokens = new();


        public class ErrorInfo
        {
            public int OpIndex;
            public string ErrorMessage = "";
            public override string ToString()
            {
                return $"{OpIndex}: {ErrorMessage}";
            }
        }

        public Assembly() 
        {
            Strings.AddOrFind("");
            Translated.AddOrFind("");
        }

        public void AddOp(Op op, Token token, long operand)
        {
            Code.Add(new AsOp(op, operand));
            DebugTokens.Add(token);
        }

        public void AddOpComment(Token token, string str)
        {
            AddOp(Op.Comment, token, Comments.Count);
            Comments.Add(str);
        }

        public void AddOpNoImp(Token token, string str)
        {
            AddOp(Op.NoImp, token, Comments.Count);
            Comments.Add(str);
        }

        public void AddOpStr(Token token, string str)
        {
            AddOp(Op.Str, token, Strings.AddOrFind(str));
        }

        public void AddOpBeginFun(Symbol fun)
        {
            Debug.Assert(fun.IsFun);
            AddOp(Op.BeginFun, fun.Token, Functions.AddOrFind(fun));
        }

        public void AddOpCall(Token token, Symbol fun)
        {
            Debug.Assert(fun.IsFun || fun.IsLambda);
            AddOp(Op.Call, token, Functions.AddOrFind(fun));
        }

        // New local variable within current scope
        public void AddOpUseLocal(Token token, int localIndex, int codeIndex = -1)
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
        public void AddOpLdlr(Token token, int localIndex)
        {
            Code.Add(new AsOp(Op.Ldlr, localIndex));
            DebugTokens.Add(token);
        }

        public void AddOpInterface(Token token, InterfaceInfo iface)
        {
            AddOp(Op.LdIface, token, Interfaces.AddOrFind(iface));
        }

        /// <summary>
        /// Generate a printout of the assembly.  Optionally store a linue
        /// number for each opcode (i.e. lineNumbers.Count == Code.Count)
        /// </summary>
        public void Print(AsTrace tracer, List<string> sb, List<int>? lineNumbers = null)
        {
            // Print code
            sb.Add("code:");
            PrintCode(sb, tracer, lineNumbers);

            // Print calls
            sb.Add("");
            sb.Add("calls:");
            var index = 0;
            foreach (var call in Functions)
                sb.Add($"    {index++} {call}");

            // Print types
            sb.Add("");
            sb.Add("types:");
            index = 0;
            foreach (var type in Types)
                sb.Add($"    {index++} {type}");

            // Print interfaces
            index = 0;
            sb.Add("");
            sb.Add("interfaces:");
            foreach (var iface in Interfaces.OrderBy(s => s.Name))
            {
                sb.Add($"    {index++} {iface}");
                var j = 0;
                foreach (var func in iface.Functions)
                    sb.Add($"        {j++} {func}");
            }

            // Print strings
            sb.Add("");
            sb.Add("strings:");
            index = 0;
            foreach (var str in Strings)
                sb.Add($"    {index++} \"{JsonEncodedText.Encode(str)}\"");

            sb.Add("");
            sb.Add("translate:");
            sb.Add("    tbd...");
        }


        void PrintCode(List<string> sb, AsTrace tracer, List<int>? lineNumbers)
        {
            if (lineNumbers != null)
                lineNumbers.Clear();

            for (int opIndex = 0; opIndex < Code.Count; opIndex++)
            {
                if (lineNumbers != null)
                    lineNumbers.Add(sb.Count);

                var op = Code[opIndex];
                var tokenName = TokenName(opIndex);

                if (op.Op == Op.BeginFun)
                {
                    sb.Add($"fun {Functions[op.OperandInt].FullName}");
                    tracer.Trace();
                    continue;
                }
                if (op.Op == Op.BeginScope)
                {
                    sb.Add($"{opIndex.ToString() + ":",-4} {{      # begin {tokenName}");
                    tracer.Trace();
                    continue;
                }
                if (op.Op == Op.EndScope)
                {
                    sb.Add($"{opIndex.ToString() + ":",-4} }}      # end {tokenName}");
                    tracer.Trace();
                    continue;
                }
                if (op.Op == Op.Comment)
                {
                    var comment = Comments[op.OperandInt];
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
                    opCode += op.Operand;

                // print comment 
                if (op.Op == Op.Str)
                    sb.Add($"{opCode} # \"{JsonEncodedText.Encode(Strings[op.OperandInt])}\"");
                else if (op.Op == Op.Call)
                    sb.Add($"{opCode} # {Functions[op.OperandInt].FullName}");
                else if (op.Op == Op.LdIface)
                    sb.Add($"{opCode} # {Interfaces[op.OperandInt]}");
                else if (op.Op == Op.Float)
                    sb.Add($"{opCode} # {BitConverter.Int64BitsToDouble(op.Operand)}");
                else if (op.Op == Op.Ldlr)
                    sb.Add($"{opCode} # {tokenName} ref {tracer.Locals[op.OperandInt]}");
                else if (op.Op == Op.Loc)
                    sb.Add($"{opCode} # [{tracer.Locals.Count}] {tokenName} {Types[op.OperandInt]}");
                else if (op.Op == Op.NoImp)
                    sb.Add($"{opCode} # {Comments[op.OperandInt]}");
                else if (op.Op == Op.Br || op.Op == Op.Brif || op.Op == Op.Brnif)
                    sb.Add($"{opCode} # {tracer.BranchTarget(op.OperandInt)}");
                else
                    sb.Add($"{opCode}");

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
