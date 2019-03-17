using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Gosub.Zurfur
{

    class SyntaxExprUnary : SyntaxExpr
    {
        SyntaxExpr mParam0;

        public SyntaxExprUnary(Token function, SyntaxExpr p0)
            : base(function)
        {
            mParam0 = p0 ?? throw new ArgumentNullException("p0 must not be null");
        }

        public override int Count => 1;

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

    // Binary operator
    class SyntaxExprBinary : SyntaxExpr
    {
        SyntaxExpr mParam0;
        SyntaxExpr mParam1;

        public SyntaxExprBinary(Token function, SyntaxExpr p0, SyntaxExpr p1)
            : base(function)
        {
            mParam0 = p0 ?? throw new ArgumentNullException("p0 must not be null");
            mParam1 = p1 ?? throw new ArgumentNullException("p1 must not be null");
        }

        public override int Count => mParam0 == null ? 0 : (mParam1 == null ? 1 : 2);

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
        public SyntaxExprMulti(Token function, SyntaxExpr[] parameters)
            : base(function)
        {
            mParameters = parameters;
        }

        public override int Count => mParameters.Length;

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


    /// <summary>
    /// Base for expression tree or leaf node
    /// </summary>
    class SyntaxExpr : IEnumerable<SyntaxExpr>
    {
        protected readonly static Token sEmptyToken = new Token("", 0, 0);
        public readonly Token Function;

        public SyntaxExpr()
        {
            Function = sEmptyToken;
        }

        public SyntaxExpr(Token function)
        {
            Function = function;
        }

        public virtual int Count => 0;
        public virtual SyntaxExpr this[int index] => throw new IndexOutOfRangeException();
        public virtual IEnumerator<SyntaxExpr> GetEnumerator()
        {
            yield break;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
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

            var q = Function.Name == "(" || Function.Name == ")" ? "'" : "";
            sb.Append(q + Function.Name + q);
            
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
}
