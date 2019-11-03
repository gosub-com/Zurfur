using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// Zurfur Simple Intermediate Language (ZSIL) is Zurfur's object file
    /// format, serialized to JSON.  There may be several versions of the
    /// file depending on the need.  For instance, a header file would not
    /// contain function bodies but could be compiled very quickly, and a
    /// module level pre-optimized (possibly obfuscated) could be distributed
    /// publicly and would would make final code generation much faster.
    /// 
    /// File types:
    /// 
    ///     .zsil - Everything needed to compile a single file
    ///     .zsilp - A package, ready for public distribution.
    ///              Possibly obfuscated, possibly some optimization,
    ///              but platform and build independent
    ///     .zsilobj - Low level, platform and build specific,
    ///                ready to be transformed to WebAssembly.
    ///     
    /// Compilation steps:
    /// 
    ///     1) Generate definitions containing a list of functions and
    ///        classes, but no resolved type names.  This phase does not
    ///        require having access to any external info.
    ///        
    ///     2) Generate a header file containing class and function
    ///        prototypes with external type names resolved.  This requires
    ///        having the definitions for each included external module.
    ///        
    ///     3) Generate the object file containing everything.
    /// 
    /// TBD: Type string name headers: 
    ///     "$" Zurfur reserved ($int, $int32, $str, $Array, $ref, $ro, etc.)
    ///     ".." Module local type name
    ///     "." Fully qualified type name
    ///     "!" Type argument
    ///     ":" Parameter name
    ///     "@" Op code
    /// </summary>
    public class SilModule
    {
        public const int VERSION = 1;
        public string Comments = "";
        public int Version = VERSION;
        public SilFile []Files = new SilFile[0];
    }

    public class SilFile
    {
        public string Module = "";
        public SilType[] Types = new SilType[0];
        public SilFunc[] Funcs = new SilFunc[0];
    }

    /// <summary>
    /// Field layout of a type (struct or class), enum is TBD
    /// </summary>
    public class SilType
    {
        public int X, Y;
        public string Attributes = "";
        public string Name = "";
        public SilField[] Fields = new SilField[0];
        public SilField[] StaticFields = new SilField[0];
    }

    public class SilField
    {
        public int X, Y;
        public string Comments = "";
        public string Attributes = "";
        public string Name = "";
        public string Type = "";
    }

    /// <summary>
    /// Function information.
    /// </summary>
    public class SilFunc
    {
        public int X, Y;
        public string Comments = "";
        public string Attributes = "";
        public string Name = "";

        /// <summary>
        /// Parameters are the type name.  If they start with ":",
        /// then it's a parameter name, and it's type name follows.
        /// Return values are after the parameters, and are separated
        /// by a "->".  For now, only one return value may be specified.
        /// </summary>
        public string[] Params = new string[0];

        /// <summary>
        /// Op codes begin with "@", and what follows are parameters.
        /// </summary>
        public string[] Body = new string[0];

    }



}
