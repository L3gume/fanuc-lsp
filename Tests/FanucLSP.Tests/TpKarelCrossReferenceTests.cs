using FanucLsp.Lsp;
using FanucLsp.Lsp.Definition;
using FanucLsp.Lsp.Hover;
using FanucLsp.Lsp.References;
using FanucLsp.Lsp.State;
using KarelParser;
using TPLangParser.TPLang;

namespace FanucLsp.Tests;

// End-to-end coverage for navigating between a TP program and the Karel program
// whose data it references ($[PROG]var.field). Uses small synthetic programs so
// the tests are self-contained.
public class TpKarelCrossReferenceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _klPath;
    private readonly string _klText;
    private readonly string _tpPath;
    private readonly string _tpText;
    private readonly LspServerState _state;

    // A Karel program "datastore" exposing a nested struct variable, and a TP
    // program "caller" that reads several paths under it.
    public TpKarelCrossReferenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "tpkl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        _klText =
            "PROGRAM datastore\r\n" +
            "TYPE\r\n" +
            "minmax = STRUCTURE\r\n" +
            "    lo : INTEGER\r\n" +
            "    hi : INTEGER\r\n" +
            "ENDSTRUCTURE\r\n" +
            "cfg_t = STRUCTURE\r\n" +
            "    mode : INTEGER\r\n" +
            "    lim : minmax\r\n" +
            "    arr : ARRAY[3] OF INTEGER\r\n" +
            "ENDSTRUCTURE\r\n" +
            "VAR\r\n" +
            "    cfg : cfg_t\r\n" +
            "    cfg2 : cfg_t\r\n" +
            "BEGIN\r\n" +
            "    cfg.arr[2] = 5\r\n" +
            "END datastore\r\n";
        _klPath = Path.Combine(_dir, "datastore.kl");
        File.WriteAllText(_klPath, _klText);

        _tpText =
            "/PROG CALLER\r\n/ATTR\r\n/MN\r\n" +
            "  1:  R[1]=($[datastore]cfg.mode) ;\r\n" +
            "  2:  R[2]=($[datastore]cfg.lim.lo) ;\r\n" +
            "  3:  R[3]=($[datastore]cfg.arr[1]) ;\r\n" +
            "  4:  R[4]=($[datastore]cfg2.mode) ;\r\n" +
            "/END\r\n";
        _tpPath = Path.Combine(_dir, "caller.ls");
        File.WriteAllText(_tpPath, _tpText);

        var klProgram = KarelProgram.ProcessAndParse(new Uri(_klPath).ToString());
        Assert.True(klProgram.WasSuccessful, klProgram.Message);
        var tpProgram = TpProgram.ProcessAndParse(_tpText);
        Assert.True(tpProgram.WasSuccessful, tpProgram.Message);

        _state = new LspServerState(Path.Combine(_dir, "log.txt"));
        _state.AllTextDocuments[_klPath] = new TextDocumentState(
            new TextDocumentItem { Uri = _klPath, LanguageId = "karel", Text = _klText },
            new ContentPosition(), DocumentType.Karel, new KlProgram(klProgram.Value));
        _state.AllTextDocuments[_tpPath] = new TextDocumentState(
            new TextDocumentItem { Uri = _tpPath, LanguageId = "tp", Text = _tpText },
            new ContentPosition(), DocumentType.Tp, new TppProgram(tpProgram.Value));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private KarelProgram Kl => ((KlProgram)_state.AllTextDocuments[_klPath].Program!).Program;
    private TpProgram Tp => ((TppProgram)_state.AllTextDocuments[_tpPath].Program!).Program;

    private static ContentPosition CursorOn(string text, int line, string needle, string within)
    {
        var lineText = text.Split('\n')[line];
        var col = lineText.IndexOf(needle, StringComparison.Ordinal) + needle.IndexOf(within, StringComparison.Ordinal);
        return new ContentPosition { Line = line, Character = col };
    }

    // The Karel VAR line is index 12 (0-based) in _klText.
    private ContentPosition KlCursorOnCfg()
        => CursorOn(_klText, 12, "cfg : cfg_t", "cfg");

    [Fact]
    public void ExternalReferences_OnBaseVariable_FindsEveryFieldAndArrayUsageInTp()
    {
        var refs = new KlExternalReferenceProvider()
            .GetReferences(Kl, KlCursorOnCfg(), _state.AllTextDocuments[_klPath].TextDocument,
                new ReferenceContext { IncludeDeclaration = false }, _state);

        // cfg.mode, cfg.lim.lo, cfg.arr[1] — all three usages under the base var.
        Assert.Equal(3, refs.Length);
        Assert.All(refs, r => Assert.Equal(_tpPath, r.Uri));
        Assert.Equal([3, 4, 5], refs.Select(r => r.Range.Start.Line).OrderBy(l => l).ToArray());
    }

    [Fact]
    public void ExternalReferences_OnArrayRootedAccess_FindsIndexedUsage()
    {
        // Cursor on "arr" of the variable-rooted access "cfg.arr[2]" in the Karel
        // body (line 15). The path is "cfg.arr" (no index), which must still match
        // the indexed TP usage "$[datastore]cfg.arr[1]".
        var refs = new KlExternalReferenceProvider()
            .GetReferences(Kl, CursorOn(_klText, 15, "cfg.arr[2]", "arr"),
                _state.AllTextDocuments[_klPath].TextDocument,
                new ReferenceContext { IncludeDeclaration = false }, _state);

        Assert.Single(refs);
        Assert.Equal(5, refs[0].Range.Start.Line);
    }

    [Fact]
    public void ExternalReferences_OnFieldDeclaration_FindsUsagesThroughEveryVariableOfThatType()
    {
        // Cursor on the "mode" field declaration inside "cfg_t" (Karel line 7).
        // Both cfg and cfg2 are of type cfg_t, so this must find $[datastore]cfg.mode
        // (TP line 3) and $[datastore]cfg2.mode (TP line 6).
        var refs = new KlExternalReferenceProvider()
            .GetReferences(Kl, CursorOn(_klText, 7, "mode : INTEGER", "mode"),
                _state.AllTextDocuments[_klPath].TextDocument,
                new ReferenceContext { IncludeDeclaration = false }, _state);

        Assert.Equal(2, refs.Length);
        Assert.All(refs, r => Assert.Equal(_tpPath, r.Uri));
        Assert.Equal([3, 6], refs.Select(r => r.Range.Start.Line).OrderBy(l => l).ToArray());
    }

    [Fact]
    public void ExternalReferences_OnNestedFieldDeclaration_FindsDottedPathUsage()
    {
        // "lo" is declared in "minmax" (Karel line 3), reached as cfg.lim.lo. Only
        // cfg.lim.lo is used in TP (cfg2.lim.lo is not), so one usage on TP line 4.
        var refs = new KlExternalReferenceProvider()
            .GetReferences(Kl, CursorOn(_klText, 3, "lo : INTEGER", "lo"),
                _state.AllTextDocuments[_klPath].TextDocument,
                new ReferenceContext { IncludeDeclaration = false }, _state);

        Assert.Single(refs);
        Assert.Equal(4, refs[0].Range.Start.Line);
    }

    [Fact]
    public void ExternalReferences_OnArrayFieldDeclaration_FindsIndexedUsage()
    {
        // "arr" is declared in "cfg_t" (Karel line 9). The relative path is "arr"
        // (no index), which must still match the indexed TP usage cfg.arr[1] on line 5.
        var refs = new KlExternalReferenceProvider()
            .GetReferences(Kl, CursorOn(_klText, 9, "arr : ARRAY[3] OF INTEGER", "arr"),
                _state.AllTextDocuments[_klPath].TextDocument,
                new ReferenceContext { IncludeDeclaration = false }, _state);

        Assert.Single(refs);
        Assert.Equal(5, refs[0].Range.Start.Line);
    }

    [Fact]
    public void Definition_OnKarelVarInTp_JumpsToFieldDeclarationInKarel()
    {
        var location = new TpKarelVarDefinitionProvider()
            .GetDefinitionLocation(Tp, CursorOn(_tpText, 3, "$[datastore]cfg.mode", "mode"),
                _state.AllTextDocuments[_tpPath].TextDocument, _state);

        Assert.NotNull(location);
        Assert.Equal(new Uri(_klPath), new Uri(location!.Uri));
        // "mode" field is declared on line 7 (0-based) of the Karel file.
        Assert.Equal(7, location.Range.Start.Line);
    }

    [Fact]
    public void Definition_OnProgramNamePartOfKarelVar_StillResolves()
    {
        // Cursor on the program bracket rather than the variable name: AST-based
        // resolution still identifies the whole $[datastore]cfg.mode node.
        var location = new TpKarelVarDefinitionProvider()
            .GetDefinitionLocation(Tp, CursorOn(_tpText, 3, "$[datastore]cfg.mode", "datastore"),
                _state.AllTextDocuments[_tpPath].TextDocument, _state);

        Assert.NotNull(location);
        Assert.Equal(7, location!.Range.Start.Line);
    }

    [Fact]
    public void Hover_OnKarelVarInTp_ShowsKarelSymbolSummary()
    {
        var hover = new TpKarelVarHoverProvider()
            .GetHoverResult(Tp, CursorOn(_tpText, 4, "$[datastore]cfg.lim.lo", "lo"), _state);

        Assert.NotNull(hover);
        Assert.Contains("lo", hover!.Contents.Value);
        Assert.Contains("StructField", hover.Contents.Value);
    }
}