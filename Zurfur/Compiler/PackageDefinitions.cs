using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gosub.Zurfur.Compiler
{

    public class PackageHeaderJson
    {
        // Compiler info
        public string CompilerName = "Zurfur";
        public string CompilerVersion = "0.0.0";
        public string PackageFormatVersion = "0";

        // Project info
        public string Version = "0.0.0";
        public string BuildDate = "";
        public string Name = "";            // Unique across the world, e.g 'com.gosub.zurfur'
        public string Title = "";           // One line summary
        public string Description = "";     // Longer description, maybe a copy of readme.md
        public string Copyright = "Copyright ©";

        /// <summary>
        /// Optional list of files in the package.  The "FI" field of the
        /// points to the file name here.
        /// </summary>
        public string[] Files = Array.Empty<string>();
        
        /// <summary>
        /// See `PackageSymbolsJson` for info
        /// </summary>
        public PackageSymbolsJson Header = new PackageSymbolsJson();
    }

    /// <summary>
    /// This is the sanitized version of `Symbol`.  Names include the
    /// separator in front of the symbol, and are verified to be correct
    /// (e.g. modules begin with `.`, types begine with `/`, etc.)
    /// 
    /// Type names include the number of generic parameters
    /// (e.g. '/Range`1').  Method names include the group name
    /// (e.g. ':GetHashCode!$fun(Zurfur/bool)(Zurfur/i64)').
    /// 
    /// Names will be verified to be correct.  Exact rules still TBD.
    /// </summary>
    public class PackageSymbolsJson
    {
        public List<PackageModuleJson> Modules = new List<PackageModuleJson>();
    }

    public class PackageModuleJson
    {
        public string Name = "";
        public string []Qualifiers = Array.Empty<string>();
        public string Comments = "";
        public List<PackageModuleJson> Modules = new List<PackageModuleJson>();
        public List<PackageTypeJson> Types = new List<PackageTypeJson>();
        public List<PackageFieldJson> Fields = new List<PackageFieldJson>();
        public List<PackageMethodJson> Methods = new List<PackageMethodJson>();

    }
    public class PackageTypeJson
    {
        public string Name = "";
        public int FI, X, Y; // Optional token location in source code (File index, X, Y)
        public string[] Qualifiers = Array.Empty<string>();
        public string Comments = "";
        public List<PackageTypeParamsJson> TypeParams = new List<PackageTypeParamsJson>();
        public List<PackageTypeJson> Types = new List<PackageTypeJson>();
        public List<PackageFieldJson> Fields = new List<PackageFieldJson>();
        public List<PackageMethodJson> Methods = new List<PackageMethodJson>();
    }

    public class PackageMethodJson
    {
        public string Name = "";
        public int FI, X, Y; // Optional token location in source code (File index, X, Y)
        public string[] Qualifiers = Array.Empty<string>();
        public string Comments = "";
        public List<PackageTypeParamsJson> TypeParams = new List<PackageTypeParamsJson>();
        public List<PackageMethodParamsJson> Params = new List<PackageMethodParamsJson>();
        public List<PackageMethodParamsJson> Returns = new List<PackageMethodParamsJson>();
    }

    public class PackageFieldJson
    {
        public string Name = "";
        public int FI, X, Y; // Optional token location in source code (File index, X, Y)
        public string[] Qualifiers = Array.Empty<string>();
        public string Comments = "";
        public string TypeName = "";
    }

    public class PackageTypeParamsJson
    {
        public string Name = "";
        public int FI, X, Y; // Optional token location in source code (File index, X, Y)
        public string[] Qualifiers = Array.Empty<string>();
        public string Comments = "";
    }
    public class PackageMethodParamsJson
    {
        public string Name = "";
        public int FI, X, Y; // Optional token location in source code (File index, X, Y)
        public string[] Qualifiers = Array.Empty<string>();
        public string Comments = "";
        public string TypeName = "";
    }

}
