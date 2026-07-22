using FanucLsp.Lsp.State;
using Sprache;
using TPLangParser.TPLang;

namespace FanucLsp.Lsp.Completion;

internal sealed class TpCallCompletionProvider : ITpCompletionProvider
{

    public CompletionItem[] GetCompletions(TpProgram program, string lineText, int line, int column, LspServerState serverState)
        => CompletionProviderUtils.TokenizeInput(lineText[..column]) switch
        {
            [.., "CALL"] => CompletionProviderUtils.GetAllProgramNames(serverState),
            [.., "RUN"] => CompletionProviderUtils.GetAllProgramNames(serverState),
            _ => []
        };

}
