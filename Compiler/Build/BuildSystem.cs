using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;


using Gosub.Lex;
using Zurfur.Vm;
using Zurfur.Compiler;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Zurfur.Build;


public class BuildSystem
{
    /// <summary>
    /// Hard code for now
    /// </summary>
    public const string OUTPUT_DIR = "Output\\Debug";

    public int SLOW_DOWN_MS = 0;
    public delegate void UpdateEventHandler(object sender, UpdatedEventArgs e);
    static ScanSymbols s_scanZurf = new(ParseZurf.MULTI_CHAR_TOKENS);
    static ScanSymbols s_scanJson = new(ParseZurf.MULTI_CHAR_TOKENS);

    /// <summary>
    /// Output base directory to write all files (files are written to "Output/Debug" directory)
    /// </summary>
    public string OutputBaseDir { get; set; } = "";

    bool _isCompiling;
    TimeSpan _lexAndParseTime;
    FileSystemInterface _fileSystem;

    // Force full re-compile every time so we can see how long it takes
    bool FULL_RECOMPILE = false;
    
    public ThreadingModel Threading = ThreadingModel.Multi;

    Dictionary<string, FileInfo> _packageFiles = new ();

    // NOTE: Queue doesn't have RemoveAll and other List features.
    //       Make sure Zurfur Queue has all that.
    List<string> _loadQueue = new List<string>();
    List<string> _parseQueue = new List<string>();

    List<TaskCompletionSource<bool>> _compileDoneTasks = new List<TaskCompletionSource<bool>>();
    List<string> _report = new List<string>();
    string _headerJson = "";
    List<string> _codeJson = [];


    public string OutputDir => Path.Combine(OutputBaseDir, OUTPUT_DIR);
    public string OutputFileReport => Path.Combine(OutputBaseDir, OUTPUT_DIR, "BuildReport.txt");
    public string OutputFileHeader => Path.Combine(OutputBaseDir, OUTPUT_DIR, "Header.json");
    public string OutputFileHeaderCode => Path.Combine(OutputBaseDir, OUTPUT_DIR, "Code.zil");


    public BuildSystem(FileSystemInterface fileSystem)
    {
        _fileSystem = fileSystem;
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
    /// Starts loading the file.  Only call once per file, ever.
    /// </summary>
    public void LoadFile(string file)
    {
        if (_packageFiles.ContainsKey(file))
            throw new Exception($"LoadFile was already called on '{file}'");
        _packageFiles[file] = new FileInfo(file);
        _loadQueue.Add(file);
        TriggerCompile();
    }

    /// <summary>
    /// Returns the lexer
    /// NOTE: This returns NULL if the build manager has not yet loaded
    ///       the file or if the file is not in the package.
    /// </summary>
    public Lexer? GetLexer(string fileName)
    {
        if (!_packageFiles.TryGetValue(fileName, out var fi)
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
        if (!_packageFiles.TryGetValue(path, out var fileIno))
            throw new Exception("Cannot set Lexer, file is not in the project: " + path);
        if (fileIno.Lexer == null)
            throw new Exception("Cannot set Lexer, file is not loaded: " + path);
        fileIno.Lexer = lexer;
        _loadQueue.RemoveAll(match => match == path);
        _parseQueue.RemoveAll(match => match == path);
        _parseQueue.Insert(0, path);
        TriggerCompile();
    }

    /// <summary>
    /// Wait for compile to finish, then write files to disk
    /// </summary>
    public async Task GeneratePackage()
    {
        await Compile();
        await _fileSystem.WriteAllLinesAsync(OutputFileReport, _report);
        await _fileSystem.WriteAllLinesAsync(OutputFileHeader, [_headerJson]);
        await _fileSystem.WriteAllLinesAsync(OutputFileHeaderCode, _codeJson);

        // TBD: This is not so good
        _packageFiles.Remove(OutputFileReport);
        _packageFiles.Remove(OutputFileHeader);
        _packageFiles.Remove(OutputFileHeaderCode);
        LoadFile(OutputFileReport);
        LoadFile(OutputFileHeader);
        LoadFile(OutputFileHeaderCode);
    }


    // If in the process of building, the task completes when it is done.
    // Otherwise, trigger a new build.
    public Task Compile()
    {
        if (FULL_RECOMPILE)
            foreach (var fileName in _packageFiles.Keys)
                _loadQueue.Add(fileName);

        var tcs = new TaskCompletionSource<bool>();
        _compileDoneTasks.Add(tcs);
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
        if (_isCompiling)
            return;


        // Let it crash when running under debugger
        if (Debugger.IsAttached)
        {
            _isCompiling = true;
            await TryCompile();
            _isCompiling = false;
            return;
        }

        _isCompiling = true;
        try
        {
            await TryCompile();
        }
        catch (Exception ex)
        {
            // TBD: Send another message for popup with stack trace.
            // NOTE: The stack trace is stored in the error message
            //       associated with the token that caused the failure.
            Console.WriteLine($"Compiler failure: {ex.Message}");
            StatusUpdate?.Invoke(this, new UpdatedEventArgs("Compiler failure: " + ex.Message));
        }
        finally
        {
            _isCompiling = false;
        }
    }

    bool SourceCodeChanged => _loadQueue.Count != 0 || _parseQueue.Count != 0;

    private async Task TryCompile()
    {
        // Load, Lex, and Parse all files in the queue.
        while (SourceCodeChanged)
        {
            var timer = Stopwatch.StartNew();
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
            _lexAndParseTime = timer.Elapsed;
            await Generate();
        }

        foreach (var tcs in _compileDoneTasks)
            tcs.SetResult(true);
        _compileDoneTasks.Clear();
    }

    async Task LoadAndLex()
    {
        if (_loadQueue.Count == 0)
            return;
        var fi = _packageFiles[_loadQueue[0]];
        _loadQueue.RemoveAll(match => match == fi.Path);
        if (fi.Lexer != null && !FULL_RECOMPILE)
            return; // File already loaded

        // Choose lexer
        StatusUpdate?.Invoke(this, new UpdatedEventArgs("Loading " + fi.Name));
        await Task.Delay(SLOW_DOWN_MS);
        Lexer lexer;
        if (fi.Extension == ".zurf")
            lexer = new Lexer(s_scanZurf);
        else if (fi.Extension == ".json")
            lexer = new Lexer(s_scanJson);
        else if (fi.Extension == ".zil" || fi.Extension == ".txt")
            lexer = new Lexer();
        else
            return; // Unrecognized file extension

        // Since there is also CPU work, do it in a background thread
        var lines = await _fileSystem.ReadAllLinesAsync(fi.Path);
        await DoCpuWork(() =>
        {
            lexer.Path = fi.Path;
            lexer.Scan(lines);
        });

        // It could have been loaded before completing
        if (fi.Lexer != null && !FULL_RECOMPILE)
            return;

        fi.Lexer = lexer;
        _parseQueue.Add(fi.Path);
        FileUpdate?.Invoke(this, new UpdatedEventArgs(fi.Path));
        await ThreadingModelAwait();
        await Task.Delay(SLOW_DOWN_MS);
    }

    async Task Parse()
    {
        if (_parseQueue.Count == 0)
            return;
        var fi = _packageFiles[_parseQueue[0]];
        _parseQueue.RemoveAll(match => match == fi.Path);
        if (fi.Lexer == null)
            return;

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
                    lexer.Scanner = s_scanZurf;
                    fi.Parser = new ParseZurf(lexer);
                    fi.ParseErrors = fi.Parser.ParseErrors;
                    break;
                case ".json":
                    lexer.Scanner = s_scanJson;
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
        if (_parseQueue.Contains(fi.Path))
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

        var timerTotal = Stopwatch.StartNew();
        var timer = Stopwatch.StartNew();

        // Generate Header for each file (only ".zurf")
        var zurfFiles = new Dictionary<string, SyntaxFile>();
        foreach (var fi in _packageFiles)
        {
            if (fi.Value.Extension == ".zurf" && fi.Value.Parser != null)
            {
                // Reset all the metadata back to the post parse state (before compiling)
                fi.Value.Parser.ResetMetadata();
                zurfFiles[fi.Key] = fi.Value.Parser.Syntax;
            }
        }

        var noVerify = zurfFiles.Values.Any(f => f.Pragmas.ContainsKey("NoVerify"));
        var noCompilerChecks = zurfFiles.Values.Any(f => f.Pragmas.ContainsKey("NoCompilerChecks"));

        var timeClearTokens = timer.ElapsedMilliseconds;
        timer.Restart();


        // TBD: This needs to move to a background thread, but it can't
        // until we clone everything (Lexer, parse tree, etc.)
        var zilHeader = CompileHeader.GenerateHeader(zurfFiles, noCompilerChecks);
        if (!noVerify)
            VerifyHeader.Verify(zilHeader.Table);
        var timeGenHeader = timer.ElapsedMilliseconds;

        FileUpdate?.Invoke(this, new UpdatedEventArgs(""));
        await ThreadingModelAwait();

        // Abandon code generation when the source code changes
        if (SourceCodeChanged)
            return;

        StatusUpdate?.Invoke(this, new UpdatedEventArgs("Generating code"));
        await ThreadingModelAwait();

        timer.Restart();
        var assembly = CompileCode.GenerateCode(zurfFiles, zilHeader.Table, zilHeader.SyntaxToSymbol, zilHeader.Uses);

        if (!noVerify)
            VerifyCode.Verify(assembly, zilHeader.Table);
        var timeGenCode = timer.ElapsedMilliseconds;


        // NOTE: Tokens in symbol table should be stable, so can run in a background thread.
        timer.Restart();
        var lineNumbers = new List<int>();
        await DoCpuWork(() =>
        {
            // Package Header
            var package = new PackageJson();
            package.BuildDate = DateTime.Now.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            package.Symbols = zilHeader.Table.Save(false);

            try
            {
                _headerJson = JsonSerializer.Serialize(package);
            }
            catch
            {
                _headerJson = "JSON serialization not supported in the browser";
            }

            // Package Code
            var codeLines = new List<string>();
            var tracer = new AsTrace(assembly, zilHeader.Table);
            assembly.Print(tracer, codeLines, lineNumbers);
            _codeJson = codeLines;
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

        var timeGenPackage = timer.ElapsedMilliseconds;

        _report.Clear();
        _report.Add("Compile Times:");
        _report.Add($"    DATE: {DateTime.Now.ToString("s").Replace("T", " ")}");
        _report.Add($"    Lex and parse changed files: {_lexAndParseTime.TotalSeconds:F3} s");
        _report.Add($"    Clear tokens: {timeClearTokens} ms");
        _report.Add($"    Compile/verify header: {timeGenHeader} ms");
        _report.Add($"    Compile/verify code: {timeGenCode} ms");
        _report.Add($"    Generate header & code: {timeGenPackage} ms");
        _report.Add($"    Total: {timerTotal.Elapsed.TotalSeconds + _lexAndParseTime.TotalSeconds:F3} s");
        _report.Add($"    Memory: {(double)GC.GetTotalMemory(true) / 1000000:F2} Mb");
        _report.Add("");

        ZilReport.GenerateReport(_report, zilHeader.Table,
            _packageFiles.Values.Select(a => a.Lexer).Where(a => a != null).ToArray());

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
        foreach (var fi in _packageFiles)
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
        public ParseZurf? Parser;

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
