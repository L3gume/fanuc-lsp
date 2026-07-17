using System.Reflection;
using Xunit.Sdk;

namespace KarelParser.Tests;
internal class DirectoryDataAttribute(string directory) : DataAttribute
{

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        var expanded = Environment.ExpandEnvironmentVariables(directory);
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
