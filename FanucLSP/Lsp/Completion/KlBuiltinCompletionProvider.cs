using FanucLsp.Lsp.State;
using FanucLsp.Util;
using KarelParser;

namespace FanucLsp.Lsp.Completion;

internal sealed class KlBuiltinCompletionProvider : IKlCompletionProvider
{
    private CompletionItem[]? _completionItems = null;

    // Builtins are never members of a datum, so a chain that applies a field ('.')
    // or array ('[]') accessor to something means builtins shouldn't be offered.
    // The chain is the innermost access at the cursor, so an index being typed
    // inside a subscript (e.g. "arr[co|") is its own bare chain and still gets
    // builtins.
    public CompletionItem[] GetCompletions(KarelProgram program, string lineText, int line, int column, LspServerState serverState)
        => CompletionProviderUtils.GetInnermostAccessChain(lineText[..Math.Min(column, lineText.Length)]) switch
        {
            { } chain when chain.Contains('.') || chain.Contains('[') => [],
            _ => _completionItems ??= BuildCompletionList()
        };

    private static CompletionItem[] BuildCompletionList()
    {
        if (EmbeddedResourceReader.GetKarelBuiltInSnippets() is not { } snippets)
        {
            return [];
        }

        return snippets.Select(kvp => new CompletionItem
        {
            Label = kvp.Value.Prefix,
            Detail = kvp.Key,
            Documentation = string.Join('\n', kvp.Value.Description ?? []),
            Kind = CompletionItemKind.Function,
            InsertText = kvp.Value.Body.FirstOrDefault() ?? kvp.Value.Prefix,
            InsertTextFormat = InsertTextFormat.Snippet
        }).ToArray();
    }
}
