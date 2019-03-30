using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;

namespace Gosub.Zurfur
{
    /// <summary>
    /// Serialized via JSON
    /// </summary>
    public class ZurfProjectData
    {
        public const string ZURFUR = "Zurfur";
        public const decimal VERSION = 0.001m;


        public string Zurfur = "";
        public decimal Version;
        public string ProjectPath = "";

        /// <summary>
        /// Check to see if this is a valid config file
        /// </summary>
        public bool IsValid() => Zurfur == ZURFUR && Version > 0;


    }
}
