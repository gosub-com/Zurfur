using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Gosub.Zurfur.Compiler
{
    /// <summary>
    /// The builder will eventually load the files from the filesystem
    /// and compile them in background threads.  It can then be handed
    /// a lexer from an editor which will override the one on the
    /// file system.  
    /// For now, it compiles only the files handed to it by the editor.
    /// </summary>
    class BuildPackage
    {

        string mRoot = "";
        Dictionary<string, BuildFile> mFiles = new Dictionary<string, BuildFile>();
        List<TaskCompletionSource<Change>> mBuildCompleteListeners = new List<TaskCompletionSource<Change>>();

        // Track modified files
        bool mModifiedAnyFile;
        DateTime mModifiedAnyFileTime;
        Dictionary<string, bool> mModifiedFiles = new Dictionary<string, bool>();

        public enum Change
        {
            FilesLoaded,
            BuildChanged,
            BuildDone
        }

        /// <summary>
        /// TBD: Not implemented yet
        /// Load the directory structure (does not load individual files).
        /// </summary>
        public void LoadDir(string root)
        {

        }

        /// <summary>
        /// Returns a file in the package, or NULL if it is not in the build
        /// </summary>
        public BuildFile GetFile(string fileName)
        {
            mFiles.TryGetValue(fileName, out var file);
            return file;
        }

        /// <summary>
        /// This is temporary and will be removed when BuildPackage manages all the
        /// files via LoadDir.  For now, the logic is still mixed with ZurfEditContoller
        /// </summary>
        public BuildFile AddFile(string fileName, Lexer lexer)
        {
            if (!mFiles.TryGetValue(fileName, out var buildFile))
            {
                buildFile = new BuildFile(fileName, this);
                mFiles[fileName] = buildFile;
            }
            buildFile.Lexer = lexer;
            return buildFile;
        }

        /// <summary>
        /// Temporary, will be removed, same as `AddFile`
        /// </summary>
        public void RemoveFile(string fileName)
        {
            mFiles.Remove(fileName);
        }

        /// <summary>
        /// Task completes whenever a build phase has changed, or the build is complete.
        /// False: Build not complete, but something is done.  True build is complete.
        /// </summary>
        public Task<Change> AwaitBuildChanged()
        {
            var tcs = new TaskCompletionSource<Change>();
            lock (mBuildCompleteListeners)
                mBuildCompleteListeners.Add(tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Send build changed notification to ask awaiters to do something now
        /// </summary>
        public void ForceNotifyBuildChanged()
        {
            NotifyBuildChanged(Change.BuildChanged);
        }

        // Ugh, is this the best way to do this?  Maybe a simple callback delegate would be better.
        void NotifyBuildChanged(Change complete)
        {
            TaskCompletionSource<Change>[] listeners;
            lock (mBuildCompleteListeners)
            {
                listeners = mBuildCompleteListeners.ToArray();
                mBuildCompleteListeners.Clear();
            }
            foreach (var tcs in listeners)
                if (!tcs.Task.IsCompleted)
                    tcs.TrySetResult(complete);
        }


        /// <summary>
        /// Called by `BuildFile` each time a file changes.  Do not call
        /// this directly, use `BuildFile.FileModified` instead
        /// </summary>
        public void FileModifiedInternal(BuildFile file)
        {
            if (!mFiles.ContainsKey(file.FileName))
                throw new Exception("Error updating file, not part of the package: " + file.FileName);
            mModifiedAnyFile = true;
            mModifiedAnyFileTime = DateTime.Now;
            mModifiedFiles[file.FileName] = true;
        }

        /// <summary>
        /// For now, this is called periodically from the UI thread to recompile
        /// when something changes.  This is lame, so is temproary until we
        /// have proper background threading implemented.
        /// </summary>
        public void Timer()
        {
            if (mModifiedAnyFile && DateTime.Now > mModifiedAnyFileTime.AddMilliseconds(250))
                Build();
        }

        void Build()
        {
            // Get files to re-compile
            var filesToCompile = new string[mModifiedFiles.Keys.Count];
            mModifiedFiles.Keys.CopyTo(filesToCompile, 0);
            mModifiedFiles.Clear();
            mModifiedAnyFile = false;

            foreach (var file in filesToCompile)
            {
                TryParseText(file);
            }

            NotifyBuildChanged(Change.BuildDone);
        }

        void TryParseText(string file)
        {
            if (!mFiles.TryGetValue(file, out var buildFile))
                return;

            if (Debugger.IsAttached)
            {
                // Reset the lexer, re-parse, and compile
                ParseText(buildFile);
            }
            else
            {
                try
                {
                    // Reset the lexer, re-parse, and compile
                    ParseText(buildFile);
                }
                catch (Exception ex)
                {
                    // Go crazy, mark them all
                    foreach (var token in buildFile.Lexer)
                        if (token.Boln)
                            token.AddError("Compiler error: " + ex.Message);
                }
                buildFile.FileBuildVersion++;
            }
        }

        void ParseText(BuildFile buildFile)
        {
            // For the time being, we'll use the extension to decide
            // which parser to use.  TBD: This will be fixed later
            var ext = Path.GetExtension(buildFile.FileName).ToLower();
            if (ext == ".zurf")
            {
                // Parse text
                var t1 = DateTime.Now;
                var parser = new ZurfParse(buildFile.Lexer);
                var program = parser.Parse();

                // Generate Sil
                if (!parser.ParseError)
                {
                    // TBD: This will all be moved to a bild manager
                    var sil = new SilGenHeader(buildFile.FileName, program);
                    sil.GenerateTypeDefinitions();
                    sil.MergeTypeDefinitions();
                    sil.GenerateHeader();
                    sil.GenerateCode();

                    var silJson = new SilGenJson(sil.Package);
                    silJson.GenerateJson();
                }
                var t2 = DateTime.Now;
                var parseTime = t2 - t1;


                // Save parser generated tokens
                buildFile.SetExtraErrorTokensInternal(parser.ExtraTokens());
            }
            else if (ext == ".json")
            {
                var parser = new JsonParse(buildFile.Lexer);
                parser.Parse();
            }
        }

    }
}
