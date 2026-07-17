using FanucLsp.Lsp.State;
using FanucLsp.Lsp.Util;
using KarelParser;
using KarelParser.SymbolTable;

namespace FanucLsp.Lsp.Hover;

internal sealed class KlSymbolHoverProvider : IKlHoverProvider
{
    public HoverResult? GetHoverResult(KarelProgram program, ContentPosition position, TextDocumentItem document, LspServerState state)
        => (program.SymTable.GetFieldSymbolAt(new(position.Line, position.Character), program.Uri)
            ?? ProgramUtils.GetTokenAt(document.Text, position) switch
            {
                { } token => program.SymTable.GetSymbol(token, new(position.Line, position.Character)),
                _ => null,
            }) switch
        {
            { } symbol => new HoverResult
            {
                Contents = BuildHoverInformation(symbol),
                Range = GetContentRange(position)
            },
            _ => null
        };

    private ContentRange GetContentRange(ContentPosition position)
        => new()
        {
            Start = position,
            End = position
        };

    private MarkupContent BuildHoverInformation(KarelSymbol symbol)
        => new()
        {
            Kind = "markdown",
            Value = $"**{symbol.Name}** ({symbol.Kind})\n"
                  + $"Type: {symbol.Type?.ToString() ?? "none"}\n"
                  + $"@ {symbol.DeclarationPosition}"
        };
}
