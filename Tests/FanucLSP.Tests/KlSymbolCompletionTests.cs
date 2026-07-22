using FanucLsp.Lsp;
using FanucLsp.Lsp.Completion;
using FanucLsp.Lsp.State;
using KarelParser;

namespace FanucLsp.Tests;

// Coverage for symbol completions in a Karel program: the plain variable/routine
// list, and struct-field completions driven off the '.' access operator resolved
// through the symbol table. Uses a small synthetic program so it is self-contained.
public class KlSymbolCompletionTests : IDisposable
{
    private readonly string _dir;
    private readonly string _klPath;
    private readonly LspServerState _state;
    private readonly KarelProgram _program;

    public KlSymbolCompletionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "klcompl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var klText =
            "PROGRAM datastore\r\n" +
            "TYPE\r\n" +
            "minmax = STRUCTURE\r\n" +
            "    lo : INTEGER\r\n" +
            "    hi : INTEGER\r\n" +
            "ENDSTRUCTURE\r\n" +
            "cfg_t = STRUCTURE\r\n" +
            "    mode : INTEGER\r\n" +
            "    lim : minmax\r\n" +
            "    arr : ARRAY[3] OF minmax\r\n" +
            "ENDSTRUCTURE\r\n" +
            "VAR\r\n" +
            "    cfg : cfg_t\r\n" +
            "    count : INTEGER\r\n" +
            "ROUTINE do_work : INTEGER\r\n" +
            "BEGIN\r\n" +
            "    RETURN(1)\r\n" +
            "END do_work\r\n" +
            "BEGIN\r\n" +
            "    count = 0\r\n" +
            "END datastore\r\n";
        _klPath = Path.Combine(_dir, "datastore.kl");
        File.WriteAllText(_klPath, klText);

        var klProgram = KarelProgram.ProcessAndParse(new Uri(_klPath).ToString());
        Assert.True(klProgram.WasSuccessful, klProgram.Message);
        _program = klProgram.Value;

        _state = new LspServerState(Path.Combine(_dir, "log.txt"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private CompletionItem[] Complete(string lineText, int? column = null, int line = 0)
        => new KlSymbolCompletionProvider()
            .GetCompletions(_program, lineText, line, column ?? lineText.Length, _state);

    [Fact]
    public void NoAccessOperator_ListsVariablesAndRoutines()
    {
        var items = Complete("    ");

        var labels = items.Select(i => i.Label).ToArray();
        Assert.Contains("CFG", labels);
        Assert.Contains("COUNT", labels);
        Assert.Contains("DO_WORK", labels);
    }

    [Fact]
    public void NoAccessOperator_DoesNotListTypesOrFields()
    {
        var labels = Complete("    ").Select(i => i.Label).ToArray();

        Assert.DoesNotContain("CFG_T", labels);
        Assert.DoesNotContain("MINMAX", labels);
        Assert.DoesNotContain("MODE", labels);
    }

    [Fact]
    public void RoutineAndVariable_GetDistinctKinds()
    {
        var items = Complete("    ");

        Assert.Equal(CompletionItemKind.Function, items.Single(i => i.Label == "DO_WORK").Kind);
        Assert.Equal(CompletionItemKind.Variable, items.Single(i => i.Label == "CFG").Kind);
    }

    [Fact]
    public void FieldAccess_ListsStructFields()
    {
        var labels = Complete("    cfg.").Select(i => i.Label).ToArray();

        Assert.Equal(["MODE", "LIM", "ARR"], labels);
        Assert.All(Complete("    cfg."), i => Assert.Equal(CompletionItemKind.Field, i.Kind));
    }

    [Fact]
    public void PartialFieldAccess_StillListsAllFieldsOfOwningStruct()
    {
        // The client filters by the partial text; the provider returns every field.
        var labels = Complete("    cfg.mo").Select(i => i.Label).ToArray();

        Assert.Equal(["MODE", "LIM", "ARR"], labels);
    }

    [Fact]
    public void NestedFieldAccess_ResolvesThroughStructTypedField()
    {
        var labels = Complete("    cfg.lim.").Select(i => i.Label).ToArray();

        Assert.Equal(["LO", "HI"], labels);
    }

    [Fact]
    public void ArrayIndexedFieldAccess_ResolvesElementStruct()
    {
        // arr is ARRAY OF minmax; indexing yields a minmax element.
        var labels = Complete("    cfg.arr[2].").Select(i => i.Label).ToArray();

        Assert.Equal(["LO", "HI"], labels);
    }

    [Fact]
    public void FieldAccess_OnNonStructField_ReturnsNothing()
    {
        Assert.Empty(Complete("    cfg.mode."));
    }

    [Fact]
    public void FieldAccessInsideArraySubscript_ResolvesInnermostChain()
    {
        // The base being indexed ("arr") is irrelevant — completion is driven by
        // the innermost access "cfg.mo" typed inside the subscript, so it must
        // list cfg's fields, not arr's element fields.
        var labels = Complete("    arr[cfg.mo").Select(i => i.Label).ToArray();

        Assert.Equal(["MODE", "LIM", "ARR"], labels);
    }

    [Fact]
    public void BareIndexInsideArraySubscript_ListsVariablesAndRoutines()
    {
        var labels = Complete("    cfg.arr[co").Select(i => i.Label).ToArray();

        Assert.Contains("CFG", labels);
        Assert.Contains("COUNT", labels);
        Assert.Contains("DO_WORK", labels);
    }

    [Fact]
    public void FieldAccess_OnUnknownBase_ReturnsNothing()
    {
        Assert.Empty(Complete("    nope."));
    }

    [Fact]
    public void ColumnBoundsTheAnalyzedText()
    {
        // Cursor sits right after "cfg." even though "mode" follows on the line.
        var labels = Complete("    cfg.mode", column: "    cfg.".Length).Select(i => i.Label).ToArray();

        Assert.Equal(["MODE", "LIM", "ARR"], labels);
    }
}
