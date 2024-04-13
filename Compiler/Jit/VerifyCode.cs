using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zurfur.Jit;

public static class VerifyCode
{
    public static void Verify(Assembly assembly, SymbolTable table)
    {
        return;
        var tracer = new AsTrace(assembly, table);
        while (tracer.OpIndex < assembly.Code.Count)
        {
            var opIndex = tracer.OpIndex;
            var errorMessage = tracer.Trace();
            if (errorMessage != null)
            {
                assembly.Errors.Add(new Assembly.ErrorInfo()
                {
                    OpIndex = opIndex,
                    ErrorMessage = errorMessage
                });
                if (opIndex < assembly.DebugTokens.Count)
                    assembly.DebugTokens[opIndex].AddError(new VerifyError(errorMessage));
            }
        }
    }

}
