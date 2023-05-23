using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gosub.Zurfur.Lex;

namespace Gosub.Zurfur.Compiler
{
    class AsFuns
    {
        public readonly Dictionary<string, AsFun> Functions = new();

    }

    class AsFun
    {
        public readonly string Name;
        public readonly Token Token;
        public readonly List<Symbol> Locals = new List<Symbol>();
        public readonly AsScope Scope;


        public AsFun(string name, Token token)
        {
            Name = name;
            Token = token;
            Scope = new AsScope(null, this);
        }
    }


    class AsScope
    {
        public readonly AsFun Fun;
        public readonly AsScope Parent;
        public readonly List<AsOp> OpList = new();

        public AsScope(AsScope parent, AsFun fun)
        {
            Parent = parent;
            Fun = fun;
        }

        // New local variable, returns local index
        public int NewLocal(Token token, Symbol type)
        {
            var localNum = Fun.Locals.Count;
            OpList.Add(new AsOp(Op.Local, token, localNum));
            Fun.Locals.Add(type);
            return localNum;
        }

        public void NoImp(Token token, string tbd)
        {
            OpList.Add(new AsOp(Op.NoImp, token, tbd));
        }

        // Load local reference
        public void Ldlr(Token token, int localIndex)
        {
            Debug.Assert(localIndex >= 0 && localIndex < Fun.Locals.Count);
            OpList.Add(new AsOp(Op.Ldlr, token, localIndex));
        }

        public void Call(Token token, Symbol func)
        {
            OpList.Add(new AsOp(Op.Call, token, func));
        }

        public void Return(Token token)
        {
            OpList.Add(new AsOp(Op.Return, token));
        }

        public void Break(Token token, int targetLlevel)
        {
            OpList.Add(new AsOp(Op.Break, token, targetLlevel));
        }

        public void Comment(Token token, string comment)
        {
            OpList.Add(new AsOp(Op.Comment, token, comment));
        }

        public AsScope Scope(Token token)
        {
            var scope = new AsScope(this, Fun);
            OpList.Add(new AsOp(Op.Scope, token, scope));
            return scope;
        }
    }


    enum Op
    {
        Nop,    // Operand is null
        Comment,// Operand is string
        NoImp,  // Invalid code (not implemented yet)
        Local,  // Operand is null
        Ldlr,   // Load local ref, operand is int (local number)
        Call,   // Operand is symbol
        Return, // Operand is null
        Scope,  // Operand is AssemblyFunScope
        Break,  // Operand is int (target level)
        Brif,   // Operand is int (target level)
    }

    struct AsOp
    {
        public readonly Op      Op;
        public readonly Token   Token;
        readonly object         _operand;

        public AsOp(Op op, Token token, object operand = null)
        {
            Token = token;
            Op = op;
            _operand = operand;
            Debug.Assert(
                op == Op.Nop && operand == null
                || op == Op.Comment && operand is string
                || op == Op.NoImp && operand is string
                || op == Op.Local && operand is int
                || op == Op.Ldlr && operand is int
                || op == Op.Call && operand is Symbol sym && (sym.IsFun || sym.IsLambda)
                || op == Op.Return && operand == null
                || op == Op.Scope && operand is AsScope);
        }

        public int OperInt
        {
            get 
            {
                Debug.Assert(_operand is int);
                return (int)_operand; 
            }
        }

        public string OperString
        {
            get
            {
                Debug.Assert(_operand is string);
                return (string)_operand;
            }
        }

        public Symbol OperSym
        {
            get
            {
                Debug.Assert(_operand is Symbol);
                return (Symbol)_operand;
            }
        }

        public AsScope OperScope
        {
            get
            {
                Debug.Assert(_operand is AsScope);
                return (AsScope)_operand;
            }
        }

        public object OperObject
        {
            get { return _operand; }
        }

    }

}
