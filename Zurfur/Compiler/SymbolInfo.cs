using System;
using System.Text;
using System.Collections.Generic;


namespace Gosub.Zurfur.Compiler
{
    class SymTypeInfo
    {
        public string Name = "";
        public int Alignment;
        public int Size;
        public string[] TypeArgs = Array.Empty<string>();
        public SymFieldInfo[] Fields = Array.Empty<SymFieldInfo>();
        public SymFieldInfo[] StaticFields = Array.Empty<SymFieldInfo>();
        public SymConstFieldInfo[] ConstFields = Array.Empty<SymConstFieldInfo>();
        public SymMethodInfo[] Methods = Array.Empty<SymMethodInfo>();

        public string FullNameWithTypeArgs()
        {
            if (TypeArgs.Length == 0)
                return Name;
            StringBuilder sb = new StringBuilder();
            sb.Append(Name);
            sb.Append("<");
            sb.Append(string.Join(",", TypeArgs));
            sb.Append(">");
            return sb.ToString();
        }

        public override string ToString()
        {
            return "Size=" + Size + ", " + Fields.Length + " fields, " + StaticFields.Length + " static fields, " + ConstFields + " const fields";
        }
    }

    class SymFieldInfo
    {
        public string Name = "";
        public string Type = "";
        public int Address;

        public override string ToString()
        {
            return Name + " " + Type;
        }
    }

    class SymConstFieldInfo
    {
        public string Name = "";
        public string Type = "";
        public decimal ValueDecimal;  // And long, int, char, etc.  (excluding f32 and f64)
        public double ValueDouble;    // Only necessary since Decimal doesn't cover all possibilities like it should
        public override string ToString()
        {
            return Name + " " + Type;
        }
    }

    class SymMethodInfo
    {
        public string Name = "";
        public string[] Params = Array.Empty<string>();
        public string[] Returns = Array.Empty<string>();
    }
}
