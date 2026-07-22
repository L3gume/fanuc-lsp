using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FanucLsp.Lsp.State;
using KarelParser;
using TPLangParser.TPLang;

namespace FanucLsp.Lsp.Completion;

internal interface ITpCompletionProvider
{
    public CompletionItem[] GetCompletions(TpProgram program, string lineText, int line, int column, LspServerState serverState);
}

internal interface IKlCompletionProvider
{
    public CompletionItem[] GetCompletions(KarelProgram program, string lineText, int line, int column, LspServerState serverState);
}

public class CodeSnippet
{
    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string[] Body { get; set; } = [];

    [JsonPropertyName("description")]
    public string[]? Description { get; set; } = [];

    public CodeSnippet()
    {
    }
}

internal class CompletionProviderUtils
{
    public static List<string> TokenizeInput(string input)
    {
        // First, remove line number if present (format: "123: ")
        var lineWithoutNumber = RemoveLineNumber(input);

        // Simple tokenization - split by whitespace but preserve quoted strings
        var tokens = new List<string>();
        const string pattern = """
                                   [^\s"]+|"[^"]*\"
                                   """;
        var matches = Regex.Matches(lineWithoutNumber, pattern);

        foreach (Match match in matches)
        {
            tokens.Add(match.Value);
        }

        return tokens;
    }

    // The innermost variable-access chain ending at the cursor: the identifier,
    // field ('.') and array ('[]') accessors that make up the token being
    // completed. Scanning stops at any separator (whitespace, operator, '(', ',')
    // and — crucially — at an unmatched '[', so that when the cursor sits inside an
    // array subscript the chain is the index expression being typed, not the array
    // access enclosing it. Balanced "[...]" subscripts stay part of the chain.
    public static string GetInnermostAccessChain(string textToCursor)
    {
        var depth = 0;
        var start = textToCursor.Length;
        for (var i = textToCursor.Length - 1; i >= 0; --i)
        {
            var c = textToCursor[i];
            switch (c)
            {
                case ']':
                    ++depth;
                    start = i;
                    break;
                case '[' when depth > 0:
                    --depth;
                    start = i;
                    break;
                case '.' or '_':
                    start = i;
                    break;
                case var letter when char.IsLetterOrDigit(letter):
                    start = i;
                    break;
                // A separator, or an unmatched '[' opening an enclosing subscript.
                default:
                    return textToCursor[start..];
            }
        }
        return textToCursor[start..];
    }

    public static string RemoveLineNumber(string input)
    {
        // Match pattern like "123: " at the beginning of the line
        const string lineNumberPattern = @"^\s*\d+\s*:";
        var match = Regex.Match(input, lineNumberPattern);

        return match.Success ?
            // Strip off the line number and the colon
            input[match.Value.Length..].TrimStart() : input;
    }

    public static CompletionItem[] GetKarelProgramNames(LspServerState serverState)
        => serverState.AllTextDocuments
            .Where(kvp => kvp.Value.Program is KlProgram)
            .Select(kvp => new CompletionItem
            {
                Label = Path.GetFileNameWithoutExtension(kvp.Value.TextDocument.Uri).ToUpper(),
                Detail = string.Empty,
                Documentation = string.Empty,
                InsertText = Path.GetFileNameWithoutExtension(kvp.Value.TextDocument.Uri).ToUpper(),
                InsertTextFormat = InsertTextFormat.PlainText,
                Kind = CompletionItemKind.Class
            }).ToArray();

    public static CompletionItem[] GetAllProgramNames(LspServerState serverState)
        => serverState.AllTextDocuments.Select(kvp => new CompletionItem()
        {
            Label = Path.GetFileNameWithoutExtension(kvp.Value.TextDocument.Uri).ToUpper(),
            Detail = $"Type:  {kvp.Value.Type}\n"
                   + $"Usage: {Path.GetFileNameWithoutExtension(kvp.Value.TextDocument.Uri).ToUpper()}{ExtractArgs(kvp.Value.Program, MakeArgsDetail)}\n"
                   + $"Uri:   {kvp.Value.TextDocument.Uri}",
            Documentation = LspUtils.ExtractDocComment(kvp.Value.Program),
            InsertText = $"{Path.GetFileNameWithoutExtension(kvp.Value.TextDocument.Uri).ToUpper()}{ExtractArgs(kvp.Value.Program, MakeArgsSnippet)}",
            InsertTextFormat = InsertTextFormat.Snippet,
            Kind = CompletionItemKind.Function
        }).ToArray();

    public static string ExtractArgs(RobotProgram? program, Func<int, string> fn)
        => LspUtils.ExtractDocComment(program) switch
        {
            { } comment when !string.IsNullOrWhiteSpace(comment) =>
                comment.Split('\n').Where(cmt => cmt.TrimStart().StartsWith("AR\\[")).ToList() switch
                {
                    { } args => fn(args.Count),
                    _ => string.Empty
                },
            _ => string.Empty
        };

    public static string MakeArgsDetail(int count)
    {
        if (count == 0)
        {
            return string.Empty;
        }

        var ret = "(";
        for (var ctr = 1; ctr < count; ++ctr)
        {
            ret += $"arg{ctr},";
        }
        ret += $"arg{count})";
        return ret;
    }

    public static string MakeArgsSnippet(int count)
    {
        if (count == 0)
        {
            return string.Empty;
        }

        var ret = "(";
        for (var ctr = 1; ctr < count; ++ctr)
        {
            ret += $"${{{ctr}:arg{ctr}}},";
        }
        ret += $"${{{count}:arg{count}}})";
        return ret;
    }

}



