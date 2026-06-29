using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// One negative case per decoration diagnostic SY1022–SY1028 (post-quote declaration decorations). Each feeds
/// deliberately-malformed Synto usage through <see cref="TemplateFactorySourceGenerator"/>, asserts the
/// diagnostic Id off the driver's diagnostics, plus a real, non-empty source span (the squiggle must point at a
/// real span, not <see cref="Location.None"/>). The offending decoration is dropped (accumulate-and-continue),
/// so the rest of the template still generates.
/// </summary>
public class DecorationDiagnosticsTests
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create("Test",
            [],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
                // The user-defined-decoration negative cases (SY1026–SY1028) author an ApplyFooAttribute hook
                // constrained on TypeDeclarationSyntax, so the consumer source needs the Roslyn syntax types.
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private readonly Compilation _baseCompilation = CreateCompilation();
    private readonly GeneratorDriver _driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());

    private ImmutableArray<Diagnostic> RunAndGetDiagnostics(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());

        var compilation = _baseCompilation.AddSyntaxTrees(syntaxTree);
        var result = _driver.RunGenerators(compilation);
        return result.GetRunResult().Diagnostics;
    }

    private static void AssertHasRealSpan(Diagnostic diag)
    {
        Assert.NotEqual(Location.None, diag.Location);
        Assert.False(diag.Location.SourceSpan.IsEmpty);
    }

    // SY1022 — marker applied to a node whose kind is NOT assignable to the hook's this-type.
    // [Implements<T>]'s this-type is TypeDeclarationSyntax; a [Template] METHOD is not a TypeDeclarationSyntax.
    [Fact]
    public void ImplementsOnMethod_ReportsSY1022()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                [Implements<global::System.IDisposable>]
                void Build() { System.Console.WriteLine("hi"); }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1022");
        AssertHasRealSpan(diag);
    }

    // SY1023 — [Visibility(Access.File)] on a non-top-level declaration (file is top-level only).
    // The nested Shell is non-top-level relative to the carrier root.
    [Fact]
    public void VisibilityFileOnNested_ReportsSY1023()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                class Shell {
                    [Visibility(Access.File)]
                    class Inner {}
                }
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1023");
        AssertHasRealSpan(diag);
    }

    // SY1024 — [Implements<T>] where T is not an interface (System.String is a sealed class).
    [Fact]
    public void ImplementsNonInterface_ReportsSY1024()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                [Implements<global::System.String>]
                class Shell {}
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1024");
        AssertHasRealSpan(diag);
    }

    // SY1025 — conflicting/duplicate decorations on one node: two [Visibility] (AllowMultiple = false at the
    // C# level, but the diagnostic is symbol-driven; use two distinct visibility markers via separate lists).
    [Fact]
    public void DuplicateVisibility_ReportsSY1025()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                [Visibility(Access.Internal)]
                [Visibility(Access.Public)]
                class Shell {}
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1025");
        AssertHasRealSpan(diag);
    }

    // SY1026 — a decoration attribute type declares a settable property (must be constructor-parameters only).
    [Fact]
    public void SettablePropOnUserAttr_ReportsSY1026()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;

            public sealed class FooAttribute : System.Attribute
            {
                public FooAttribute(string tag) { Tag = tag; }
                public string Tag { get; }
                public string Bar { get; set; }
            }

            public static class FooExt
            {
                public static T ApplyFooAttribute<T>(this T node, string tag) where T : TypeDeclarationSyntax => node;
            }

            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                [Foo("hi")]
                class Shell {}
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1026");
        AssertHasRealSpan(diag);
    }

    // SY1027 — no ApplyXxxAttribute *extension* method resolvable for a [Xxx] marker. The method exists by name
    // (so [Foo] is recognized as a decoration candidate) but is declared as a non-extension instance method.
    [Fact]
    public void MissingApplyForUserAttr_ReportsSY1027()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;

            public sealed class FooAttribute : System.Attribute
            {
                public FooAttribute(string tag) { Tag = tag; }
                public string Tag { get; }
            }

            public static class FooExt
            {
                // Same name, but NOT an extension method (no `this`) => not resolvable as a decoration hook.
                public static TypeDeclarationSyntax ApplyFooAttribute(TypeDeclarationSyntax node, string tag) => node;
            }

            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                [Foo("hi")]
                class Shell {}
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1027");
        AssertHasRealSpan(diag);
    }

    // SY1028 — a resolvable ApplyXxxAttribute whose return type != its this-parameter type (breaks composition).
    [Fact]
    public void ApplyWrongReturn_ReportsSY1028()
    {
        var diagnostics = RunAndGetDiagnostics(
            """
            using Synto.Templating;
            using Microsoft.CodeAnalysis.CSharp.Syntax;

            public sealed class FooAttribute : System.Attribute
            {
                public FooAttribute(string tag) { Tag = tag; }
                public string Tag { get; }
            }

            public static class FooExt
            {
                // Resolvable extension, but returns object (not T / not the this-type) => non-composing.
                public static object ApplyFooAttribute<T>(this T node, string tag) where T : TypeDeclarationSyntax => node;
            }

            partial class Factory {}
            public class Holder {
                [Template(typeof(Factory))]
                [Foo("hi")]
                class Shell {}
            }
            """);

        var diag = Assert.Single(diagnostics, d => d.Id == "SY1028");
        AssertHasRealSpan(diag);
    }
}
