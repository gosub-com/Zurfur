using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Gosub.Zurfur.Lex;
using Gosub.Zurfur.Compiler;


namespace Gosub.Zurfur.Build
{
    /// <summary>
    /// The builder will eventually load the files from the filesystem
    /// and compile them in background threads.  It can then be handed
    /// a lexer from an editor which will override the one on the
    /// file system.  
    /// </summary>
    class BuildManager
    {
        object mLock = new object();
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
        /// Returns a file in the build system, or NULL if it is not already there.
        /// `LoadFile` should be used the first time, then GetFile since it
        /// is already loaded.
        /// </summary>
        public BuildFile GetFile(string fileName)
        {
            lock (mLock)
            {
                mFiles.TryGetValue(fileName, out var file);
                return file;
            }
        }


        /// <summary>
        /// Loads a file into the build system if it is not already there.
        /// Re-loads the file if it is stale.  Returns NULL if this isn't
        /// a file the build system cares about.
        /// </summary>
        public async Task<BuildFile> LoadFileAsync(string path)
        {
            path = FixPathName(path);
            var ext = Path.GetExtension(path).ToLower();

            // Ignore unknown files for now
            if (ext != ".zurf" && ext != ".json")
                return null;

            lock (mLock)
            {
                // See if we already have it, and it's up to date
                var fi = new FileInfo(path);
                fi.Refresh(); // Seems to be necessary to get latest info
                if (mFiles.TryGetValue(path, out var buildFile1))
                {
                    if (fi.LastWriteTimeUtc == buildFile1.LastWriteTimeUtc)
                        return buildFile1;
                }
            }

            // Load and lex in background thread
            var buildFile = await Task.Run(() =>
            {
                var lex = new Lexer(new ScanZurf());

                lex.ScanLines(File.ReadAllLines(path));

                lock (mLock)
                {
                    var fi = new FileInfo(path);
                    fi.Refresh(); // Seems to be necessary to get latest info
                    if (mFiles.TryGetValue(path, out var buildFile2))
                    {
                        if (fi.LastWriteTimeUtc == buildFile2.LastWriteTimeUtc)
                            return buildFile2;  //  Another thread got here first
                    }
                    else
                    {
                        buildFile2 = new BuildFile(path, this);
                        mFiles[path] = buildFile2;
                    }

                    buildFile2.Lexer = lex; // No need for clone, lexer is transferred to UI thread
                    buildFile2.LastWriteTimeUtc = fi.LastWriteTimeUtc;
                    return buildFile2;
                }
            });
            return buildFile;
        }


        public static string FixPathName(string pathName)
        {
            pathName = Path.GetFullPath(pathName);
            if (!(File.Exists(pathName) || Directory.Exists(pathName)))
                return pathName;
            return FixPathName(new DirectoryInfo(pathName));
        }

        public static string FixPathName(DirectoryInfo di)
        {
            if (di.Parent == null)
                return di.Name.ToUpper();

            return Path.Combine(FixPathName(di.Parent),
                                di.Parent.GetFileSystemInfos(di.Name)[0].Name);
        }


        /// <summary>
        /// Closes the file being edited
        /// </summary>
        public void CloseFile(string fileName)
        {
            // For now just hold it in memory forever since it's probably part of the build
            // if (GetFile(fileName) != null)
            //    LoadFile(fileName);
        }

        /// <summary>
        /// Task completes whenever a build phase has changed, or the build is complete.
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

            // Allow safe parsing in a backgroudn thread
            var lexer = buildFile.Lexer.Clone();

            if (Debugger.IsAttached)
            {
                // Reset the lexer, re-parse, and compile
                ParseText(buildFile, lexer);
            }
            else
            {
                try
                {
                    // Reset the lexer, re-parse, and compile
                    ParseText(buildFile, lexer);
                }
                catch (Exception ex)
                {
                    // Go crazy, mark them all
                    foreach (var token in lexer)
                        if (token.Boln)
                            token.AddError("Compiler error: " + ex.Message);
                }
                buildFile.FileBuildVersion++;
            }

            // Send meta tokens back to original lexer
            buildFile.Lexer.MetaTokens = lexer.MetaTokens;
        }

        void ParseText(BuildFile buildFile, Lexer lexer)
        {
            // For the time being, we'll use the extension to decide
            // which parser to use.  TBD: This will be fixed later
            var ext = Path.GetExtension(buildFile.FileName).ToLower();
            if (ext == ".zurf")
            {
                // Parse text
                var t1 = DateTime.Now;
                var parser = new ParseZurf(lexer);
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
                }
                var t2 = DateTime.Now;
                var parseTime = t2 - t1;
            }
            else if (ext == ".json")
            {
                var parser = new ParseJson(lexer);
                parser.Parse();
            }
        }

    }
}
