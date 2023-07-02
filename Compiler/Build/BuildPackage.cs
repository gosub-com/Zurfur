using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

using Zurfur.Lex;
using Zurfur.Jit;
using Zurfur.Compiler;
using System.Diagnostics;

namespace Zurfur.Build
{

    /// <summary>
    /// Build package is responsible for building all the files under
    /// a subdirectory.  Call Load, then GetLexer or SetLexer.
    /// Compiling happens in the background.  Catch StatusUpdate
    /// and FileUpdate to detect build status updates.
    /// </summary>
    public class BuildPackage
    {
        /// <summary>
        /// Hard code for now
        /// </summary>
        const string OUTPUT_DIR = "Output\\Debug";

        public int SLOW_DOWN_MS = 0;
        public delegate void UpdateEventHandler(object sender, UpdatedEventArgs e);
        static ScanZurf sScanZurf = new ScanZurf();
        static ScanZurf sScanJson = new ScanZurf();

        string mBaseDir = "";
        bool mIsCompiling;
        int mCompileCount;
        TimeSpan mLexAndParseTime;

        // Force full re-compile every time so we can see how long it takes
        bool FULL_RECOMPILE = false;
        bool SINGLE_THREADED = false;

        Dictionary<string, FileInfo> mPackageFiles = new Dictionary<string, FileInfo>();

        // NOTE: Queue doesn't have RemoveAll and other List features.
        //       Make sure Zurfur Queue has all that.
        List<string> mLoadQueue = new List<string>();
        List<string> mParseQueue = new List<string>();

        List<TaskCompletionSource<bool>> mCompileDoneTasks = new List<TaskCompletionSource<bool>>();
        List<string> mReport = new List<string>();
        string mHeaderJson = "";
        string mCodeJson = "";

        public string OutputDir => Path.Combine(mBaseDir, OUTPUT_DIR);
        public string OutputFileReport => Path.Combine(mBaseDir, OUTPUT_DIR, "BuildReport.txt");
        public string OutputFileHeader => Path.Combine(mBaseDir, OUTPUT_DIR, "Header.json");
        public string OutputFileHeaderCode => Path.Combine(mBaseDir, OUTPUT_DIR, "Code.zil");

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
        /// Build status - Load, Parse, Errors, Done, etc.
        /// </summary>
        public event UpdateEventHandler StatusUpdate;

        /// <summary>
        /// Lexer has been updated by compiler to convey new token information.
        /// Message contains the file name, or "" for all files updated.
        /// </summary>
        public event UpdateEventHandler FileUpdate;

        /// <summary>
        /// Starts loading the project into memory.  Only call once, ever.
        /// </summary>
        public void Load(string dir)
        {
            if (mBaseDir != "")
                throw new Exception("Not allowed to call 'Load' ever again");
            mBaseDir = dir;

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
        /// Throws exception if the file is not in the project or is not yet loaded.
        /// Use GetLexer() == null to check for that.
        /// </summary>
        public void SetLexer(string fileName, Lexer lexer)
        {
            if (!mPackageFiles.TryGetValue(fileName, out var fileIno))
                throw new Exception("Cannot set Lexer, file is not in the project: " + fileName);
            if (fileIno.Lexer == null)
                throw new Exception("Cannot set Lexer, file is not loaded: " + fileName);
            fileIno.Lexer = lexer;
            mLoadQueue.RemoveAll(match => match == fileName);
            mParseQueue.RemoveAll(match => match == fileName);
            mParseQueue.Insert(0, fileName);
            Compile();
        }

        /// <summary>
        /// Wait for compile to finish, then write files to disk
        /// </summary>
        public async Task GeneratePackage()
        {
            await ReCompile();
            if (!Directory.Exists(OutputDir))
                Directory.CreateDirectory(OutputDir);
            await Task.Run(() =>
            {
                File.WriteAllLines(OutputFileReport, mReport);
                File.WriteAllText(OutputFileHeader, mHeaderJson);
                File.WriteAllText(OutputFileHeaderCode, mCodeJson);
            });
        }


        // If in the process of building, the task completes when it is done.
        // Otherwise, trigger a new build.
        public Task ReCompile()
        {
            if (FULL_RECOMPILE)
                foreach (var fileName in mPackageFiles.Keys)
                    mLoadQueue.Add(fileName);

            var tcs = new TaskCompletionSource<bool>();
            mCompileDoneTasks.Add(tcs);
            Compile();
            return tcs.Task;
        }

        /// <summary>
        /// Called whenever anything changes.  NOTE: Doesn't start a new
        /// build if in the process of compiling, but the compiler will
        /// restart the build at the end if there are changes.
        /// </summary>
        async void Compile()
        {
            if (mIsCompiling)
                return;


            // Let it crash when running under debugger
            if (Debugger.IsAttached)
            {
                mIsCompiling = true;
                await TryCompile();
                mIsCompiling = false;
                return;
            }

            mIsCompiling = true;
            try
            {
                await TryCompile();
            }
            catch (Exception ex)
            {
                // TBD: Send another message for popup with stack trace.
                // NOTE: The stack trace is stored in the error message
                //       associated with the token that caused the failure.
                StatusUpdate?.Invoke(this, new UpdatedEventArgs("Compiler failure: " + ex.Message));
            }
            finally
            {
                mIsCompiling = false;
            }
        }

        bool SourceCodeChanged => mLoadQueue.Count != 0 || mParseQueue.Count != 0;

        private async Task TryCompile()
        {
            // Load, Lex, and Parse all files in the queue.
            while (SourceCodeChanged)
            {
                var lexStartTime = DateTime.Now;
                while (SourceCodeChanged)
                {
                    if (SINGLE_THREADED)
                    {
                        // Single threaded full re-compile (for timing)
                        await LoadAndLex();
                        await Parse();
                    }
                    else
                    {
                        // Allow load and parse to run concurrently since
                        // one is mostly IO and the other mostly CPU.
                        // Other than that, we won't try to multi-task for now.
                        var loadTask = LoadAndLex();
                        var parseTask = Parse();
                        await loadTask;
                        await parseTask;
                    }
                }
                mLexAndParseTime = DateTime.Now - lexStartTime;
                await Generate();
            }

            foreach (var tcs in mCompileDoneTasks)
                tcs.SetResult(true);
            mCompileDoneTasks.Clear();
        }

        async Task LoadAndLex()
        {
            if (mLoadQueue.Count == 0)
                return;
            var fi = mPackageFiles[mLoadQueue[0]];
            mLoadQueue.RemoveAll(match => match == fi.Path);
            if (fi.Lexer != null && !FULL_RECOMPILE)
                return; // File already loaded

            // Choose lexer
            StatusUpdate?.Invoke(this, new UpdatedEventArgs("Loading " + fi.Name));
            await Task.Delay(SLOW_DOWN_MS);
            Lexer lexer;
            if (fi.Extension == ".zurf")
                lexer = new Lexer(sScanZurf);
            else if (fi.Extension == ".json")
                lexer = new Lexer(sScanJson);
            else if (fi.Extension == ".zil")
                lexer = new Lexer();
            else
                return; // Unrecognized file extension

            // Since there is also CPU work, do it in a background thread
            await Task.Run(() =>
            {
                var lines = File.ReadAllLines(fi.Path);
                lexer.Path = fi.Path;
                lexer.Scan(lines);
            });

            // It could have been loaded before completing
            if (fi.Lexer != null && !FULL_RECOMPILE)
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
            mParseQueue.RemoveAll(match => match == fi.Path);

            await Task.Delay(SLOW_DOWN_MS / 2);
            StatusUpdate?.Invoke(this, new UpdatedEventArgs("Parsing " + fi.Name));
            await Task.Delay(SLOW_DOWN_MS / 2);

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
                    case ".zil":
                        var zilParse = new ParseZil(lexer);
                        zilParse.Parse();
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
            // Abandon code generation when the source code changes
            if (SourceCodeChanged)
                return;

            await Task.Delay(SLOW_DOWN_MS);
            StatusUpdate?.Invoke(this, new UpdatedEventArgs("Compiling headers"));

            var dtStart = DateTime.Now;

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
            var noVerify = false;
            var noCompilerChecks = false;
            foreach (var fi in zurfFiles)
            {
                foreach (var token in fi.Value.Lexer)
                    RemoveZilInfo(token);
                foreach (var token in fi.Value.Lexer.MetaTokens)
                    RemoveZilInfo(token);
                RemoveZilInfo(fi.Value.Lexer.EndToken);

                if (fi.Value.Pragmas.ContainsKey("NoVerify"))
                    noVerify = true;
                if (fi.Value.Pragmas.ContainsKey("NoCompilerChecks"))
                    noCompilerChecks = true;
            }
            var dtEndClearTokens = DateTime.Now;


            // TBD: This needs to move to a background thread, but it can't
            // until we clone everything (Lexer, parse tree, etc.)
            var dtStartGenHeader = DateTime.Now;
            var zilHeader = CompileHeader.GenerateHeader(zurfFiles, noCompilerChecks);
            if (!noVerify)
                VerifyHeader.Verify(zilHeader.Table);
            var dtEndGenHeader = DateTime.Now;

            FileUpdate(this, new UpdatedEventArgs(""));

            // Abandon code generation when the source code changes
            if (SourceCodeChanged)
                return;

            StatusUpdate?.Invoke(this, new UpdatedEventArgs("Generating code"));
            //await Task.Delay(1); // Allow time for UI to update


            // TBD: This should also move to a background thread.
            var dtStartGenCode = DateTime.Now;
            var asFuns = CompileCode.GenerateCode(zurfFiles, zilHeader.Table, zilHeader.SyntaxToSymbol, zilHeader.Uses);
            var dtEndGenCode = DateTime.Now;


            // NOTE: Tokens in symbol table should be stable, so can run in a background thread.
            var dtStartGenPackage = DateTime.Now;
            await Task.Run(() =>
            {
                // Header
                var package = new PackageJson();
                package.BuildDate = DateTime.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                package.Symbols = zilHeader.Table.Save(false);
                mHeaderJson = JsonSerializer.Serialize(package);

                // Code
                var sb = new StringBuilder();
                asFuns.Print(sb);
                mCodeJson = sb.ToString();
            });
            var dtEndGenPackage = DateTime.Now;

            mReport.Clear();
            mReport.Add("Compile Times:");
            mReport.Add($"    DATE: {DateTime.Now.ToString("s").Replace("T", " ")}");
            mReport.Add($"    Lex and parse changed files: {mLexAndParseTime.TotalSeconds:F3}");
            mReport.Add($"    Clear tokens: {(dtEndClearTokens - dtStart).TotalSeconds:F3}");
            mReport.Add($"    Compile/verify header: {(dtEndGenHeader - dtStartGenHeader).TotalSeconds:F3}");
            mReport.Add($"    Compile/verify code: {(dtEndGenCode - dtStartGenCode).TotalSeconds:F3}");
            mReport.Add($"    Generate package: {(dtEndGenPackage - dtStartGenPackage).TotalSeconds:F3}");
            mReport.Add($"    Total: {(dtEndGenPackage - dtStart).TotalSeconds + mLexAndParseTime.TotalSeconds:F3}");
            mReport.Add($"    Memory: {(double)GC.GetTotalMemory(true)/1000000:F2} Mb");
            mReport.Add("");

            ZilReport.GenerateReport(mReport, zilHeader.Table,
                mPackageFiles.Values.Select(a => a.Lexer).Where(a => a != null).ToArray());

            FileUpdate(this, new UpdatedEventArgs(""));
            int errors = CountErrors();
            if (errors != 0)
            {
                StatusUpdate?.Invoke(this, new UpdatedEventArgs("ERROR: " + errors + " errors"));
                return;
            }
            StatusUpdate?.Invoke(this, new UpdatedEventArgs("Done"));
        }

        private static void RemoveZilInfo(Token token)
        {
            token.RemoveInfo<Symbol>();
            token.RemoveInfo<VerifySuppressError>();
            token.RemoveInfo<ZilCompileError>();
            token.RemoveInfo<VerifyError>();
            token.RemoveInfo<ZilWarn>();
            token.RemoveInfo<string>();
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
