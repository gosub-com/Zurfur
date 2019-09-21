using System;
using System.Collections.Generic;
using System.Text;
using System.Drawing;

namespace Gosub.Zurfur
{
    /// <summary>
    /// Basic types recognized by the lexer
    /// </summary>
    public enum eTokenType : short
    {
        // Lexer recognized types:
        Normal,
        Reserved,
        ReservedControl,
        Identifier,
        Number,
        Quote,
        Comment,
        TypeName
    }

    public enum eTokenBits : short
    {
        Error = 1,
        Warn = 2,
        Grayed = 4,
        Invisible = 8
    }


    /// <summary>
    /// Each lexical element is assigned a new token.  This class is
    /// also used by the parser and code generator to mark tokens with 
    /// error codes and other information.
    /// </summary>
    public class Token
    {
        public static readonly Token []EmptyArray = new Token[0];

        // Fields
        public readonly string Name = "";
        public TokenLoc Location;
        public eTokenType Type;
        eTokenBits mBits;
        Object mInfo; // Null, object, or List<Object>

        public bool Error
        {
            get { return (mBits & eTokenBits.Error) != 0; }
            set { mBits = mBits & ~eTokenBits.Error | (value ? eTokenBits.Error : 0); }
        }
        public bool Warn
        {
            get { return (mBits & eTokenBits.Warn) != 0; }
            set { mBits = mBits & ~eTokenBits.Warn | (value ? eTokenBits.Warn : 0); }
        }
        public bool Grayed
        {
            get { return (mBits & eTokenBits.Grayed) != 0; }
            set { mBits = mBits & ~eTokenBits.Grayed | (value ? eTokenBits.Grayed : 0); }
        }
        public bool Invisible
        {
            get { return (mBits & eTokenBits.Invisible) != 0; }
            set { mBits = mBits & ~eTokenBits.Invisible | (value ? eTokenBits.Invisible : 0); }
        }
        public void ClearBits()
        {
            mBits = 0;
        }

        public void AddInfo(object info)
        {
            if (mInfo == null)
            {
                mInfo = info;
                return;
            }
            var list = mInfo as List<Object>;
            if (list != null)
            {
                list.Add(info);
                return;
            }
            list = new List<object>();
            list.Add(mInfo);
            list.Add(info);
            mInfo = list;
        }
        public void RemoveInfo<T>()
        {
            if (mInfo == null)
                return;
            if (mInfo is T)
            {
                mInfo = null;
                return;
            }
            var list = mInfo as List<object>;
            if (list != null)
                list.RemoveAll((obj) => obj is T);
        }
        public void RepaceInfo<T>(T info)
        {
            RemoveInfo<T>();
            AddInfo(info);
        }
        public void ClearInfo()
        {
            mInfo = null;
        }

        public bool HasInfo()
        {
            return mInfo != null;
        }

        public T GetInfo<T>()
        {
            if (mInfo is T)
                return (T)mInfo;
            var list = mInfo as List<Object>;
            if (list != null)
                foreach (var elem in list)
                    if (elem is T)
                        return (T)elem;
            return default(T);
        }
        public T[] GetInfos<T>()
        {
            if (mInfo is T)
                return new T[1] { (T)mInfo };
            var list = mInfo as List<Object>;
            if (list == null)
                return new T[0];
            var infos = new List<T>();
            foreach (var elem in list)
                if (elem is T)
                    infos.Add((T)elem);
            return infos.ToArray();
        }
        public string GetInfoString()
        {
            var strings = GetInfos<string>();
            var sb = new StringBuilder();
            foreach (var s in strings)
            {
                sb.Append(s);
                sb.Append("\r\n\r\n");
            }
            return sb.ToString();
        }

        public int Y
        {
            get { return Location.Y; }
            set { Location.Y = value; }
        }
        public int X
        {
            get { return Location.X; }
            set { Location.X = value; }
        }

        public Token()
        {
        }

        public Token(string tokenName, int y, int x)
        {
            Name = tokenName;
            Y = y;
            X = x;
        }

        public Token(string tokenName, int y, int x, eTokenType tokenType)
        {
            Name = tokenName;
            Y = y;
            X = x;
            Type = tokenType;
        }

        public override string ToString()
        {
            return Name;
        }

        /// <summary>
        /// Reject this token (set error flag and append the message)
        /// NOTE: Duplicate error messages are ignored
        /// </summary>
        public void Reject(string errorMessage)
        {
            // Display error message
            Error = true;
            AddInfo(errorMessage);
        }

        public void AddWarning(string warnMessage)
        {
            Warn = true;
            AddInfo(warnMessage);
        }

        /// <summary>
        /// Convert this token to a string
        /// </summary>
        public static implicit operator string(Token token)
        {
            return token.Name;
        }

    }

    /// <summary>
    /// Keep track of the token location (Y is line number, X is column)
    /// </summary>
    public struct TokenLoc
    {
        /// <summary>
        /// Line number of token
        /// </summary>
        public int Y;

        /// <summary>
        /// Column of token
        /// </summary>
        public int X;

        public TokenLoc(int y, int x)
        {
            Y = y;
            X = x;
        }

        /// <summary>
        /// Ensure that low >= high (swap them if they are not)
        /// </summary>
        public static void FixOrder(ref TokenLoc low, ref TokenLoc high)
        {
            if (low > high)
            {
                TokenLoc temp = low;
                low = high;
                high = temp;
            }
        }

        public static bool operator==(TokenLoc a, TokenLoc b)
        {
            return a.Y == b.Y && a.X == b.X;
        }
        public static bool operator!=(TokenLoc a, TokenLoc b)
        {
            return !(a == b);
        }
        public static bool operator>(TokenLoc a, TokenLoc b)
        {
            return a.Y > b.Y || a.Y == b.Y && a.X > b.X;
        }
        public static bool operator<(TokenLoc a, TokenLoc b)
        {
            return a.Y < b.Y || a.Y == b.Y && a.X < b.X;
        }
        public static bool operator>=(TokenLoc a, TokenLoc b)
        {
            return a.Y > b.Y || a.Y == b.Y && a.X >= b.X;
        }
        public static bool operator<=(TokenLoc a, TokenLoc b)
        {
            return a.Y < b.Y || a.Y == b.Y && a.X <= b.X;
        }
        public override int GetHashCode()
        {
            return (Y << 12) + X;
        }
        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            if (!(obj is TokenLoc))
                return false;
            return this == (TokenLoc)obj;
        }
        public override string ToString()
        {
            return "" + "Line: " + Y + ", Char: " + X;
        }

    }

}
