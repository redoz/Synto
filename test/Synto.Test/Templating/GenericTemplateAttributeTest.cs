using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Synto.Generators.Templating;
namespace Synto.Test.Templating;

/// <summary>
/// Task 1 facts for the generic <c>[Template&lt;T&gt;]</c> discovery path. The generic attribute lands ALONGSIDE
/// the existing <c>[Template(typeof(T))]</c> ctor form: it must derive the factory target from its type argument
/// and flow through the identical <c>GenerateTemplate</c>/<c>Emit</c> shape, so a top-level generic template is
/// byte-identical to the same template written with the <c>typeof</c> ctor. Also proves carrier/child recognition
/// (<see cref="ChildTemplateFinder"/>/<c>TemplateScope</c>) recognizes the generic carrier + generic nested child.
/// </summary>
public class GenericTemplateAttributeTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    private static readonly MetadataReference[] References =
    [
        CorlibReference,
        NetStandardReference,
        SystemRuntimeReference,
        MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Collections.Immutable.ImmutableArray).Assembly.Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Linq, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Collections, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime.Extensions, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location),
    ];

    private static Compilation CompilationWithSource(string source)
    {
        MetadataReference[] references = [.. References, MetadataReference.CreateFromFile(SyntoCoreAssembly.Location)];
        return CSharpCompilation.Create("GenericTemplateAttributeTest",
            [CSharpSyntaxTree.ParseText(source)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static Dictionary<string, string> RunGenerator(string source)
    {
        var compilation = CompilationWithSource(source);
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var result = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var runResult = result.GetRunResult();
        Assert.Empty(runResult.Diagnostics);
        return runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);
    }

    private const string GenericForm =
        """
        using System;
        using Synto.Templating;

        partial class Factory {}

        public class TestClass
        {
            [Template<Factory>]
            static void Sample()
            {
                Console.WriteLine("Hello world");
            }
        }
        """;

    private const string GenericBareForm =
        """
        using System;
        using Synto.Templating;

        partial class Factory {}

        public class TestClass
        {
            [Template<Factory>(Options = TemplateOption.Bare)]
            static void Sample()
            {
                Console.WriteLine("Hello world");
            }
        }
        """;

    // Generic-form clone of ChildTemplateTest.NestedChildSpliceValueComposition: carrier + nested child both on
    // [Template<Factory>]. Proves ChildTemplateFinder/TemplateScope recognize the GENERIC carrier + generic nested
    // child (matched on the attribute's original definition), so the child's [Splice] value param never leaks.
    private const string GenericNestedChildSpliceValueComposition =
        """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using Synto.Templating;
        using static Synto.Templating.Template;
        using Microsoft.CodeAnalysis.CSharp.Syntax;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
        using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

        partial class Factory {}

        public readonly record struct Col(int Ordinal, string Name, string ClrType);

        [Template<Factory>]
        public class Reader
        {
            [Template<Factory>]
            public TRet TypedGetter<[Splice] TRet>(int i, [Splice] object childOnlySplice)
            {
                var columns = Parameter<IReadOnlyList<Col>>();
                var clrType = Parameter<string>();
                var typeLabel = Parameter<string>();
                foreach (var c in columns.Where(c => c.ClrType == clrType))
                    if (i == c.Ordinal)
                        return Member<TRet>(childOnlySplice, c.Name);
                throw new global::System.InvalidCastException($"Field {i} is not {typeLabel} column.");
            }

            [Splice]
            static IEnumerable<MemberDeclarationSyntax> Getters()
            {
                var columns = Parameter<IReadOnlyList<Col>>();
                yield return Factory.TypedGetter(PredefinedType(Token(IntKeyword)), columns, "System.Int32", "an Int32")
                    .WithIdentifier(Identifier("GetInt32"));
            }
        }
        """;

    [Fact]
    public void GenericForm_ProducesFactory()
    {
        // The ctor form [Template(typeof(T))] is gone (Phase 1 clean break), so the generic form is now the only
        // way to pin a target. It must still generate a factory document.
        var genericSources = RunGenerator(GenericForm);

        Assert.NotEmpty(genericSources);
    }

    [Fact]
    public void GenericForm_HonorsBareOption()
    {
        // Options = Bare changes the generated factory body, so the Bare form's output differs from the default.
        var defaultSources = RunGenerator(GenericForm);
        var bareSources = RunGenerator(GenericBareForm);

        Assert.NotEmpty(bareSources);
        Assert.NotEqual(defaultSources, bareSources);
    }

    [Fact]
    public void GenericNestedChild_SpliceValueParam_DoesNotLeakIntoParentFactory()
    {
        var sources = RunGenerator(GenericNestedChildSpliceValueComposition);

        Assert.Contains("Factory.Reader.g.cs", sources.Keys);
        Assert.DoesNotContain("childOnlySplice", sources["Factory.Reader.g.cs"], StringComparison.Ordinal);
    }
}
