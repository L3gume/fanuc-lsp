using FanucLsp.Lsp.State;
using FanucLsp.Lsp.Util;
using ParserUtils;
using KarelParser;

namespace FanucLsp.Lsp.Definition;

internal class KlSymbolDefinitionProvider : IKlDefinitionProvider
{
    public TextDocumentLocation? GetDefinitionLocation(
        KarelProgram program,
        ContentPosition position,
        TextDocumentItem document,
        LspServerState state
    )
        => (program.SymTable.GetFieldSymbolAt(new(position.Line, position.Character), program.Uri)
            ?? ProgramUtils.GetTokenAt(document.Text, position) switch
            {
                { } token => program.SymTable.GetSymbol(token, GetTokenPosition(position)),
                _ => null,
            }) switch
        {
            { } symbol => new TextDocumentLocation
            {
                Uri = symbol.DeclarationPosition.ProgramUri.LocalPath,
                Range = GetContentRange(symbol.DeclarationPosition.Position)
            },
            _ => null
        };

    private TokenPosition GetTokenPosition(ContentPosition position)
        => new(position.Line, position.Character);

    private ContentRange GetContentRange(TokenPosition position)
        => new()
        {
            Start = new ContentPosition { Line = position.Line, Character = position.Column },
            End = new ContentPosition { Line = position.Line, Character = position.Column }
        };
}
