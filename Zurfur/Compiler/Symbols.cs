using System;
using System.Collections.Generic;

namespace Gosub.Zurfur.Compiler
{

    enum SymTypeEnum
    {
        Namespace,
        Class, // or Struct, Enum, Interface
        Funcs,
        Func,
        Field
    }

    class SymPackage
    {
        public string PackageName = "";
        public PackageInfo PackageInfo = new PackageInfo();
        public CompilerInfo CompilerInfo = new CompilerInfo();

        public Dictionary<string, SymScope> Symbols = new Dictionary<string, SymScope>();
    }

    class PackageInfo
    {
        public DateTime BuildDate = DateTime.Now;
        public string Title = "";
        public string Description = "";
        public string Company = "";
        public string Product = "";
        public string Copyright = "";
        public string Version = "";
    }

    class CompilerInfo
    {
        public string Name = "Zurfur";
        public string Version = "0.0.0";

        /// <summary>
        /// The platform is "ZSIL" for a package ready for public distribution.
        /// Everything else is tied to a specific compiler, buildname, 
        /// and all build options and constants.
        /// </summary>
        public string Platform = "";
        public int PlatformVersion;
        public string BuildName = ""; // Debug, Release, etc.
        public Dictionary<string, string> BuildOptions = new Dictionary<string, string>();
        public Dictionary<string, string> BuildConstants = new Dictionary<string, string>();
    }

    class SymFile
    {
        public string FileName = "";
    }

    /// <summary>
    /// Namespace, class, struct, enum, interface
    /// </summary>
    class SymScope
    {
        public SymTypeEnum Type;
        public string Name = "(none)";
        public string Comments = "";
        public SymFile File;
        public Token NameToken = Token.Empty;
        public Dictionary<string, SymScope> Symbols = new Dictionary<string, SymScope>();


        public SymScope(SymTypeEnum type)
        {
            Type = type;
        }

        public override string ToString()
        {
            return Type + ":" + Name;
        }
    }

    class SymNamespace : SymScope
    {
        public SymNamespace() : base(SymTypeEnum.Namespace) { }
    }

    class SymClass : SymScope
    {
        public SymClass() : base(SymTypeEnum.Class) {  }
    }

    class SymField : SymScope
    {
        public SymField() : base(SymTypeEnum.Field) { }
    }

    class SymFuncs : SymScope
    {
        public SymFuncs() : base(SymTypeEnum.Funcs) { }
    }

    class SymFunc : SymScope
    {
        public SymFunc() : base(SymTypeEnum.Func) { }
    }

}
