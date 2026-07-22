using FanucLsp.Lsp;
using FanucLsp.Lsp.Completion;
using FanucLsp.Lsp.State;
using KarelParser;

namespace FanucLsp.Tests;

// The builtin completion provider offers Karel builtin snippets, but only when a
// bare identifier is being typed — not when the cursor sits inside a field or
// array access, where builtins are never valid.
public class KlBuiltinCompletionTests : IDisposable
{
    private readonly string _dir;
    private readonly LspServerState _state;
    private readonly KarelProgram _program;

    public KlBuiltinCompletionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "klbuiltin_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        var klText =
            "PROGRAM p\r\n" +
            "BEGIN\r\n" +
            "    x = 0\r\n" +
            "END p\r\n";
        var klPath = Path.Combine(_dir, "p.kl");
        File.WriteAllText(klPath, klText);

        var klProgram = KarelProgram.ProcessAndParse(new Uri(klPath).ToString());
        Assert.True(klProgram.WasSuccessful, klProgram.Message);
        _program = klProgram.Value;

        _state = new LspServerState(Path.Combine(_dir, "log.txt"));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private CompletionItem[] Complete(string lineText, int? column = null, int line = 0)
        => new KlBuiltinCompletionProvider()
            .GetCompletions(_program, lineText, line, column ?? lineText.Length, _state);

    [Fact]
    public void BareIdentifier_ReturnsBuiltins()
        => Assert.NotEmpty(Complete("    WR"));

    [Fact]
    public void EmptyContext_ReturnsBuiltins()
        => Assert.NotEmpty(Complete("    "));

    [Fact]
    public void FieldAccess_ReturnsNothing()
        => Assert.Empty(Complete("    cfg."));

    [Fact]
    public void PartialFieldAccess_ReturnsNothing()
        => Assert.Empty(Complete("    cfg.mo"));

    [Fact]
    public void InsideArraySubscript_ReturnsBuiltins()
    {
        // The cursor is typing an index expression, where a builtin is valid.
        Assert.NotEmpty(Complete("    arr["));
        Assert.NotEmpty(Complete("    arr[co"));
    }

    [Fact]
    public void FieldAccessInsideArraySubscript_ReturnsNothing()
        // The innermost token "cfg.mo" is a field access, so builtins stay hidden.
        => Assert.Empty(Complete("    arr[cfg.mo"));

    [Fact]
    public void ArrayThenFieldAccess_ReturnsNothing()
        => Assert.Empty(Complete("    arr[2]."));

    [Fact]
    public void ColumnBeforeAccessor_ReturnsBuiltins()
        // Cursor sits on the bare "cfg" before the '.' is considered.
        => Assert.NotEmpty(Complete("    cfg.mode", column: "    cfg".Length));
}