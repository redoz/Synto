using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Synto.Generators.Templating;

namespace Synto.Test;

/// <summary>
/// Plan Task 2 (#4): hint names are sanitized to a whitelist so illegal path characters (angle brackets from
/// generics, <c>?</c> from nullable types) never reach <c>AddSource</c>, and each generator's <c>Emit</c>
/// degrades a residual <c>AddSource</c> collision to a diagnostic instead of crashing the host.
/// </summary>
public class HintNameSanitizationTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);
    private static readonly MetadataReference RoslynReference = MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location);
    private static readonly MetadataReference RoslynCSharpReference = MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location);

    private static MetadataReference[] References =>
    [
        CorlibReference,
        NetStandardReference,
        SystemRuntimeReference,
        MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
        RoslynReference,
        RoslynCSharpReference,
        MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
        MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
    ];

    private static CSharpCompilation CompilationWithSource(string source) =>
        CSharpCompilation.Create("Test",
            [CSharpSyntaxTree.ParseText(source)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    // A [SyntaxBuilder] with an int? parameter: the facade hint is derived from the full signature, so without
    // sanitization the '?' from System.Int32? reaches AddSource. The facade must still be produced, with a
    // question-mark-free hint.
    [Fact]
    public void BuilderFacadeHint_NullableParam_HasNoQuestionMark()
    {
        var compilation = CompilationWithSource(
            """
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            using Synto.Templating;
            using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

            public static partial class MyBuilders {
                [SyntaxBuilder]
                public static ExpressionSyntax Cast([TypeArg, ReturnType] TypeSyntax t, [Quoted] ExpressionSyntax x, int? width) => CastExpression(t, x);
            }
            """);

        var driver = CSharpGeneratorDriver.Create(new SyntaxBuilderFacadeGenerator());
        var runResult = driver.RunGenerators(compilation, Xunit.TestContext.Current.CancellationToken).GetRunResult().Results.Single();

        Assert.Null(runResult.Exception);
        Assert.NotEmpty(runResult.GeneratedSources);
        Assert.DoesNotContain(runResult.GeneratedSources, s => s.HintName.Contains('?', System.StringComparison.Ordinal));
    }
}
