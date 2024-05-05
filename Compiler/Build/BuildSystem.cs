using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;


using Gosub.Lex;
using Zurfur.Jit;
using Zurfur.Compiler;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Zurfur.Build;


public class BuildSystem
{
    /// <summary>
    /// Hard code for now
    /// </summary>
    const string OUTPUT_DIR = "Output\\Debug";

    public int SLOW_DOWN_MS = 0;
    public delegate void UpdateEventHandler(object sender, UpdatedEventArgs e);
    static ScanSymbols sScanZurf = new(ParseZurf.MULTI_CHAR_TOKENS);
    static ScanSymbols sScanJson = new(ParseZurf.MULTI_CHAR_TOKENS);

    string mBaseDir = "";
    bool mIsCompiling;
    TimeSpan mLexAndParseTime;
    FileSystemInterface mFileSystem;

    // Force full re-compile every time so we can see how long it takes
    bool FULL_RECOMPILE = false;
    
    
    public ThreadingModel Threading = ThreadingModel.Multi;

    public bool DisableVerificationAndReports = false;

    Dictionary<string, FileInfo> mPackageFiles = new ();

    // NOTE: Queue doesn't have RemoveAll and other List features.
    //       Make sure Zurfur Queue has all that.
    List<string> mLoadQueue = new List<string>();
    List<string> mParseQueue = new List<string>();

    List<TaskCompletionSource<bool>> mCompileDoneTasks = new List<TaskCompletionSource<bool>>();
    List<string> mReport = new List<string>();
    string mHeaderJson = "";
    List<string> mCodeJson = [];

    public string OutputDir => Path.Combine(mBaseDir, OUTPUT_DIR);
    public string OutputFileReport => Path.Combine(mBaseDir, OUTPUT_DIR, "BuildReport.txt");
    public string OutputFileHeader => Path.Combine(mBaseDir, OUTPUT_DIR, "Header.json");
    public string OutputFileHeaderCode => Path.Combine(mBaseDir, OUTPUT_DIR, "Code.zil");


    public BuildSystem(FileSystemInterface fileSystem)
    {
        mFileSystem = fileSystem;
    }

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
    public event UpdateEventHandler? StatusUpdate;

    /// <summary>
    /// Lexer has been updated by compiler to convey new token information.
    /// Message contains the file name, or "" for all files updated.
    /// </summary>
    public event UpdateEventHandler? FileUpdate;

    public enum ThreadingModel
    {
        Multi,
        Single,         
        SingleAwait
    }

    // Allow single or multi-threaded operation
    async Task DoCpuWork(Action a)
    {
        switch (Threading)
        {
            default:
            case ThreadingModel.Single:
                a.Invoke();
                break;
            case ThreadingModel.SingleAwait:
                a.Invoke();
                await Task.Delay(1);
                break;
            case ThreadingModel.Multi:
                await Task.Run(a);
                break;
        }
    }


    /// <summary>
    /// Starts loading the project into memory.  Only call once, ever.
    /// Sets the output base directory.
    /// </summary>
    public void Load(string dir)
    {
        if (mBaseDir != "")
            throw new Exception("Not allowed to call 'Load' ever again");
        mBaseDir = dir;

        foreach (var file in FileSystemOs.EnumerateAllFiles(dir))
            LoadFile(file);
    }

    /// <summary>
    /// Starts loading the file.  Only call once per file, ever.
    /// </summary>
    public void LoadFile(string file)
    {
        if (mPackageFiles.ContainsKey(file))
            throw new Exception($"LoadFile was already called on '{file}'");
        mPackageFiles[file] = new FileInfo(file);
        mLoadQueue.Add(file);
        TriggerCompile();
    }

    /// <summary>
    /// Returns the lexer
    /// NOTE: This returns NULL if the build manager has not yet loaded
    ///       the file or if the file is not in the package.
    /// </summary>
    public Lexer? GetLexer(string fileName)
    {
        if (!mPackageFiles.TryGetValue(fileName, out var fi)
            || fi.Lexer == null)
        {
            return null;
        }
        return fi.Lexer;
    }

    /// <summary>
    /// Sets the lexer, overriding the file on disk and triggers a new build.
    /// Throws exception if the file is not in the project or is not yet loaded.
    /// Use GetLexer() == null to check for that.
    /// </summary>
    public void SetLexer(Lexer lexer)
    {
        var path = lexer.Path;
        if (!mPackageFiles.TryGetValue(path, out var fileIno))
            throw new Exception("Cannot set Lexer, file is not in the project: " + path);
        if (fileIno.Lexer == null)
            throw new Exception("Cannot set Lexer, file is not loaded: " + path);
        fileIno.Lexer = lexer;
        mLoadQueue.RemoveAll(match => match == path);
        mParseQueue.RemoveAll(match => match == path);
        mParseQueue.Insert(0, path);
        TriggerCompile();
    }

    /// <summary>
    /// Wait for compile to finish, then write files to disk
    /// </summary>
    public async Task GeneratePackage()
    {
        await Compile();
        if (!Directory.Exists(OutputDir))
            Directory.CreateDirectory(OutputDir);
        await mFileSystem.WriteAllLinesAsync(OutputFileReport, mReport);
        await mFileSystem.WriteAllLinesAsync(OutputFileHeader, [mHeaderJson]);
        await mFileSystem.WriteAllLinesAsync(OutputFileHeaderCode, mCodeJson);
    }


    // If in the process of building, the task completes when it is done.
    // Otherwise, trigger a new build.
    public Task Compile()
    {
        if (FULL_RECOMPILE)
            foreach (var fileName in mPackageFiles.Keys)
                mLoadQueue.Add(fileName);

        var tcs = new TaskCompletionSource<bool>();
        mCompileDoneTasks.Add(tcs);
        TriggerCompile();
        return tcs.Task;
    }

    /// <summary>
    /// Called whenever anything changes.  NOTE: Doesn't start a new
    /// build if in the process of compiling, but the compiler will
    /// restart the build at the end if there are changes.
    /// </summary>
    async void TriggerCompile()
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
                // Allow load and parse to run concurrently since
                // one is mostly IO and the other mostly CPU.
                // Other than that, we won't try to multi-task for now.
                var loadTask = LoadAndLex();
                var parseTask = Parse();
                await loadTask;
                await parseTask;
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
        var lines = await mFileSystem.ReadAllLinesAsync(fi.Path);
        await DoCpuWork(() =>
        {
            lexer.Path = fi.Path;
            lexer.Scan(lines);
        });

        // It could have been loaded before completing
        if (fi.Lexer != null && !FULL_RECOMPILE)
            return;

        fi.Lexer = lexer;
        mParseQueue.Add(fi.Path);
        FileUpdate?.Invoke(this, new UpdatedEventArgs(fi.Path));
        await ThreadingModelAwait();
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
        await DoCpuWork(() =>
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
        await ThreadingModelAwait();
        await Task.Delay(SLOW_DOWN_MS);
    }

    async Task Generate()
    {
        // Abandon code generation when the source code changes
        if (SourceCodeChanged)
            return;

        await Task.Delay(SLOW_DOWN_MS);
        StatusUpdate?.Invoke(this, new UpdatedEventArgs("Compiling headers"));
        await ThreadingModelAwait();

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

        FileUpdate?.Invoke(this, new UpdatedEventArgs(""));
        await ThreadingModelAwait();

        // Abandon code generation when the source code changes
        if (SourceCodeChanged)
            return;

        StatusUpdate?.Invoke(this, new UpdatedEventArgs("Generating code"));
        await ThreadingModelAwait();

        var dtStartGenCode = DateTime.Now;
        var assembly = CompileCode.GenerateCode(zurfFiles, zilHeader.Table, zilHeader.SyntaxToSymbol, zilHeader.Uses);

        if (DisableVerificationAndReports)
        {
            UpdateCompileStatus();
            return;
        }

        if (!noVerify)
            VerifyCode.Verify(assembly, zilHeader.Table);
        var dtEndGenCode = DateTime.Now;


        // NOTE: Tokens in symbol table should be stable, so can run in a background thread.
        var dtStartGenPackage = DateTime.Now;
        var lineNumbers = new List<int>();
        await DoCpuWork(() =>
        {
            // Header
            var package = new PackageJson();
            package.BuildDate = DateTime.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            package.Symbols = zilHeader.Table.Save(false);
            mHeaderJson = JsonSerializer.Serialize(package);

            // Code
            var codeLines = new List<string>();
            var tracer = new AsTrace(assembly, zilHeader.Table);
            assembly.Print(tracer, codeLines, lineNumbers);
            mCodeJson = codeLines;
        });

        // Show verification errors in zil code
        var codeLexer = GetLexer(OutputFileHeaderCode);
        if (codeLexer != null)
        {
            foreach (var error in assembly.Errors)
            {
                if (error.OpIndex < lineNumbers.Count)
                {
                    // TBD: The user can edit the .ZIL files,
                    //      so errors can get out of sync
                    if (error.OpIndex < codeLexer.LineCount)
                    {
                        var tokens = codeLexer.GetLineTokens(lineNumbers[error.OpIndex]);
                        if (tokens.Length != 0)
                            tokens[0].AddError(new VerifyError(error.ErrorMessage));
                    }
                }
            }
        }

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
        mReport.Add($"    Memory: {(double)GC.GetTotalMemory(true) / 1000000:F2} Mb");
        mReport.Add("");

        ZilReport.GenerateReport(mReport, zilHeader.Table,
            mPackageFiles.Values.Select(a => a.Lexer).Where(a => a != null).ToArray());

        UpdateCompileStatus();
    }

    private async void UpdateCompileStatus()
    {
        FileUpdate?.Invoke(this, new UpdatedEventArgs(""));
        int errors = CountErrors();
        if (errors != 0)
        {
            StatusUpdate?.Invoke(this, new UpdatedEventArgs("ERROR: " + errors + " errors"));
            return;
        }
        StatusUpdate?.Invoke(this, new UpdatedEventArgs("Done"));
        await ThreadingModelAwait();
    }

    /// <summary>
    /// Allow short wait for GUI to update when doing ThreadingModel.SingleAwait
    /// </summary>
    async Task ThreadingModelAwait()
    {
        if (Threading == ThreadingModel.SingleAwait)
            await Task.Delay(1);
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


    class FileInfo
    {
        public string Path;         // Full path name
        public string Name;         // Name without extension
        public string Extension;    // Always lower case
        public Lexer? Lexer;
        public int ParseErrors;
        public SyntaxFile? Syntax;

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
