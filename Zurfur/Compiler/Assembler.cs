using Gosub.Zurfur.Lex;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Formats.Asn1.AsnWriter;
using System.Xml.Linq;
using System.Collections;

namespace Gosub.Zurfur.Compiler
{
    static class Assembler
    {

        public static void Print(this AsFuns my, StringBuilder sb)
        {
            foreach (var fun in my.Functions.Values.OrderBy(i => i.Name))
                fun.Print(sb);
        }

        public static void Print(this AsFun my, StringBuilder sb)
        {
            sb.Append("\r\n\r\n");
            sb.Append("fun " + my.Name);
            for (int i = 0; i < my.Locals.Count; i++)
            {
                var t = my.Locals[i].HasToken ? my.Token.Name : "(null)";
                sb.Append($"  local   {i:2} {my.Locals[i]} // {t}\r\n");
            }
            my.Scope.Print(sb, 0);
        }

        public static void Print(this AsScope my, StringBuilder sb, int level)
        {
            sb.Append(' ', level * 2);
            sb.Append("{\r\n");
            foreach (var op in my.OpList)
            {
                if (op.Op == Op.Scope)
                {
                    op.OperScope.Print(sb, level + 1);
                    continue;
                }
                sb.Append(' ', (level + 1) * 2);
                sb.Append($"{op.Op.ToString(),-8}{op.OperObject}\r\n");
            }
            sb.Append(' ', level * 2);
            sb.Append("}\r\n");
        }

    }
}
