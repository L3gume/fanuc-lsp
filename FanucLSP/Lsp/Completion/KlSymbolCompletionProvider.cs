using ParserUtils;

using KarelParser;
using KarelParser.SymbolTable;

using FanucLsp.Lsp.State;

namespace FanucLsp.Lsp.Completion;

internal sealed class KlSymbolCompletionProvider : IKlCompletionProvider
{
    public CompletionItem[] GetCompletions(KarelProgram program, string lineText, int line, int column, LspServerState serverState)
        => CompletionProviderUtils.GetInnermostAccessChain(lineText[..Math.Min(column, lineText.Length)]) switch
        {
            { } chain when chain.LastIndexOf('.') is var dot && dot >= 0 => CompleteFields(program, chain[..dot], new(line, column)),
            _ => CompleteSymbols(program, new(line, column))
        };

    // Every variable and routine visible at the cursor — the enclosing routine's
    // locals and parameters plus the program globals (and anything pulled in
    // through %INCLUDE) — taken from the symbol table.
    private static CompletionItem[] CompleteSymbols(KarelProgram program, TokenPosition position)
        => program.SymTable.GetVisibleSymbols(position)
            .Where(symbol => symbol.Kind is KarelSymbolKind.Variable or KarelSymbolKind.Routine)
            .Select(ToCompletionItem)
            .ToArray();

    // The fields of the struct that the access path before the last '.' evaluates
    // to. The symbol table resolves the path — scoped to the cursor so locals and
    // parameters resolve too — and if that type is (or unwraps to) a struct, its
    // fields become the completions.
    private static CompletionItem[] CompleteFields(KarelProgram program, string accessPath, TokenPosition position)
        => ResolveStructure(program.SymTable.Resolver, program.SymTable.ResolveAccessSymbol(accessPath, position)?.Type) switch
        {
            { } structure => structure.Fields.Select(ToCompletionItem).ToArray(),
            _ => []
        };

    // Unwraps arrays and resolves a named type to its struct body, or null when the
    // type isn't a struct (or can't be found in the resolver's registry).
    private static KarelStructure? ResolveStructure(KarelTypeResolver? resolver, KarelUserType? type)
        => type switch
        {
            KarelStructure structure => structure,
            KarelTypeArray array => ResolveStructure(resolver, array.Type),
            KarelTypeName name => resolver?.GetStructure(name.Identifier),
            _ => null
        };

    private static CompletionItem ToCompletionItem(KarelSymbol symbol)
        => new()
        {
            Label = symbol.Name.ToUpper(),
            Detail = symbol.Kind switch
            {
                KarelSymbolKind.Routine => "Karel Routine",
                _ => "Karel Variable"
            },
            Documentation = symbol.Type?.ToString() ?? string.Empty,
            InsertText = symbol.Name.ToUpper(),
            InsertTextFormat = InsertTextFormat.PlainText,
            Kind = symbol.Kind switch
            {
                KarelSymbolKind.Routine => CompletionItemKind.Function,
                _ => CompletionItemKind.Variable
            }
        };

    private static CompletionItem ToCompletionItem(KarelField field)
        => new()
        {
            Label = field.Identifier.ToUpper(),
            Detail = "Structure Field",
            Documentation = field.Type.ToString(),
            InsertText = field.Identifier.ToUpper(),
            InsertTextFormat = InsertTextFormat.PlainText,
            Kind = CompletionItemKind.Field
        };
}