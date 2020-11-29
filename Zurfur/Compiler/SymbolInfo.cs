using System;
using System.Collections.Generic;
using Gosub.Zurfur.Lex;


/// <summary>
/// This file contains low level symbol information.  The types in
/// SymbolTable contain the high high level information.
/// </summary>

namespace Gosub.Zurfur.Compiler
{
    class SymTypeInfo
    {
        public int Alignment;
        public int Size;
        public SymFieldInfo[] Fields = Array.Empty<SymFieldInfo>();
        public SymFieldInfo[] StaticFields = Array.Empty<SymFieldInfo>();
        public SymConstFieldInfo[] ConstFields = Array.Empty<SymConstFieldInfo>();
        public override string ToString()
        {
            return "Size=" + Size + ", " + Fields.Length + " fields, " + StaticFields.Length + " static fields, " + ConstFields + " const fields";
        }
    }

    struct SymFieldInfo
    {
        public Token Name;
        public SymType Type;
        public int Address;

        public override string ToString()
        {
            return Name == null || Type == null ? "?" : Name + " " + Type;
        }
    }

    struct SymConstFieldInfo
    {
        public Token Name;
        public SymType Type;
        public decimal ValueDecimal;  // And long, int, char, etc.  (excluding f32 and f64)
        public double ValueDouble;    // Only necessary since Decimal doesn't cover all possibilities like it should
        public override string ToString()
        {
            return Name == null || Type == null ? "?" : Name + " " + Type;
        }
    }
}
