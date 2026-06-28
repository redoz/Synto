using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Durable proof of the interpolation staged-fold (spec 2026-06-28): a BARE staged-string interpolation hole
/// (no alignment/format clause, in a regular <c>$"…"</c> string) is baked into its surrounding literal text at
/// factory-build time via <c>value.ToInterpolatedText()</c>, instead of being re-emitted as a runtime hole.
/// Runtime output is unchanged — only the generated factory source changes. Harness modeled on
/// <see cref="SimpleTemplateTest"/>.
/// </summary>
public class InterpolationFoldTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    private static CSharpCompilation CreateCompilation()
    {
        return CSharpCompilation.Create("Test",
            Array.Empty<SyntaxTree>(),
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location)
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    private readonly Compilation _baseCompilation;
    private readonly GeneratorDriver _driver;

    public InterpolationFoldTest()
    {
        _baseCompilation = CreateCompilation();
        _driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
    }

    private Compilation CompilationWithSource(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());
        return _baseCompilation.AddSyntaxTrees(syntaxTree);
    }

    private async Task VerifyTemplate(string source)
    {
        var compilation = CompilationWithSource(source);
        Assert.Empty(compilation.GetDiagnostics().Where(diag => diag.Severity == DiagnosticSeverity.Error));
        var result = _driver.RunGenerators(compilation);
        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    [Fact]
    public async Task BareStagedString_ParameterRoot_FoldsIntoLiteralText()
    {
        // `label` is a string-typed Parameter<string>() staged root; `i` is a genuine runtime parameter.
        // Expected: {label} is baked into the surrounding literal text (fused via label.ToInterpolatedText());
        // {i} stays a runtime hole.
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;
            using static Synto.Templating.Template;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory))]
                void LocalFunction(int i) {
                    var label = Parameter<string>();
                    Console.WriteLine($"Field {i} is not {label} column.");
                }
            }
            """
        );
    }

    [Fact]
    public async Task BareStagedString_UnquoteLocal_FoldsIntoLiteralText()
    {
        // `label` is a string-typed Unquote<string>(...) staged local; `i` is a genuine runtime parameter.
        // Same fold as the Parameter<string> path (the second, independently-fallible channel-population path).
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;
            using static Synto.Templating.Template;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory))]
                void LocalFunction(int i) {
                    var label = Unquote<string>("Boolean");
                    Console.WriteLine($"Field {i} is not {label} column.");
                }
            }
            """
        );
    }

    [Fact]
    public async Task Escaping_HazardCharacters_RoundTrip()
    {
        // The literal runs flanking the foldable hole contain every interpolated-text hazard class:
        // escaped braces ({{ }}), a quote (\") and a backslash (\\). The fold must preserve these
        // escapes byte-for-byte in the fused InterpolatedStringText token (Text = raw escaped form,
        // ValueText = decoded form); the staged value is escaped at factory-build time via
        // label.ToInterpolatedText(). Nothing here may produce malformed literal text.
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;
            using static Synto.Templating.Template;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory))]
                void LocalFunction() {
                    var label = Parameter<string>();
                    Console.WriteLine($"pre {{ }} \" \\ {label} post");
                }
            }
            """
        );
    }

    [Fact]
    public async Task RuntimeHole_IsLeftUntouched()
    {
        // `i` is a genuine runtime parameter (not a staged root), so $"x {i} y" must keep {i} as a
        // runtime interpolation hole — no fold, no string channel membership.
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;
            using static Synto.Templating.Template;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory))]
                void LocalFunction(int i) {
                    Console.WriteLine($"x {i} y");
                }
            }
            """
        );
    }

    [Fact]
    public async Task NonStringStaged_IsLeftUntouched()
    {
        // `n` is a staged root but NOT string-typed (Parameter<int>()); `flag` is a staged Unquote<bool>.
        // Both are in _unquotedReplacements but absent from the string channel, so their bare holes stay
        // runtime holes (non-goal: only string staged roots fold in v1).
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;
            using static Synto.Templating.Template;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory))]
                void LocalFunction() {
                    var n = Parameter<int>();
                    var flag = Unquote<bool>(true);
                    Console.WriteLine($"count {n} flag {flag}");
                }
            }
            """
        );
    }

    [Fact]
    public async Task StagedString_WithFormatOrAlignment_IsLeftUntouched()
    {
        // A string staged root with a format clause ({label:N2}) and one with an alignment clause
        // ({label,5}) are both left as holes — the predicate requires no AlignmentClause and no
        // FormatClause (non-goal: formatted/aligned holes never fold in v1).
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;
            using static Synto.Templating.Template;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory))]
                void LocalFunction() {
                    var label = Parameter<string>();
                    Console.WriteLine($"fmt {label:N2} align {label,5} end");
                }
            }
            """
        );
    }

    [Fact]
    public async Task StagedString_InVerbatimOrRawString_IsLeftUntouched()
    {
        // v1 targets regular `$"…"` strings only. A bare staged-string hole inside a verbatim ($@"…")
        // and inside a raw ($"""…""") interpolated string must stay a runtime hole (deferred to base).
        await VerifyTemplate(
            "using System;\n" +
            "using Synto.Templating;\n" +
            "using static Synto.Templating.Template;\n" +
            "\n" +
            "partial class Factory {}\n" +
            "\n" +
            "public class TestClass {\n" +
            "    [Template(typeof(Factory))]\n" +
            "    void LocalFunction() {\n" +
            "        var label = Parameter<string>();\n" +
            "        Console.WriteLine($@\"verbatim {label} text\");\n" +
            "        Console.WriteLine($\"\"\"raw {label} text\"\"\");\n" +
            "    }\n" +
            "}\n"
        );
    }

    [Fact]
    public async Task Mixed_FoldsOnlyStagedPart()
    {
        // $"a {runtime} b {label} c" folds only the staged-string hole {label}; the runtime hole
        // {runtime} and both surrounding literal runs survive (the run breaks at the runtime hole).
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;
            using static Synto.Templating.Template;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory))]
                void LocalFunction(int runtime) {
                    var label = Parameter<string>();
                    Console.WriteLine($"a {runtime} b {label} c");
                }
            }
            """
        );
    }

    [Fact]
    public async Task Boundary_StartEnd_AndAdjacentHoles_FoldIndependently()
    {
        // A foldable hole at the string START ($"{a} start"), a foldable hole at the END ($"end {b}"),
        // two adjacent foldable holes ($"{a}{b}"), and a foldable hole flanked by a runtime hole
        // ($"{a}{i}{b}") each fold into the adjoining (possibly empty) literal text; the runtime hole
        // breaks the run so the two staged holes fold independently around it.
        await VerifyTemplate(
            """
            using System;
            using Synto.Templating;
            using static Synto.Templating.Template;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory))]
                void LocalFunction(int i) {
                    var a = Parameter<string>();
                    var b = Parameter<string>();
                    Console.WriteLine($"{a} start");
                    Console.WriteLine($"end {b}");
                    Console.WriteLine($"{a}{b}");
                    Console.WriteLine($"{a}{i}{b}");
                }
            }
            """
        );
    }
}
