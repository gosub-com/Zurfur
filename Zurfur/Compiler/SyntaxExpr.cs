using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Gosub.Zurfur
{
    /// <summary>
    /// Base class for an expression tree.
    /// </summary>
    abstract class SyntaxExpr : IEnumerable<SyntaxExpr>
    {
        public readonly Token Token;
        public readonly int Count;
        public abstract SyntaxExpr this[int index] { get; }
        public abstract IEnumerator<SyntaxExpr> GetEnumerator();

        public SyntaxExpr(Token token, int count)
        {
            Token = token ?? throw new ArgumentNullException("Token must not be null");
            Count = count;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException("Do not use IEnumerable.GetEnumerator().  Use IEnumerable<Type>.GetEnumerator() instead.");
        }

        /// <summary>
        /// Generate an expression list (as if this were lisp)
        /// </summary>
        void ToString(StringBuilder sb, int level)
        {
            if (level >= 10)
            {
                sb.Append("*OVF*");
                return;
            }

            if (Count != 0)
                sb.Append("(");

            var q = Token.Name == "(" || Token.Name == ")" ? "'" : "";
            sb.Append(q + Token.Name + q);

            foreach (var param in this)
            {
                sb.Append(" ");
                param.ToString(sb, level + 1);
            }
            if (Count != 0)
                sb.Append(")");
        }

        /// <summary>
        /// Display the expression list (as if this were lisp)
        /// </summary>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            ToString(sb, 0);
            return sb.ToString();
        }
    }

    class SyntaxExprToken : SyntaxExpr
    {
        public SyntaxExprToken(Token token)
            : base(token, 0)
        {
        }

        public override SyntaxExpr this[int index]
        {
            get
            {
                throw new IndexOutOfRangeException();
            }
        }
        public override IEnumerator<SyntaxExpr> GetEnumerator()
        {
            yield break;
        }
    }

    class SyntaxExprUnary : SyntaxExpr
    {
        SyntaxExpr mParam0;

        public SyntaxExprUnary(Token token, SyntaxExpr p0)
            : base(token, 1)
        {
            mParam0 = p0 ?? throw new ArgumentNullException("p0 must not be null");
        }

        public override SyntaxExpr this[int index]
        {
            get
            {
                if (index == 0)
                    return mParam0;
                throw new IndexOutOfRangeException();
            }
        }
        public override IEnumerator<SyntaxExpr> GetEnumerator()
        {
            yield return mParam0;
        }
    }

    class SyntaxExprBinary : SyntaxExpr
    {
        SyntaxExpr mParam0;
        SyntaxExpr mParam1;

        public SyntaxExprBinary(Token token, SyntaxExpr p0, SyntaxExpr p1)
            : base(token, 2)
        {
            mParam0 = p0 ?? throw new ArgumentNullException("p0 must not be null");
            mParam1 = p1 ?? throw new ArgumentNullException("p1 must not be null");
        }

        public override SyntaxExpr this[int index]
        {
            get
            {
                if (index == 0)
                    return mParam0;
                if (index == 1)
                    return mParam1;
                throw new IndexOutOfRangeException();
            }
        }

        public override IEnumerator<SyntaxExpr> GetEnumerator()
        {
            yield return mParam0;
            yield return mParam1;
        }
    }

    // Multi-paramerter expression
    class SyntaxExprMulti : SyntaxExpr
    {
        SyntaxExpr[] mParameters;

        public SyntaxExprMulti(Token token, SyntaxExpr[] parameters)
            : base(token, parameters.Length)
        {
            if (parameters == null)
                throw new ArgumentNullException("parameters must not be null");
            foreach (var p in parameters)
                if (p == null)
                    throw new ArgumentNullException("parameters must not contain a null expression");
            mParameters = parameters;
        }

        public SyntaxExprMulti(Token token, SyntaxExpr p0, SyntaxExpr p1, SyntaxExpr p2)
            : base(token, 3)
        {
            if (p0 == null || p1 == null || p2 == null)
                throw new ArgumentNullException("p0, p1, and p2 must not be null");
            mParameters = new SyntaxExpr[] { p0, p1, p2 };
        }

        public override SyntaxExpr this[int index]
        {
            get { return mParameters[index]; }
        }

        public override IEnumerator<SyntaxExpr> GetEnumerator()
        {
            foreach (var parameter in mParameters)
                yield return parameter;
        }
    }

}
