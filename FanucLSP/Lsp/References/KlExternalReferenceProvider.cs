using FanucLsp.Lsp.State;
using FanucLsp.Lsp.Util;
using ParserUtils;
using KarelParser;
using KarelParser.SymbolTable;

namespace FanucLsp.Lsp.References;

internal sealed class KlExternalReferenceProvider : IKlReferenceProvider
{
    public TextDocumentLocation[] GetReferences(
        KarelProgram program,
        ContentPosition position,
        TextDocumentItem document,
        ReferenceContext context,
        LspServerState state
    )
    {
        var path = ProgramUtils.GetKlAccessPathAt(document.Text, position);

        // A variable-rooted access ("cfg", "cfg.lim.lo", "cfg.arr[2]") yields precise
        // single-variable results: match every TP usage at or under that exact datum.
        // The base must resolve to a top-level variable — only those are reachable
        // from outside the program.
        var baseName = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Split('.', '[')[0];
        if (program.SymTable.GetTopLevelSymbol(baseName) is { Kind: KarelSymbolKind.Variable })
        {
            // A TP program keys each Karel access by its full path ($[PROG]cfg.mode,
            // $[PROG]cfg.lim[1]), so match every usage at or under the clicked datum:
            // clicking the base variable collects all its fields/elements, and a
            // field/array access collects its indexed usages.
            return CollectByPrefixes(program, state, [path]);
        }

        // Otherwise the cursor may be on a struct-field declaration inside a TYPE
        // block (e.g. "mode : INTEGER" in "cfg_t = STRUCTURE ..."). A field doesn't
        // know which variable reaches it, so enumerate every variable-rooted path in
        // this program that addresses the field and match TP usages under each.
        if (program.SymTable.GetFieldSymbolAt(new TokenPosition(position.Line, position.Character), program.Uri) is not { } fieldSym)
        {
            return [];
        }

        var field = fieldSym.Name;
        // FullName is "OwningType.field" (literal dot); strip the trailing ".field".
        var owningType = fieldSym.FullName[..^(field.Length + 1)];
        return CollectByPrefixes(program, state, program.SymTable.GetVariablePathsToField(owningType, field));
    }

    // Gathers every TP usage keyed under $[PROG]<path> for each path, deduped by
    // location. Distinct paths can reach the same TP symbol (e.g. two variables of
    // the same struct type never collide, but a field reached by multiple relative
    // paths could), so the final set is deduplicated.
    private TextDocumentLocation[] CollectByPrefixes(KarelProgram program, LspServerState state, IEnumerable<string> paths)
        => paths
            .SelectMany(path => state.AllTextDocuments.Values
                .Where(doc => doc.Program is TppProgram)
                .SelectMany(doc => ((TppProgram)doc.Program!).Program.SymTable
                    .GetKarelVarReferencesByPrefix($"$[{program.Name}]{path}")
                    .Select(refpos => new TextDocumentLocation { Uri = doc.TextDocument.Uri, Range = GetContentRange(refpos) })))
            .GroupBy(loc => (loc.Uri, loc.Range.Start.Line, loc.Range.Start.Character))
            .Select(group => group.First())
            .ToArray();

    private ContentRange GetContentRange(TokenPosition position)
        => new()
        {
            Start = new ContentPosition { Line = position.Line, Character = position.Column },
            End = new ContentPosition { Line = position.Line, Character = position.Column }
        };
}
