using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Synto.Test;

/// <summary>
/// Regression guard for T4: a Verify <c>*.verified.*</c> snapshot whose owning <c>[Fact]</c> no longer
/// exists is dead weight that silently accumulates. This test maps every snapshot filename in the project
/// back to a <c>{Type}.{Method}</c> pair and fails if the test method can't be found, so orphans can't
/// pile up unnoticed.
/// </summary>
public class SnapshotOrphanGuardTest
{
    [Fact]
    public void EverySnapshotMapsToATestMethod()
    {
        var projectDir = FindTestProjectDirectory();
        var assembly = typeof(SnapshotOrphanGuardTest).Assembly;

        var orphans = new List<string>();

        foreach (var file in Directory.EnumerateFiles(projectDir, "*.verified.*", SearchOption.AllDirectories))
        {
            // only the checked-in snapshot folders; never build output or transient .received files
            if (!file.Contains($"{Path.DirectorySeparatorChar}snapshots{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            var name = Path.GetFileName(file);

            var (typeName, methodName) = ParseSnapshotName(name);

            bool exists = assembly.GetTypes().Any(t =>
                string.Equals(t.Name, typeName, StringComparison.Ordinal)
                && t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Any(m => string.Equals(m.Name, methodName, StringComparison.Ordinal)));

            if (!exists)
                orphans.Add($"{name}  ->  {typeName}.{methodName} (no such test method)");
        }

        Assert.True(
            orphans.Count == 0,
            "Orphaned snapshot files (no corresponding test method) found:\n" + string.Join("\n", orphans));
    }

    /// <summary>
    /// Extracts the <c>{Type}.{Method}</c> pair encoded in a Verify snapshot filename, handling both the
    /// parameter/hint form (<c>Type.Method#Hint.g.verified.cs</c>) and the plain form
    /// (<c>Type.Method.verified.txt</c>).
    /// </summary>
    private static (string TypeName, string MethodName) ParseSnapshotName(string name)
    {
        string stem;

        int hash = name.IndexOf('#');
        if (hash >= 0)
        {
            stem = name.Substring(0, hash);
        }
        else
        {
            int verified = name.IndexOf(".verified", StringComparison.Ordinal);
            stem = name.Substring(0, verified);
            if (stem.EndsWith(".g", StringComparison.Ordinal))
                stem = stem.Substring(0, stem.Length - ".g".Length);
        }

        // stem == "{SimpleTypeName}.{MethodName}"; the simple type name carries no dot.
        int dot = stem.IndexOf('.');
        return (stem.Substring(0, dot), stem.Substring(dot + 1));
    }

    private static string FindTestProjectDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Synto.Test.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the Synto.Test project directory from " + AppContext.BaseDirectory);
    }
}
