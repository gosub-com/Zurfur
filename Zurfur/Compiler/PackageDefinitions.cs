using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gosub.Zurfur.Compiler
{

    /// <summary>
    /// This is the package json, contained in both "Header.json" and
    /// "Code.json".  The difference is that the header only contains public
    /// symbols whereas the code has all symbols along with the code.
    /// </summary>
    public class PackageJson
    {
        // Compiler info
        public string CompilerName = "Zurfur";
        public string CompilerVersion = "0.0.0";

        /// <summary>
        /// Increment for each incompatible change
        /// </summary>
        public int FormatVersion = 0;

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
        public List<PackageSymbolJson> Symbols;
        public PackageCodeJson Code;

        /// <summary>
        /// EXPERIMENT: Modules, types, methods.  
        /// </summary>
        public Dictionary<string, PackageSymbolJson> SymbolsMapExperiment;

    }


    /// <summary>
    /// This is the sanitized version of `Symbol` so we have a clear separation
    /// between internal compiler data structures and the public API.
    /// </summary>
    public class PackageSymbolJson
    {
        public string Kind; // module, type, field, method, param, return, param_type
        public string Name;
        public string[] Qualifiers;
        public string Comments;

        /// <summary>
        /// Type args and function parameters MUST be saved in the same order
        /// they are declared in the source code.
        /// </summary>
        public List<PackageSymbolJson> Symbols;

        // Anything not needed for this type can be omitted
        // by leaving it at the default value
        public string Type;     // Type of field or method parameter
        public string Where;    // Where clause for type args (TBD: Will probably be a class)
        public string Requires; // Require clause for methods (TBD: Make into a class) 

        public override string ToString() => Name;
    }


    public class PackageCodeJson
    {
        public Dictionary<string, PackageCodeMethodJson> Methods;
    }

    public class PackageCodeMethodJson 
    {
        public string[] Zil;
    }

}
