using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Synto.Generators.Templating;
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

            [Template<Factory>]
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
        [Template<Factory>]
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
    // The child now carries a BARE [Template] (Phase 2): it is a parent-driven internal sub-template, built by the
    // parent and folded into the parent's SINGLE document as an `internal static` method on the shared target —
    // not an independent public sibling in its own document. Its inner Parameter<…>() calls (clrType, typeLabel)
    // still must NOT leak into the PARENT factory's parameter list.
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
        [Template<Factory>]
        public class Reader
        {
            private readonly System.Collections.IEnumerator _e = default!;

            [Template]
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

    // Same nested shape, but the child [Template] declares a [Splice] VALUE parameter (`childOnlySplice`) — a
    // finder kind (SpliceParameterFinder) that historically had NO per-site child-skip. When the PARENT carrier is
    // processed, the per-template finders must NOT visit the child's subtree, so `childOnlySplice` must never leak
    // into the parent factory's parameter list. Regression guard for the structural ownership boundary (skip-during).
    private const string NestedChildSpliceValueComposition =
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

        // The bare [Template] child is a PRIVATE SUB-TEMPLATE folded into the parent's single document — NOT an
        // independent public sibling. So there is exactly ONE generated document (the parent's) and NO separate
        // Factory.TypedGetter.g.cs.
        Assert.Contains("Factory.Reader.g.cs", sources.Keys);
        Assert.DoesNotContain("Factory.TypedGetter.g.cs", sources.Keys);
        Assert.Single(sources);

        var readerDocument = sources["Factory.Reader.g.cs"];

        // The parent factory is public; the sub-template is emitted as an `internal static` method named after the
        // child template (TypedGetter) on the shared target — proving the parent-driven sub-template shape.
        // Internal (not private) so a carrier's [Splice] source and sibling carriers can name it across classes.
        Assert.Contains("public static", readerDocument, StringComparison.Ordinal);
        Assert.Contains("internal static", readerDocument, StringComparison.Ordinal);
        Assert.Contains("TypedGetter", readerDocument, StringComparison.Ordinal);

        // The child's Parameter<string>() roots (clrType, typeLabel) belong to the CHILD sub-template's own
        // parameter list; they must NEVER leak into the PARENT factory's signature. (They may legitimately appear
        // inside the sub-template method body region, so this is asserted against the parent factory method only.)
        var parentFactory = ParentFactoryRegion(readerDocument);
        Assert.DoesNotContain("clrType", parentFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("typeLabel", parentFactory, StringComparison.Ordinal);

        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    // Returns the text of the public parent factory method (from `public static` up to the first `internal static`
    // sub-template that follows it) so leak assertions can target the parent's signature without matching the
    // folded-in child sub-template body.
    private static string ParentFactoryRegion(string document)
    {
        var start = document.IndexOf("public static", StringComparison.Ordinal);
        if (start < 0)
            return document;

        var subTemplateStart = document.IndexOf("internal static", start, StringComparison.Ordinal);
        return subTemplateStart < 0 ? document.Substring(start) : document.Substring(start, subTemplateStart - start);
    }

    // #11: a nested child [Template] whose body carries a facade-call error (here a named argument on a built-in
    // Member facade -> SY1029) must have that error reported EXACTLY ONCE — against the child's own factory pass.
    // Before FacadeCallFinder was template-scoped, the PARENT carrier's builder pass LINQ-scanned the whole carrier
    // (including the foreign child subtree), re-reporting the child's SY1029 a second time AND bailing the parent's
    // own factory. Scoping the finder skips the foreign child subtree: SY1029 fires once and the parent still
    // generates.
    private const string NestedChildBuilderErrorComposition =
        """
        using System;
        using Synto.Templating;
        using static Synto.Templating.Template;
        using Microsoft.CodeAnalysis.CSharp.Syntax;

        partial class Factory {}

        [Template<Factory>]
        public class Reader
        {
            private readonly object _obj = default!;

            // Child template: its body has a NAMED-ARG facade call (unsupported) -> SY1029.
            [Template<Factory>]
            public object Child([Splice] object instance)
            {
                return Member<object>(instance: instance, name: "Foo");
            }

            // The parent carrier's own (facade-clean) body — should still quote to a factory.
            void ParentBody()
            {
                System.Console.WriteLine(_obj);
            }
        }
        """;

    [Fact]
    public void ChildTemplate_BuilderErrorInChild_ReportedOnce_ParentStillGenerates()
    {
        var compilation = CompilationWithSource(NestedChildBuilderErrorComposition);
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var result = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        var runResult = result.GetRunResult();

        // The child's builder-arg error is reported exactly once (against the child), not doubled by the parent scan.
        Assert.Single(runResult.Diagnostics, d => d.Id == "SY1029");

        var sources = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

        // The parent factory still generates — the child's error no longer poisons the parent's builder pass.
        Assert.Contains("Factory.Reader.g.cs", sources.Keys);
    }

    [Fact]
    public void ChildTemplate_SpliceValueParam_DoesNotLeakIntoParentFactory()
    {
        var compilation = CompilationWithSource(NestedChildSpliceValueComposition);
        var driver = CSharpGeneratorDriver.Create(new TemplateFactorySourceGenerator());
        var result = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        var runResult = result.GetRunResult();

        // No generator diagnostics (a leaked/unhandled staged shape would surface here).
        Assert.Empty(runResult.Diagnostics);

        var sources = runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal);

        // The parent factory IS generated.
        Assert.Contains("Factory.Reader.g.cs", sources.Keys);
        var readerFactory = sources["Factory.Reader.g.cs"];

        // The child's [Splice] VALUE parameter belongs to the CHILD's own factory; it must NEVER surface in the
        // parent factory. Before the structural ownership boundary, the unguarded SpliceParameterFinder walked the
        // child subtree and leaked `childOnlySplice` into the parent factory's parameter list.
        Assert.DoesNotContain("childOnlySplice", readerFactory, StringComparison.Ordinal);
    }
}
