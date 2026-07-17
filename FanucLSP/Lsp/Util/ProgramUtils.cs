using System.Text.RegularExpressions;

namespace FanucLsp.Lsp.Util;

internal static partial class ProgramUtils
{
    public static string GetTokenAt(string content, ContentPosition position)
    {
        var lines = content.Split('\n');
        if (position.Line < 0 || position.Line >= lines.Length)
        {
            return string.Empty;
        }

        var line = lines[position.Line];
        if (position.Character < 0 || position.Character >= line.Length)
        {
            return string.Empty;
        }

        // Find the start of the identifier
        var start = position.Character;
        while (start > 0 && IsIdentifierChar(line[start - 1]))
        {
            start--;
        }

        // Find the end of the identifier
        var end = position.Character;
        while (end < line.Length && IsIdentifierChar(line[end]))
        {
            end++;
        }

        // Extract the identifier
        if (start < end && IsIdentifierStart(line[start]))
        {
            return line.Substring(start, end - start);
        }

        return string.Empty;
    }

    public static string GetRegisterAt(string content, ContentPosition position)
    {
        var lines = content.Split('\n');
        if (position.Line < 0 || position.Line >= lines.Length)
        {
            return string.Empty;
        }

        var line = lines[position.Line];
        if (position.Character < 0 || position.Character >= line.Length)
        {
            return string.Empty;
        }

        // Find the start of the identifier
        var start = position.Character;
        while (start > 0 && IsRegisterChar(line[start - 1]))
        {
            start--;
        }

        // Find the end of the identifier
        var end = position.Character;
        while (end < line.Length && IsRegisterChar(line[end]))
        {
            end++;
        }

        // Extract the identifier
        if (start < end && IsRegisterStart(line[start]))
        {
            return line.Substring(start, end - start);
        }

        return string.Empty;
    }

    // Matches a Karel variable reference as written in a TP program:
    //   $[PROG]var            $[PROG].var          $[PROG]var.field
    //   $[PROG]arr[2].field   $[PROG]a.b[1].c
    // Group 1 captures the program bracket ($[PROG]); group 2 captures the
    // dotted field path (var.field[2]...). The optional '.' separator between
    // them is matched but dropped, so the result mirrors the symbol-table key
    // produced by TpSymbolTableBuilder.KarelVariableName ($[PROG]var.field).
    [GeneratedRegex(@"(\$\[([a-zA-Z_]\w*)\])\.?([a-zA-Z_]\w*(?:\[\d+\])?(?:\.[a-zA-Z_]\w*(?:\[\d+\])?)*)")]
    public static partial Regex KarelVariable();

    public static string GetKlVariableAt(string content, ContentPosition position)
    {
        var lines = content.Split('\n');
        if (position.Line < 0 || position.Line >= lines.Length)
        {
            return string.Empty;
        }

        var line = lines[position.Line];
        if (position.Character < 0 || position.Character > line.Length)
        {
            return string.Empty;
        }

        var match = KarelVariable().Matches(line);
        // A Karel variable spans the whole $[PROG]var.field token, so locate
        // every such reference on the line and return the one the cursor sits
        // inside, normalized to the symbol-table form ($[PROG]var.field).
        return match
                .FirstOrDefault(m => position.Character >= m.Index
                                     && position.Character <= m.Index + m.Length)
            switch
            {
                { Success: true } hit => hit.Groups[0].Value,
                _ => string.Empty
            };
    }

    // Matches a Karel variable access as written in source code: a base
    // identifier followed by any number of .field or [index] suffixes
    // (e.g. a.b[2].c). Used to find the datum the cursor is pointing at.
    [GeneratedRegex(@"[a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*|\[[^\]]*\])*")]
    public static partial Regex KlAccess();

    // Returns the base-rooted access path ending at the token under the cursor.
    // For "a.b.field" with the cursor on "field" this is "a.b.field"; with the
    // cursor on "b" it is "a.b". Returns "" when the cursor is not inside an
    // identifier access.
    public static string GetKlAccessPathAt(string content, ContentPosition position)
    {
        var lines = content.Split('\n');
        if (position.Line < 0 || position.Line >= lines.Length)
        {
            return string.Empty;
        }

        var line = lines[position.Line];
        if (position.Character < 0 || position.Character > line.Length)
        {
            return string.Empty;
        }

        var match = KlAccess().Matches(line)
            .FirstOrDefault(m => position.Character >= m.Index
                                 && position.Character <= m.Index + m.Length);
        if (match is not { Success: true })
        {
            return string.Empty;
        }

        // Truncate at the end of the identifier token under the cursor so the
        // path addresses exactly that datum, not the suffixes after it.
        var end = position.Character;
        while (end < line.Length && IsIdentifierChar(line[end]))
        {
            end++;
        }

        // If the cursor sits on an array index, include the closing bracket so
        // the extracted path stays well-formed (e.g. arr[2], not arr[2).
        if (end < line.Length && line[end] == ']')
        {
            end++;
        }

        return end > match.Index ? line.Substring(match.Index, end - match.Index) : string.Empty;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsRegisterStart(char c) => char.IsLetter(c);
    private static bool IsRegisterChar(char c) => IsIdentifierChar(c) || c == '[' || c == ']' || c == ':';

}

