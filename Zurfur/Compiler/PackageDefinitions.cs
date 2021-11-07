using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gosub.Zurfur.Compiler
{

    /// <summary>
    /// This is the package header, in the file "Header.json".
    /// See Zil.md
    /// </summary>
    public class PackageHeaderJson
    {
        // Compiler info
        public string CompilerName = "Zurfur";
        public string CompilerVersion = "0.0.0";
        public int FormatVersion = 0; // Increment for each incompatible change

        // Project info
        public string Name = "";            // Unique across the world, e.g 'com.gosub.zurfur'
        public string Title = "";           // One line summary
        public string Description = "";     // Longer description, maybe a copy of readme.md
        public string Version = "0.0.0";
        public string BuildDate = "";       // ISO 8601
        public string Copyright = "Copyright ©";

        /// <summary>
        /// Optional list of files in the package.  The "FI" field of the
        /// points to the file name here.
        /// </summary>
        public string[] Files = Array.Empty<string>();
        
        /// <summary>
        /// See `PackageSymbolsJson` for info
        /// </summary>
        public PackageSymbolJson Symbols = new PackageSymbolJson();
    }

    /// <summary>
    /// This is the sanitized version of `Symbol` so we have a clear separation
    /// between internal compiler data structures and the public API.
    /// 
    /// Names include the separator in front of the symbol, and are verified
    /// to be correct (e.g. modules begin with `.`, types begine with `/`, etc.)
    /// 
    /// Type names include the number of generic parameters
    /// (e.g. '/Range`1').  Method names include the group name
    /// (e.g. ':GetHashCode!$fun(Zurfur/bool)(Zurfur/i64)').
    /// 
    /// Names will be verified to be correct.  Exact rules still TBD.
    /// </summary>
    public class PackageSymbolJson
    {
        /// <summary>
        /// The name includes the separator at the beginning and suffix
        /// at the end.  The type of the symbol is determined by the
        /// separator at the beginning and is one of the following:
        ///     .   Module
        ///     /   Type name
        ///     @   Field name
        ///     :   Method, followed by prototype (contains $fun, $get, etc.)
        ///     #   Generic argument (followed by argument number)
        ///     ~   Method parameter (followed by ! for parameter or ` for return)
        ///     ~~  Generic parameter
        /// </summary>
        public string Name = "";
        public string[] Qualifiers = Array.Empty<string>();
        public string Comments = "";

        /// <summary>
        /// Optional token location in source code (File index, X, Y).
        /// When not present, FI is negative.
        /// </summary>
        public int FI = -1, X, Y;

        /// <summary>
        /// Type args and function parameters MUST be saved in the same order
        /// they are declared in the source code.
        /// </summary>
        public List<PackageSymbolJson> Symbols = new List<PackageSymbolJson>();

        // Anything not needed for this type can be omitted
        public string TypeName = ""; // Field, 

        public override string ToString()
        {
            return Name;
        }

    }
}
