using System;
using System.Collections.Generic;

using Gosub.Zurfur.Lex;
using Gosub.Zurfur.Compiler;

namespace Gosub.Zurfur.Build
{
    /// <summary>
    /// These are owned by BuildPackage, used for communication with the IDE
    /// </summary>
    class BuildFile
    {
        public readonly string FileName = "";
        BuildManager mPackage;
        Lexer mLexer = new LexZurf();
        public DateTime LastWriteTimeUtc;

        public BuildFile(string fileName, BuildManager package)
        {
            FileName = fileName;
            mPackage = package;
        }

        /// <summary>
        /// Incremented whenever the build has changed something in the file
        /// </summary>
        public int FileBuildVersion;

        /// <summary>
        /// Get or set the build manager lexer.  The first time a
        /// lexer is needed, it must come through the build manager from here.
        /// When the text changes, this property can be set to override the
        /// text in the build manager (and also on disk).  
        /// Not thread safe, this should mirror the UI editor lexer.
        /// Any background processing should create a clone.
        /// </summary>
        public Lexer Lexer
        {
            get { return mLexer; }

            set
            {
                mLexer = value;
                mPackage.FileModifiedInternal(this);
            }
        }

    }


}
