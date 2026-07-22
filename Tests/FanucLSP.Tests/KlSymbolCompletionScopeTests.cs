using FanucLsp.Lsp;
using FanucLsp.Lsp.Completion;
using FanucLsp.Lsp.State;
using KarelParser;

namespace FanucLsp.Tests;

// Scope-aware symbol completion: a routine's locals and parameters must be offered
// alongside the program globals when the cursor is inside that routine, and struct
// field access ('.') must resolve against whichever variable — local or global —
// is visible at the cursor.
public class KlSymbolCompletionScopeTests : IDisposable
{
    private readonly string _dir;
    private readonly KarelProgram _program;
    private readonly LspServerState _state;

    // Line numbers (0-based) referenced by the tests:
    private const int InRoutineBody = 18; // "    tmp = 0" inside ROUTINE work
    private const int InMainBody = 22;    // "    gcount = 0" in the program body

    public KlSymbolCompletionScopeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "klscope_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var klText =
            "PROGRAM datastore\r\n" +                       // 0
            "TYPE\r\n" +                                    // 1
            "minmax = STRUCTURE\r\n" +                      // 2
            "    lo : INTEGER\r\n" +                        // 3
            "    hi : INTEGER\r\n" +                        // 4
            "ENDSTRUCTURE\r\n" +                            // 5
            "cfg_t = STRUCTURE\r\n" +                       // 6
            "    mode : INTEGER\r\n" +                      // 7
            "    lim : minmax\r\n" +                        // 8
            "ENDSTRUCTURE\r\n" +                            // 9
            "VAR\r\n" +                                     // 10
            "    gcfg : cfg_t\r\n" +                        // 11
            "    gcount : INTEGER\r\n" +                    // 12
            "ROUTINE work(arg : INTEGER) : INTEGER\r\n" +   // 13
            "VAR\r\n" +                                     // 14
            "    loc : cfg_t\r\n" +                         // 15
            "    tmp : INTEGER\r\n" +                       // 16
            "BEGIN\r\n" +                                   // 17
            "    tmp = 0\r\n" +                             // 18
            "    RETURN(tmp)\r\n" +                         // 19
            "END work\r\n" +                                // 20
            "BEGIN\r\n" +                                   // 21
            "    gcount = 0\r\n" +                          // 22
            "END datastore\r\n";                            // 23
        var klPath = Path.Combine(_dir, "datastore.kl");
        File.WriteAllText(klPath, klText);

        var klProgram = KarelProgram.ProcessAndParse(new Uri(klPath).ToString());
        Assert.True(klProgram.WasSuccessful, klProgram.Message);
        _program = klProgram.Value;

        _state = new LspServerState(Path.Combine(_dir, "log.txt"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string[] Complete(string lineText, int line)
        => new KlSymbolCompletionProvider()
            .GetCompletions(_program, lineText, line, lineText.Length, _state)
            .Select(i => i.Label)
            .ToArray();

    [Fact]
    public void InsideRoutine_ListsLocalsParametersAndGlobals()
    {
        var labels = Complete("    ", InRoutineBody);

        Assert.Contains("LOC", labels);    // local variable
        Assert.Contains("TMP", labels);    // local variable
        Assert.Contains("ARG", labels);    // parameter
        Assert.Contains("GCFG", labels);   // global still visible
        Assert.Contains("GCOUNT", labels);
        Assert.Contains("WORK", labels);   // routine
    }

    [Fact]
    public void InMainBody_DoesNotListRoutineLocalsOrParameters()
    {
        var labels = Complete("    ", InMainBody);

        Assert.DoesNotContain("LOC", labels);
        Assert.DoesNotContain("TMP", labels);
        Assert.DoesNotContain("ARG", labels);
        Assert.Contains("GCFG", labels);
        Assert.Contains("GCOUNT", labels);
    }

    [Fact]
    public void FieldAccess_OnLocalStructVariable_ListsFields()
    {
        var labels = Complete("    loc.", InRoutineBody);

        Assert.Equal(["MODE", "LIM"], labels);
    }

    [Fact]
    public void FieldAccess_OnGlobalStructVariable_FromInsideRoutine_ListsFields()
    {
        var labels = Complete("    gcfg.", InRoutineBody);

        Assert.Equal(["MODE", "LIM"], labels);
    }

    [Fact]
    public void FieldAccess_OnGlobalStructVariable_FromMainBody_ListsFields()
    {
        var labels = Complete("    gcfg.", InMainBody);

        Assert.Equal(["MODE", "LIM"], labels);
    }

    [Fact]
    public void FieldAccess_OnLocalStructVariable_FromMainBody_ResolvesNothing()
    {
        // loc is not in scope in the program body, so nothing resolves.
        Assert.Empty(Complete("    loc.", InMainBody));
    }
}
