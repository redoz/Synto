using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

// Mirrors SwitchBuilderTest's harness: SurfaceInjection + TemplateFactory generators over a [Template] carrier.
public class QualificationTemplateTest
{
    private static readonly MetadataReference[] References =
    [
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("netstandard, Version=2.0.0.0").Location),
        MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
        MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
    ];

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Text;
        using Synto.Templating;
        using static Synto.Templating.Template;

        partial class Factory {}

        [Template<Factory>]
        public class Carrier
        {
            public Type Describe()
            {
                Type t = typeof(int);
                object boxed = DBNull.Value;
                List<StringBuilder> nums = new List<StringBuilder>();
                GC.KeepAlive(nums);
                return t;
            }
        }
        """;

    [Fact]
    public void QualifiesFrameworkNamesInGeneratedFactory()
    {
        var compilation = CSharpCompilation.Create("Test",
            [CSharpSyntaxTree.ParseText(Source, cancellationToken: TestContext.Current.CancellationToken)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(
            new SurfaceInjectionGenerator(),
            new TemplateFactorySourceGenerator());
        var runResult = driver.RunGenerators(compilation, TestContext.Current.CancellationToken).GetRunResult();

        var factory = runResult.Results.Single(r => r.Generator.GetGeneratorType() == typeof(TemplateFactorySourceGenerator));
        var factorySource = factory.GeneratedSources
            .Single(g => g.HintName.StartsWith("Factory.", StringComparison.Ordinal))
            .SourceText.ToString();

        // The quoter emits each NameSyntax STRUCTURALLY as SyntaxFactory calls (not as a literal "global::..."
        // string), so a qualified `global::System.Type` renders as
        //   QualifiedName(AliasQualifiedName(IdentifierName("global"), ::, IdentifierName("System")), IdentifierName("Type")).
        // Assert against the whitespace-normalized structural fragment for each case: absent qualification these
        // would be a bare `IdentifierName("Type")` with no global::-rooted AliasQualifiedName prefix.
        var normalized = System.Text.RegularExpressions.Regex.Replace(factorySource, @"\s+", "");
        const string globalSystem = "AliasQualifiedName(IdentifierName(\"global\"),Token(ColonColonToken),IdentifierName(\"System\"))";
        Assert.Contains(globalSystem + ",IdentifierName(\"Type\"))", normalized, StringComparison.Ordinal);
        Assert.Contains(globalSystem + ",IdentifierName(\"DBNull\"))", normalized, StringComparison.Ordinal);
        // Generic name: the qualifier prefix (global::System.Collections.Generic) is composed in front of the
        // (base-quoted) authored `List<...>` GenericName, exercising the QualifyStep compose path — the authored
        // GenericName (with its TypeArgumentList) survives, so a nested [Splice] type arg's ReplaceStep would fire.
        Assert.Contains("IdentifierName(\"Generic\")),GenericName(Identifier(\"List\"),", normalized, StringComparison.Ordinal);
        // Regression (deep-review finding #1): a CONCRETE short-named type ARGUMENT must be qualified too. The
        // finder descends into generic type args, so `StringBuilder` gets its own substitute; absent that it would
        // render bare (CS0246 in a consumer without `using System.Text;`), defeating self-containment.
        Assert.Contains(globalSystem + ",Token(DotToken),IdentifierName(\"Text\")),IdentifierName(\"StringBuilder\"))", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void QualificationIsCacheable()
    {
        // Adding ResolveQualifications inside the transform must introduce NO new pipeline step and capture no
        // SemanticModel/ISymbol: after an unrelated edit the tracked template steps must stay Cached/Unchanged.
        var compilation = CSharpCompilation.Create("Test",
            [CSharpSyntaxTree.ParseText(Source, cancellationToken: TestContext.Current.CancellationToken)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new TemplateFactorySourceGenerator().AsSourceGenerator() },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        var modified = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { internal sealed class Unrelated { } }", cancellationToken: TestContext.Current.CancellationToken));
        driver = driver.RunGenerators(modified, TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();

        // The [Template<Factory>] carrier drives the GENERIC template pipeline.
        CacheabilityAssert.AllStepsCachedOrUnchanged(
            result,
            new[] { TemplateTrackingNames.GenericTransform, TemplateTrackingNames.GenericResult });
    }
}
