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
        /// Get or set a clone of the build manager lexer.  The first time a
        /// lexer is needed, it must come through the build manager from here.
        /// When the text changes, this property can be set to override the
        /// text in the build manager (and also on disk).   Getting and
        /// setting create a clone of the lexer (but share tokens)so it can be
        /// re-parsed in a background thread
        /// </summary>
        public Lexer Lexer
        {
            // The build manager never mutates the lexer, so assuming this
            // is called from the UI thread, it should be thread safe
            get { return mLexer.Clone(); }

            set
            {
                mLexer = value.Clone();
                mPackage.FileModifiedInternal(this);
            }
        }

        /// <summary>
        /// During parsing or linking, extra error tokens may be generated.
        /// </summary>
        public Token[] ExtraTokens { get; private set; } = new Token[0];


        /// <summary>
        /// Called by BuildPackage only
        /// </summary>
        /// <param name="tokens"></param>
        public void SetExtraErrorTokensInternal(Token[] tokens)
        {
            ExtraTokens = tokens;
        }

        /// <summary>
        /// When interactive, spend more time generating feedback, such
        /// as connecting tokens and symbols.  When not interactive,
        /// only collect error info.
        /// </summary>
        public bool Interactive { get; set; }

        /// <summary>
        /// Number of errors in this file
        /// </summary>
        public int Errors { get; set; }



    }


}
