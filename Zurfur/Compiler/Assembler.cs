using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System.Diagnostics;

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
            sb.Append($"fun {my.Name}\r\n");
            for (int i = 0; i < my.Locals.Count; i++)
                sb.Append($"  local   {my.Locals[i]} # {i} {my.Locals[i].Type}\r\n");
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
                if (op.Op == Op.Comment)
                {
                    var comment = op.OperString;
                    if (comment == "")
                        sb.Append("\r\n");
                    else
                        sb.Append($"// {comment}\r\n");
                    continue;
                }
                sb.Append(' ', (level + 1) * 2);
                if (op.Op == Op.Local || op.Op == Op.Ldlr)
                {
                    var i = op.OperInt;
                    sb.Append($"{op.Op.ToString(),-8}{i} # '{op.Token}' {my.Fun.Locals[i].Type}\r\n");
                    continue;
                }
                sb.Append($"{op.Op.ToString(),-8}{op.OperObject}\r\n");
            }
            sb.Append(' ', level * 2);
            sb.Append("}\r\n");
        }

    }
}
