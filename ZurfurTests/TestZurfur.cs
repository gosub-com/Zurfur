using Zurfur;
using Zurfur.Build;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Zurfur.Tests;

public class TestZurfur
{
    readonly ITestOutputHelper _output;

    public TestZurfur(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Check that each test case (separated by ###) in 
    /// AATestFails.zurf has exactly one error
    /// </summary>
    [Fact]
    public async Task TestAATestFails()
    {
        var exeDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!;
        var testProjectDir = Path.Combine(exeDir, "ZurfurLib");
        var builder = new BuildSystem(new FileSystemOs());
        Debug.WriteLine("Starting...");

        // Load and compile first
        var timer = Stopwatch.StartNew();
        builder.Load(testProjectDir);            
        await builder.Compile();
        var compileTime1 = timer.ElapsedMilliseconds;

        // Remove pragmas
        var lexerName = Path.Combine(testProjectDir, "AATestFails.zurf");
        var lexer = builder.GetLexer(lexerName);
        Assert.NotNull(lexer);

        for (int y = 0; y < lexer.LineCount; y++)
            if (lexer.GetLine(y).Contains("pragma"))
                lexer.ReplaceText([], new(0, y), new(1000, y));

        // Recompile without pragmas
        timer = Stopwatch.StartNew();
        builder.SetLexer(lexer);
        await builder.Compile();
        Debug.WriteLine($"Load and Compile in {compileTime1} ms, recompile in {timer.ElapsedMilliseconds} ms");

        // Scan that each test case contains exactly one error
        lexer = builder.GetLexer(lexerName);
        var fails = 0;
        var testCases = 0;
        var errorCount = 0;
        for (int y = 0;  y < lexer.LineCount;  y++)
        {
            foreach (var t in lexer.GetLineTokens(y))
                if (t.Error)
                    errorCount++;
            if (lexer.GetLine(y).Contains("###"))
            {
                testCases++;
                if (errorCount != 1)
                {
                    fails++;
                    Debug.WriteLine($"Test case above line {y} failed. Expecting 1 error, but got {errorCount} errors");
                }
                errorCount = 0;
            }
        }

        if (fails == 0)
            Debug.WriteLine($"Success!  {testCases} passed.");
        else
            Debug.WriteLine($"Fail! {fails} fails out of {testCases} test cases)");
        Assert.Equal(0, fails);
        Assert.True(testCases >= 10);
    }
}