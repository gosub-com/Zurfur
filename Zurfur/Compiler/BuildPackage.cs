using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

using Gosub.Zurfur.Compiler;
using Gosub.Zurfur.Lex;
using System.Diagnostics;
using System.Windows.Forms;
using System.Reflection;
using System.Data;
using System.CodeDom.Compiler;

namespace Gosub.Zurfur.Compiler
{

    /// <summary>
    /// Build package is responsible for building all the files under
    /// a subdirectory.  Call Load, then GetLexer or SetLexer.
    /// Compiling happens in the background.  Catch StatusUpdate
    /// and FileUpdate to detect build status updates.
    /// </summary>
    public class BuildPackage
    {
        const string OUTPUT_DIR = "Output\\Debug";

        public int SLOW_DOWN_MS = 0;
        public delegate void UpdateEventHandler(object sender, UpdatedEventArgs e);

        /// <summary>
        /// Build status - Load, Parse, Errors, Done, etc.
        /// </summary>
        public event UpdateEventHandler StatusUpdate;

        /// <summary>
        /// Lexer has been updated by compiler to convey new token information.
        /// Message contains the file name, or "" for all files updated.
        /// </summary>
        public event UpdateEventHandler FileUpdate;

        string mBaseDir = "";
        string mOutputDir = "";
        bool mIsCompiling;

        static ScanZurf sScanZurf = new ScanZurf();
        static ScanZurf sScanJson = new ScanZurf();

        Dictionary<string, FileInfo> mPackageFiles = new Dictionary<string, FileInfo>();

        // NOTE: Queue doesn't have RemoveAll and other List features.
        //       Make sure Zurfur has all that.
        List<string> mLoadQueue = new List<string>();
        List<string> mParseQueue = new List<string>();

        /// <summary>
        /// For status, Message: Build step (Loading, Parsing, Linking, etc.)
        /// For file, Message has name of updated file, or "" for all
        /// </summary>
        public class UpdatedEventArgs : EventArgs
        {
            public readonly string Message = "";
            public UpdatedEventArgs(string message) 
            { 
                Message = message; 
            }
        }

        /// <summary>
        /// Starts loading the project into memory.  Only call once, ever.
        /// </summary>
        public void Load(string dir)
        {
            if (mBaseDir != "")
                throw new Exception("Not allowed to call 'Load' ever again");
            mBaseDir = dir;
            mOutputDir = Path.Combine(mBaseDir, OUTPUT_DIR);

            var projectFiles = new List<FileInfo>();
            EnumerateDir(FixPathName(dir), projectFiles);

            foreach (var file in projectFiles)
            {
                mPackageFiles[file.Path] = file;
                mLoadQueue.Add(file.Path);
            }
            Compile();
        }

        /// <summary>
        /// Returns the lexer
        /// NOTE: This returns NULL if the build manager has not yet loaded
        ///       the file or if the file is not in the package.
        /// </summary>
        public Lexer GetLexer(string fileName)
        {
            if (!mPackageFiles.TryGetValue(fileName, out var fi)
                || fi.Lexer == null)
            {
                return null;
            }
            return fi.Lexer;
        }

        /// <summary>
        /// Sets the lexer, ovrriding the file on disk and triggers a new build.
        /// </summary>
        public void SetLexer(string fileName, Lexer lexer)
        {
            if (!mPackageFiles.TryGetValue(fileName, out var fileIno))
                return;
            fileIno.Lexer = lexer;
            mLoadQueue.RemoveAll(match => match == fileName);
            mParseQueue.RemoveAll(match => match == fileName);
            mParseQueue.Insert(0, fileName);
            Compile();
        }

        /// <summary>
        /// Called whenever anything changes.
        /// </summary>
        async void Compile()
        {
            try
            {
                if (mIsCompiling)
                    return;
                mIsCompiling = true;

                await TryCompile();

            }
            catch (Exception ex)
            {
                mIsCompiling = false;
                StatusUpdate?.Invoke(this, new UpdatedEventArgs("Compiler failure: " + ex.Message));
                Debug.WriteLine("Compiler failure: " + ex.Message + "\r\n" + ex.StackTrace);
                if (Debugger.IsAttached)
                    MessageBox.Show("Error: " + ex.Message + "\r\n\r\n" + ex.StackTrace, "Zurfur");
            }
            finally
            {
                mIsCompiling = false;
            }
        }

        private async Task TryCompile()
        {
            // Load, Lex, and Parse all files in the queue
            while (mLoadQueue.Count != 0 || mParseQueue.Count != 0)
            {
                // Allow load and parse to run concurrently since
                // one is mostly IO and the other mostly CPU.
                // Other than that, we won't try to multi-task for now.
                var loadTask = LoadAndLex();
                var parseTask = Parse();
                await loadTask;
                await parseTask;
            }

            if (mLoadQueue.Count == 0 && mParseQueue.Count == 0)
                await Generate();
        }

        async Task LoadAndLex()
        {
            if (mLoadQueue.Count == 0)
                return;
            var fi = mPackageFiles[mLoadQueue[0]];
            mLoadQueue.RemoveAll(match => match == fi.Path);
            if (fi.Lexer != null)
                return; // File already loaded

            // Choose lexer (for now, the project doesn't include anything but .zurf and .json
            StatusUpdate?.Invoke(this, new UpdatedEventArgs("Loading " + fi.Name));
            await Task.Delay(SLOW_DOWN_MS);
            Lexer lexer;
            if (fi.Extension == ".zurf")
                lexer = new Lexer(sScanZurf);
            else if (fi.Extension == ".json")
                lexer = new Lexer(sScanJson);
            else
                return; // Unrecognized file extension
                
            // Since there is also CPU work, do it in a background thread
            await Task.Run(() => 
            {
                var lines = File.ReadAllLines(fi.Path);
                lexer.Scan(lines);
            });

            // It could have been loaded before completing
            if (fi.Lexer != null)
                return;

            fi.Lexer = lexer;
            mParseQueue.Add(fi.Path);
            FileUpdate?.Invoke(this, new UpdatedEventArgs(fi.Path));
            await Task.Delay(SLOW_DOWN_MS);
        }

        async Task Parse()
        {
            if (mParseQueue.Count == 0)
                return;
            var fi = mPackageFiles[mParseQueue[0]];
            mParseQueue.RemoveAll( match => match == fi.Path);
            if (fi.Extension != ".zurf" && fi.Extension != ".json")
                return;

            await Task.Delay(SLOW_DOWN_MS/2);
            StatusUpdate?.Invoke(this, new UpdatedEventArgs("Parsing " + fi.Name));
            await Task.Delay(SLOW_DOWN_MS/2);

            // Clone before processing in background thread
            var lexer = fi.Lexer.Clone();
            await Task.Run(() => 
            {
                switch (fi.Extension)
                {
                    case ".zurf":
                        lexer.Scanner = sScanZurf;
                        var zurfParse = new ParseZurf(lexer);
                        fi.Syntax = zurfParse.Parse();
                        fi.ParseErrors = zurfParse.ParseErrors;
                        break;
                    case ".json":
                        lexer.Scanner = sScanJson;
                        var jsonParse = new ParseJson(lexer);
                        jsonParse.Parse();
                        fi.ParseErrors = jsonParse.ParseErrors;
                        break;
                }
            });

            // If requested to compile again, throw away the intermediate results
            if (mParseQueue.Contains(fi.Path))
                return;

            fi.Lexer = lexer;
            FileUpdate?.Invoke(this, new UpdatedEventArgs(fi.Path));
            await Task.Delay(SLOW_DOWN_MS);
        }

        async Task Generate()
        {
            await Task.Delay(SLOW_DOWN_MS);

            var dt1 = DateTime.Now;

            // Generate Header for each file (only ".zurf")
            var zurfFiles = new Dictionary<string, SyntaxFile>();
            foreach (var fi in mPackageFiles)
                if (fi.Value.Extension == ".zurf")
                    zurfFiles[fi.Key] = fi.Value.Syntax;

            // NOTE: We should probably clone everything here, but that would have
            //       to include the lexer and the syntax tree generated by the parser.
            //       Alternatively, we could separate the token metadata from the
            //       lexer, thereby allowing us to keep the lexer and syntax tree
            //       immutable.  That would probably be the best thing to do.
            // INSTEAD: Clear all the metadata generated in this phase (yucky)
            foreach (var fi in zurfFiles)
            {
                foreach (var token in fi.Value.Lexer)
                {
                    if (token.Error)
                    {
                        token.RemoveInfo<Symbol>();
                        token.RemoveInfo<SilError>();
                        if (token.GetInfo<TokenError>() == null)
                            token.Error = false;
                    }
                }
            }

            var dt2 = DateTime.Now;

            // TBD: Move to background thread (clone Lexer, parse tree, etc.)
            var sil = new SilGenHeader();
            sil.EnumerateSymbols(zurfFiles);
            sil.ResolveTypeNames();
            sil.GenerateHeader(mOutputDir);

            var dt3 = DateTime.Now;
            var ts1 = dt2 - dt1;
            var ts2 = dt3 - dt2;


            FileUpdate(this, new UpdatedEventArgs(""));

            int errors = CountErrors();
            if (errors != 0)
            {
                StatusUpdate?.Invoke(this, new UpdatedEventArgs("ERROR: " + errors + " errors"));
                return;
            }
            StatusUpdate?.Invoke(this, new UpdatedEventArgs("Done"));
        }

        int CountErrors()
        {
            int errors = 0;
            foreach (var fi in mPackageFiles)
            {
                if (fi.Value.Lexer == null)
                    continue;
                foreach (var token in fi.Value.Lexer)
                    if (token.Error)
                        errors++;
                foreach (var token in fi.Value.Lexer.MetaTokens)
                    if (token.Error)
                        errors++;
            }
            return errors;
        }

        /// <summary>
        /// Fix path name to match capitalization of file system
        /// </summary>
        public static string FixPathName(string pathName)
        {
            string FixPathName(DirectoryInfo di)
            {
                if (di.Parent == null)
                    return di.Name.ToUpper();

                return Path.Combine(FixPathName(di.Parent),
                                    di.Parent.GetFileSystemInfos(di.Name)[0].Name);
            }

            pathName = Path.GetFullPath(pathName);
            if (!(File.Exists(pathName) || Directory.Exists(pathName)))
                return pathName;
            return FixPathName(new DirectoryInfo(pathName));
        }
        
        void EnumerateDir(string dir, List<FileInfo> package)
        {
            if (!Directory.Exists(dir))
                return;
            foreach (var file in Directory.GetFiles(dir))
                package.Add(new FileInfo(file));
            foreach (var d in Directory.GetDirectories(dir))
                EnumerateDir(d, package);
        }


        class FileInfo
        {
            public string Path;         // Full path name
            public string Name;         // Name without ath
            public string Extension;    // Always lower case
            public Lexer Lexer;
            public int ParseErrors;
            public SyntaxFile Syntax;

            public FileInfo(string path)
            {
                Path = path;
                Name = System.IO.Path.GetFileName(path);
                Extension = System.IO.Path.GetExtension(Path).ToLower();
            }
            public override string ToString()
            {
                return System.IO.Path.GetFileName(Path);
            }

        }

    }
}
