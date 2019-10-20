﻿using System;
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
        PublicComment,
        CreateVariable,
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

        /// <summary>
        /// The string is always "", but error codes and location are undefined
        /// </summary>
        public static readonly Token Empty = new Token();

        // Fields
        public readonly string Name = "";
        public TokenLoc Location;
        public eTokenType Type;
        eTokenBits mBits;
        object mInfo; // Null, object, or List<Object>

        public Token()
        {
        }

        public Token(string name, int y, int x)
        {
            Name = name;
            Y = y;
            X = x;
        }

        public Token(string name, int y, int x, eTokenType tokenType)
        {
            Name = name;
            Y = y;
            X = x;
            Type = tokenType;
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

        public override string ToString()
        {
            return Name;
        }

        public bool Error
        {
            get => (mBits & eTokenBits.Error) != 0;
            set { mBits = mBits & ~eTokenBits.Error | (value ? eTokenBits.Error : 0); }
        }
        public bool Warn
        {
            get => (mBits & eTokenBits.Warn) != 0;
            set { mBits = mBits & ~eTokenBits.Warn | (value ? eTokenBits.Warn : 0); }
        }
        public bool Grayed
        {
            get => (mBits & eTokenBits.Grayed) != 0;
            set { mBits = mBits & ~eTokenBits.Grayed | (value ? eTokenBits.Grayed : 0); }
        }
        public bool Invisible
        {
            get => (mBits & eTokenBits.Invisible) != 0;
            set { mBits = mBits & ~eTokenBits.Invisible | (value ? eTokenBits.Invisible : 0); }
        }

        /// <summary>
        /// Clear info and bits, but not type or location
        /// </summary>
        public void Clear()
        {
            mBits = 0;
            mInfo = null;
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

        /// <summary>
        /// Add an error to this token (set error flag and append the message)
        /// </summary>
        public void AddError(string errorMessage)
        {
            // Display error message
            Error = true;
            AddInfo(errorMessage);
        }

        /// <summary>
        /// Add a warning to the token (set warning bit and append the message)
        /// </summary>
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

        /// <summary>
        /// Connect the tokens because they are logically connected, such as
        /// matching '(' and ')' or invisible replacement tokens.
        /// Use GetInfo<Token[]>() to find connected tokens.
        /// </summary>
        static public void Connect(Token t1, Token t2)
        {
            // Find tokens that are already connected
            var tokens = new List<Token>();
            var s1Connectors = t1.GetInfo<Token[]>();
            if (s1Connectors != null)
                tokens.AddRange(s1Connectors);
            var s2Connectors = t2.GetInfo<Token[]>();
            if (s2Connectors != null)
                tokens.AddRange(s2Connectors);

            // Add these tokens to the list
            tokens.Remove(t1);
            tokens.Remove(t2);
            tokens.Add(t1);
            tokens.Add(t2);

            // Set token info
            Token[] sa = tokens.ToArray();
            foreach (Token s in sa)
                s.RepaceInfo(sa);
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
