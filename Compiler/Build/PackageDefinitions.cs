using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Zurfur.Build
{

    /// <summary>
    /// This is the package json, contained in both "Header.json" and
    /// "Code.json".  The difference is that the header only contains public
    /// symbols whereas the code has all symbols along with the code.
    /// </summary>
    public class PackageJson
    {
        // Compiler info
        public string CompilerName { get; set; } = "Zurfur";
        public string CompilerVersion { get; set; } = "0.0.0";

        /// <summary>
        /// Increment for each incompatible change
        /// </summary>
        public int FormatVersion { get; set; } = 0;

        // Project info
        public string Name { get; set; } = "";            // Unique across the world, e.g 'com.gosub.zurfur'
        public string Title { get; set; } = "";           // One line summary
        public string Description { get; set; } = "";     // Longer description, maybe a copy of readme.md
        public string Version { get; set; } = "0.0.0";
        public string BuildDate { get; set; } = "";       // ISO 8601
        public string Copyright { get; set; } = "Copyright ©";

        /// <summary>
        /// Optional list of files in the package.  The "FI" field of the
        /// points to the file name here.
        /// </summary>
        public string[] Files = Array.Empty<string>();

        public List<PackageSymbolJson> Symbols { get; set; } = new();
        public PackageCodeJson Code { get; set; } = new();
    }


    /// <summary>
    /// This is the sanitized version of `Symbol` so we have a clear separation
    /// between internal compiler data structures and the public API.
    /// </summary>
    public class PackageSymbolJson
    {
        public string Name { get; set; } = "";
        public string Tags { get; set; } = "";
        public string Comments { get; set; } = "";

        /// <summary>
        /// Type args and function parameters MUST be saved in the same order
        /// they are declared in the source code.
        /// </summary>
        public List<PackageSymbolJson> Symbols { get; set; } = new();

        // Anything not needed for this type can be omitted
        // by leaving it at the default value
        public string Type { get; set; } = "";     // Type of field or method parameter
        public Dictionary<string, string[]> Constraints { get; set; } = new();

        public string Requires { get; set; } = ""; // Require clause for methods (TBD: Make into a class) 

        public override string ToString() => Name;
    }


    public class PackageCodeJson
    {
        public Dictionary<string, PackageCodeMethodJson> Methods { get; set; } = new();
    }

    public class PackageCodeMethodJson
    {
        public string[] Zil { get; set; } = Array.Empty<string>();
    }

}
