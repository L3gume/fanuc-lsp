using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit.Sdk;

namespace KarelParser.Tests;
internal class DirectoryDataAttribute(string directory, [CallerFilePath] string callerFilePath = "") : DataAttribute
{

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        // Anchor the directory to the source file this attribute is applied in,
        // so it resolves regardless of the working directory the tests run from.
        var baseDir = Path.GetDirectoryName(callerFilePath) ?? Directory.GetCurrentDirectory();
        var expanded = Path.GetFullPath(Path.Combine(baseDir, Environment.ExpandEnvironmentVariables(directory)));
        if (!Directory.Exists(expanded))
        {
            Console.Error.WriteLine($"Directory not found: {expanded}");
            return [];
        }

        // Top-level only: subdirectories (e.g. "Include") hold %INCLUDE fragments
        // that aren't complete standalone programs, so they're excluded from this
        // "every file here must parse as a full PROGRAM" check.
        var files = Directory.GetFiles(expanded, "*.kl", SearchOption.TopDirectoryOnly);
        return files.Select(fileStr => new object[] { fileStr });
    }
}
