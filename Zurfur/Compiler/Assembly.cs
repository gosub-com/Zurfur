using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{

    class Assembly
    {
        public readonly List<string> Comments = new();
        public readonly ConsolidatedList<Symbol> Symbols = new();
        public readonly ConsolidatedList<string> Strings = new();
        public readonly ConsolidatedList<string> Translated = new();
        public readonly Dictionary<string, AsFun> Functions = new();

        public Assembly() 
        {
            Strings.AddOrFind("");
            Translated.AddOrFind("");
        }

        public void Print(StringBuilder sb)
        {
            // Print code
            sb.Append("code:\r\n");
            foreach (var fun in Functions.Values.OrderBy(i => i.Name))
                fun.Print(sb);

            // Print symbols
            sb.Append("\r\nsymbols:\r\n");
            var index = 0;
            foreach (var symbol in Symbols)
                sb.Append($"    {index++} {symbol}\r\n");

            // Print strings
            sb.Append("\r\nstrings:\r\n");
            index = 0;
            foreach (var str in Strings)
                sb.Append($"    {index++} \"{JsonEncodedText.Encode(str)}\"\r\n");

            sb.Append("\r\ntranslate:\r\n");
            sb.Append("    tbd...\r\n\r\n");


        }
    }

    class AsFun
    {
        public readonly Assembly Assembly;
        public readonly string Name;
        public readonly Token Token;
        public readonly List<Symbol> LocalTypes = new List<Symbol>();
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
            Add(Op.Call, token, Assembly.Symbols.AddOrFind(fun));
        }

        // New local variable within current scope, returns local index
        public int UseLocal(Token token, Symbol type)
        {
            var localNum = LocalTypes.Count;
            Code.Add(new AsOp(Op.Use, localNum));
            CodeTokens.Add(token);
            LocalTypes.Add(type);
            return localNum;
        }

        // Load local reference
        public void Ldlr(Token token, int localIndex)
        {
            Debug.Assert(localIndex >= 0 && localIndex < LocalTypes.Count);
            Code.Add(new AsOp(Op.Ldlr, localIndex));
            CodeTokens.Add(token);
        }

        public void Print(StringBuilder sb)
        {
            sb.Append("\r\n\r\n");
            sb.Append($"fun {Name}\r\n");
            var i = 0;
            foreach (var type in LocalTypes)
                sb.Append($"    {i++} {type}\r\n");

            int level = 0;
            int opIndex = -1;
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
                    sb.Append($" # {Assembly.Symbols[op.OperInt].FullName}");
                else if (op.Op == Op.F64)
                    sb.Append($" # {BitConverter.Int64BitsToDouble(op.Oper)}");
                else if (op.Op == Op.Use || op.Op == Op.Ldlr)
                    sb.Append($" # {tokenName} {LocalTypes[op.OperInt].Type}");
                else if (op.Op == Op.NoImp)
                    sb.Append($" # {Assembly.Comments[op.OperInt]}");
                else if (op.Op == Op.Br || op.Op == Op.Brif || op.Op == Op.Brnif)
                    sb.Append($" # {FindBranchTarget(opIndex)}");
                sb.Append($"\r\n");
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
            if (opIndex < 0)
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

    enum Op
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
        Use,    // Use a local in this scope, operand is an index into locals
        Call,   // Operand is index into symbols
        Ldlr,   // Load local ref, operand is an index into locals
        Setr,   // Store into reference (value, ref)
        Getr,   // Get from reference
    }

    struct AsOp
    {
        public readonly Op      Op;
        public long Oper { get; private set; }

        public AsOp(Op op, long operand)
        {
            Op = op;
            Oper = operand;
        }

        public int OperInt
        {
            get 
            {
                return (int)Oper; 
            }
        }
    }

    /// <summary>
    /// Consolidate re-used objects into a list.  Items are found by name (i.e. item.ToString())
    /// </summary>
    class ConsolidatedList<T>
    {
        List<T> mList = new();
        Dictionary<string, int> mMap = new();

        /// <summary>
        /// Add to list or re-use the one that's already there.
        /// Uses item.ToString() to find and compare items.
        /// Returns the index of the item.
        /// </summary>
        public int AddOrFind(T item)
        {
            if (mMap.TryGetValue(item.ToString(), out var index))
                return index;
            mMap[item.ToString()] = index;
            mList.Add(item);
            return mList.Count - 1;
        }

        public List<T>.Enumerator GetEnumerator() => mList.GetEnumerator();
        public int Count => mList.Count;
        public T this[int i]
        {
            get { return mList[i]; }
            set { mList[i] = value; }
        }
    }

}
