using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Durable proof of the "a template invoking a template" composition pattern: a standalone child
/// <c>[Template]</c> getter with a method-level <c>[Splice]</c> return type
/// (<c>TRet TypedGetter&lt;[Splice] TRet&gt;(...)</c>), invoked by a sibling <c>[Splice]</c> member-generator
/// that renames each result via <c>.WithIdentifier(...)</c>. Exercises <c>Member&lt;TRet&gt;</c> where
/// <c>TRet</c> is a <c>[Splice]</c> type param. Compilation setup mirrors
/// <see cref="SpliceMemberGeneratorTest"/> in this same folder.
/// </summary>
public class ChildTemplateTest
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

    // Standalone child template invoked by a [Splice] member-generator that renames each result.
    private const string ChildGetterComposition =
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

        // Standalone child template: one typed getter, return type supplied per call via a [Splice] type param.
        // The inert `_e` lets the carrier compile alone; only `_e.Current` is quoted into output.
        internal sealed class GetterTemplate
        {
            private readonly System.Collections.IEnumerator _e = default!;

            [Template(typeof(Factory))]
            public TRet TypedGetter<[Splice] TRet>(int i)
            {
                var columns = Parameter<IReadOnlyList<Col>>();
                var clrType = Parameter<string>();
                var typeLabel = Parameter<string>();
                foreach (var c in columns.Where(c => c.ClrType == clrType))
                    if (i == c.Ordinal)
                        return Member<TRet>(_e.Current, c.Name);
                throw new global::System.InvalidCastException($"Field {i} is not {typeLabel} column.");
            }
        }

        // Parent carrier: a [Splice] member-generator invokes the child factory per getter and renames each result.
        [Template(typeof(Factory))]
        public class Reader
        {
            [Splice]
            static IEnumerable<MemberDeclarationSyntax> Getters()
            {
                var columns = Parameter<IReadOnlyList<Col>>();
                yield return Factory.TypedGetter(PredefinedType(Token(IntKeyword)), columns, "System.Int32", "an Int32")
                    .WithIdentifier(Identifier("GetInt32"));
                yield return Factory.TypedGetter(PredefinedType(Token(StringKeyword)), columns, "System.String", "a String")
                    .WithIdentifier(Identifier("GetString"));
            }
        }
        """;

    private static Compilation CompilationWithSource(string source)
    {
        MetadataReference[] references = [.. References, MetadataReference.CreateFromFile(SyntoCoreAssembly.Location)];
        return CSharpCompilation.Create("ChildTemplateSnapshotTest",
            [CSharpSyntaxTree.ParseText(source)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    // Same composition, but the child [Template] (TypedGetter) and its inert `_e` live INSIDE the Reader
    // carrier (which still hosts the [Splice] Getters() member-generator that invokes Factory.TypedGetter).
    // Exercises Capability 1: a method-level [Template] nested in a class-level [Template] is a sibling child
    // template — trimmed from the parent's quoted output, and its inner Parameter<…>() calls (clrType,
    // typeLabel) must NOT leak into the parent factory's parameter list.
    private const string NestedChildComposition =
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

        // Parent carrier with the child [Template] nested INSIDE it; the real `_e` binds here.
        [Template(typeof(Factory))]
        public class Reader
        {
            private readonly System.Collections.IEnumerator _e = default!;

            [Template(typeof(Factory))]
            public TRet TypedGetter<[Splice] TRet>(int i)
            {
                var columns = Parameter<IReadOnlyList<Col>>();
                var clrType = Parameter<string>();
                var typeLabel = Parameter<string>();
                foreach (var c in columns.Where(c => c.ClrType == clrType))
                    if (i == c.Ordinal)
                        return Member<TRet>(_e.Current, c.Name);
                throw new global::System.InvalidCastException($"Field {i} is not {typeLabel} column.");
            }

            [Splice]
            static IEnumerable<MemberDeclarationSyntax> Getters()
            {
                var columns = Parameter<IReadOnlyList<Col>>();
                yield return Factory.TypedGetter(PredefinedType(Token(IntKeyword)), columns, "System.Int32", "an Int32")
                    .WithIdentifier(Identifier("GetInt32"));
                yield return Factory.TypedGetter(PredefinedType(Token(StringKeyword)), columns, "System.String", "a String")
                    .WithIdentifier(Identifier("GetString"));
            }
        }
        """;

    [Fact]
    public async Task ChildTemplate_MemberGeneratorInvokesChildFactory()
    {
        var compilation = CompilationWithSource(ChildGetterComposition);
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var result = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    [Fact]
    public async Task ChildTemplate_NestedInParentCarrier()
    {
        var compilation = CompilationWithSource(NestedChildComposition);
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var result = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        var runResult = result.GetRunResult();

        // No generator diagnostics (a leaked/unhandled staged shape would surface here).
        Assert.Empty(runResult.Diagnostics);

        var sources = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

        // A factory IS generated for the nested child template.
        Assert.Contains("Factory.TypedGetter.g.cs", sources.Keys);

        // The parent factory IS generated.
        Assert.Contains("Factory.Reader.g.cs", sources.Keys);
        var readerFactory = sources["Factory.Reader.g.cs"];

        // (b) The child template is NOT quoted as a member of the parent's output: neither its quoted method
        // declaration (Identifier("TypedGetter")) nor its body marker (the InvalidCastException it throws) nor
        // any residual Parameter<…> call from the child body appears. NOTE: the retained [Splice] Getters()
        // member-generator legitimately CALLS Factory.TypedGetter(...) at factory time, so a bare "TypedGetter"
        // substring is expected — what must be absent is the child quoted AS a member declaration.
        Assert.DoesNotContain("Identifier(\"TypedGetter\")", readerFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidCastException", readerFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter<", readerFactory, StringComparison.Ordinal);

        // (c) The child's Parameter<string>() roots (clrType, typeLabel) do NOT leak into the parent factory's
        // parameter list (they appear nowhere in the parent factory). The shared columns parameter (also declared
        // in the parent's Getters member-generator) is legitimately present and is NOT asserted away.
        Assert.DoesNotContain("clrType", readerFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("typeLabel", readerFactory, StringComparison.Ordinal);

        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }
}
