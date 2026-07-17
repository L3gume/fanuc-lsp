using FanucLsp.Lsp.State;
using ParserUtils;
using TPLangParser.TPLang;
using TPLangParser.TPLang.Instructions;

namespace FanucLsp.Lsp.References;

internal sealed class TpProgramReferencesProvider : ITpReferenceProvider
{
    public TextDocumentLocation[] GetReferences(TpProgram program, ContentPosition position, TextDocumentItem document, ReferenceContext context, LspServerState state)
        => program.GetNodeAt<TpCallByName>(new(position.Line, position.Character)) switch
        {
            { ProgramName: var name } =>  state.AllTextDocuments.Values
                .Where(doc => doc.Program is TppProgram)
                .Select(doc => (doc.TextDocument.Uri, Program: (TppProgram)doc.Program!))
                .SelectMany(entry => entry.Program!.Program.SymTable.GetProgramReferences(name)
                        .Select(position => new TextDocumentLocation { Uri = entry.Uri, Range = GetContentRange(position) }))
                .ToArray(),
            _ => []
        };

    private ContentRange GetContentRange(TokenPosition position)
        => new()
        {
            Start = new ContentPosition { Line = position.Line, Character = position.Column },
            End = new ContentPosition { Line = position.Line, Character = position.Column }
        };
}
