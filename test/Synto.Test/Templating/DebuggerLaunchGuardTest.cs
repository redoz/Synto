using System.IO;
using System.Text.RegularExpressions;

namespace Synto.Test.Templating;

/// <summary>
/// Regression guard for C1: a stray, uncommented <c>Debugger.Launch()</c> on a reachable generation path
/// pops a modal attach dialog inside a consumer's compiler/IDE/CI. This test scans the whole <c>src/</c>
/// tree and fails if any uncommented <c>Debugger.Launch</c> survives outside a <c>#if DEBUG</c> guard, so
/// debug residue can never ship again.
/// </summary>
public class DebuggerLaunchGuardTest
{
    [Fact]
    public void NoUncommentedDebuggerLaunchInSource()
    {
        var srcDir = FindSrcDirectory();

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            // skip build output
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            // strip /* ... */ block comments so a Debugger.Launch inside one is not flagged
            var text = Regex.Replace(File.ReadAllText(file), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

            // track #if DEBUG regions (a debug-guarded Debugger.Launch is legitimate)
            int debugDepth = 0;
            var ifStack = new Stack<bool>();

            int lineNumber = 0;
            foreach (var rawLine in text.Split('\n'))
            {
                lineNumber++;
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("#if", StringComparison.Ordinal))
                {
                    bool isDebug = Regex.IsMatch(trimmed, @"\bDEBUG\b");
                    ifStack.Push(isDebug);
                    if (isDebug) debugDepth++;
                    continue;
                }
                if (trimmed.StartsWith("#endif", StringComparison.Ordinal))
                {
                    if (ifStack.Count > 0 && ifStack.Pop()) debugDepth--;
                    continue;
                }

                // only consider the code portion before any line comment
                int commentIndex = line.IndexOf("//", StringComparison.Ordinal);
                string code = commentIndex >= 0 ? line.Substring(0, commentIndex) : line;

                if (code.Contains("Debugger.Launch", StringComparison.Ordinal) && debugDepth == 0)
                {
                    offenders.Add($"{file}:{lineNumber}: {trimmed}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Uncommented Debugger.Launch outside #if DEBUG found in source:\n" + string.Join("\n", offenders));
    }

    private static string FindSrcDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var src = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(src) && File.Exists(Path.Combine(dir.FullName, "global.json")))
                return src;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository 'src' directory from " + AppContext.BaseDirectory);
    }
}
