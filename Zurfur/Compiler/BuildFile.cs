using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// These are owned by BuildPackage, used for communication with the IDE
    /// </summary>
    class BuildFile
    {
        public readonly string FileName = "";
        BuildPackage mPackage;
        Lexer mLexer = new Lexer();

        public BuildFile(string fileName, BuildPackage package)
        {
            FileName = fileName;
            mPackage = package;
        }

        /// <summary>
        /// Incremented whenever the build has changed something in the file
        /// </summary>
        public int FileBuildVersion;

        /// <summary>
        /// An editor may set the lexer, in which case, that lexer
        /// will override the one loaded from the file system.  
        /// NOTE: The editor should call this once to set the
        /// new lexer, but should call `FileChanged` each time the
        /// text changes
        /// </summary>
        public Lexer Lexer
        {
            get { return mLexer; }
            set
            {
                mLexer = value;
                FileModified();
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
        /// This should be called each time the `Lexer` text or file is
        /// modified by an external source
        /// </summary>
        public void FileModified()
        {
            mPackage.FileModifiedInternal(this);
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
