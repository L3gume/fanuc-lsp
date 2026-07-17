using FanucLsp.Lsp.State;
using KarelParser.SymbolTable;
using TPLangParser.TPLang;

namespace FanucLsp.Lsp.Util;

// Resolves a Karel-variable reference written in a TP program ($[PROG]var.field)
// to the Karel symbol it addresses, by locating the parsed Karel program named
// PROG among the indexed documents and walking its symbol table. Shared by the
// TP definition and hover providers so both agree on the target.
internal static class TpKarelResolver
{
    public static KarelSymbol? Resolve(LspServerState state, TpValueKarelVariable karelVar)
        => state.AllTextDocuments.Values
            .FirstOrDefault(doc => doc.Program is KlProgram
                && Path.GetFileNameWithoutExtension(doc.TextDocument.Uri)
                    .Equals(karelVar.Program, StringComparison.OrdinalIgnoreCase))
            ?.Program is KlProgram klProg
            ? klProg.Program.SymTable.ResolveAccessSymbol(karelVar.Variable)
            : null;
}