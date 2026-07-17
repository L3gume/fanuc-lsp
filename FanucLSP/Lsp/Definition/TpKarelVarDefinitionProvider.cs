using FanucLsp.Lsp.State;
using FanucLsp.Lsp.Util;
using ParserUtils;
using TPLangParser.TPLang;


namespace FanucLsp.Lsp.Definition;

internal sealed class TpKarelVarDefinitionProvider : ITpDefinitionProvider
{
    public TextDocumentLocation? GetDefinitionLocation(TpProgram program, ContentPosition position, TextDocumentItem document, LspServerState state)
        => program.GetNodeAt<TpValueKarelVariable>(new(position.Line, position.Character)) is { } karelVar
           && TpKarelResolver.Resolve(state, karelVar) is { } symbol
            ? new TextDocumentLocation
            {
                Uri = symbol.DeclarationPosition.ProgramUri.LocalPath,
                Range = GetContentRange(symbol.DeclarationPosition.Position)
            }
            : null;

    private ContentRange GetContentRange(TokenPosition position)
        => new()
        {
            Start = new ContentPosition { Line = position.Line, Character = position.Column },
            End = new ContentPosition { Line = position.Line, Character = position.Column }
        };
}
