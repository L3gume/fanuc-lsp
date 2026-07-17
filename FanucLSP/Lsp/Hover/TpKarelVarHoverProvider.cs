using FanucLsp.Lsp.State;
using FanucLsp.Lsp.Util;
using KarelParser.SymbolTable;
using ParserUtils;
using TPLangParser.TPLang;

namespace FanucLsp.Lsp.Hover;

// Hover for a Karel variable referenced from a TP program ($[PROG]var.field):
// resolves it to the Karel declaration and shows the same summary the Karel
// in-file hover does.
internal sealed class TpKarelVarHoverProvider : IHoverProvider
{
    public HoverResult? GetHoverResult(TpProgram program, ContentPosition position, LspServerState serverState)
        => program.GetNodeAt<TpValueKarelVariable>(new(position.Line, position.Character)) is { } karelVar
           && TpKarelResolver.Resolve(serverState, karelVar) is { } symbol
            ? new HoverResult
            {
                Contents = BuildHoverInformation(symbol),
                Range = GetContentRange(position)
            }
            : null;

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