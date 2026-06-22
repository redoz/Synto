using System;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// Injects the small consumer-facing Synto marker surface (the marker types: <c>TemplateAttribute</c>,
/// <c>InlineAttribute</c>, <c>RuntimeAttribute</c>, <c>TemplateOption</c>, <c>Syntax</c>/<c>Syntax&lt;T&gt;</c>)
/// as <c>internal</c> source into the consumer's compilation. This lets a consumer depend on the
/// <c>Synto</c> generator package alone, with no reference to the <c>Synto.Core</c> runtime assembly.
/// </summary>
/// <remarks>
/// <para>
/// The marker surface is authored once in <c>src\Synto</c> and embedded into this assembly as a manifest
/// resource (see the <c>Synto.Runtime.*</c> &lt;EmbeddedResource&gt; items in the csproj). Each resource
/// is parsed and its top-level type/delegate declarations are rewritten from <c>public</c> to
/// <c>internal</c> before being emitted, so the single source of truth never drifts from what is injected.
/// </para>
/// <para>
/// The markers must stay <c>internal</c> (rather than <c>file</c>-scoped): a consumer references them
/// from their own hand-written files, and <see cref="TemplateFactorySourceGenerator"/>'s
/// <c>ForAttributeWithMetadataName</c> discovery needs the <c>TemplateAttribute</c> visible across the
/// whole compilation.
/// </para>
/// <para>
/// The two runtime HELPERS that actually appear in generated code (<c>ToSyntax</c> / <c>ToTypeSyntax</c>)
/// are deliberately NOT injected here. <see cref="TemplateFactorySourceGenerator"/> instead emits each as
/// a <c>file static class</c> into the single generated file that uses it (see <c>FileLocalHelpers</c>).
/// A <c>file</c> type is invisible across files, so the emitted helper can never collide with anything —
/// in particular not with <c>Synto.Core</c>'s public copies of the same extension methods when a consumer
/// references both — eliminating the CS0121 ambiguity an injected internal copy would otherwise cause.
/// </para>
/// <para>
/// Injection runs at post-initialization (unconditionally) so the injected <c>TemplateAttribute</c> is
/// visible to <see cref="TemplateFactorySourceGenerator"/>'s attribute discovery in the same pass; a
/// regular source output would not be. A real consumer therefore needs only the <c>Synto</c> generator
/// package and never references the runtime, so the injected internal surface is the only copy present.
/// </para>
/// <para>
/// Some Synto-internal test projects DO need the public <c>Synto.Core</c> assembly (its public marker
/// types must be referenceable from the in-memory compilations those tests build). They reference
/// <c>Synto.Core</c> through an <c>extern alias</c> so its public markers stay out of the default scope
/// and only the injected internal markers participate in normal name resolution (avoiding a CS0436-style
/// clash on the markers in the test assembly itself).
/// </para>
/// <para>
/// This is deliberately a SEPARATE generator from <see cref="TemplateFactorySourceGenerator"/>: the
/// in-memory snapshot tests instantiate that generator directly, and keeping injection separate keeps
/// their output stable.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class SurfaceInjectionGenerator : IIncrementalGenerator
{
    // The LogicalName prefix shared by every embedded surface resource (see csproj).
    private const string ResourcePrefix = "Synto.Runtime.";

    // The LogicalName prefix for the SHIFT GROUP: resources whose authored `namespace Synto[.*]` is
    // retargeted to `namespace Synto.Generators[.*]` on injection (in addition to public->internal). This
    // lets Synto's existing internal cacheability files (EquatableArray.cs / DiagnosticInfo.cs, namespace
    // `Synto`) be embedded AS-IS and injected downstream under the distinct `Synto.Generators` namespace,
    // so the single source of truth never drifts and the injected copy never collides with the IVT-visible
    // internal `Synto.*` copy. Must NOT be a prefix of (or prefixed by) ResourcePrefix.
    private const string ShiftResourcePrefix = "Synto.RuntimeGenerated.";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The rewritten surface is constant for a given generator assembly, so build it once.
        var surface = BuildSurface();

        context.RegisterPostInitializationOutput(productionContext =>
        {
            foreach (var file in surface)
            {
                productionContext.CancellationToken.ThrowIfCancellationRequested();
                productionContext.AddSource(file.HintName, file.Text);
            }
        });
    }

    private static ImmutableArray<InjectedFile> BuildSurface()
    {
        var assembly = typeof(SurfaceInjectionGenerator).Assembly;
        var builder = ImmutableArray.CreateBuilder<InjectedFile>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            string prefix;
            bool shiftNamespace;
            if (resourceName.StartsWith(ShiftResourcePrefix, StringComparison.Ordinal))
            {
                prefix = ShiftResourcePrefix;
                shiftNamespace = true;
            }
            else if (resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                prefix = ResourcePrefix;
                shiftNamespace = false;
            }
            else
            {
                continue;
            }

            string source = ReadResource(assembly, resourceName);

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            var rewritten = (CompilationUnitSyntax)new PublicToInternalRewriter().Visit(root);

            // Shift-group resources additionally retarget `namespace Synto[.*]` -> `Synto.Generators[.*]`,
            // so an embedded internal `Synto.*` file is injected downstream under `Synto.Generators`.
            if (shiftNamespace)
                rewritten = (CompilationUnitSyntax)new NamespaceShiftRewriter().Visit(rewritten);

            // The surface uses nullable reference annotations; emit it in an explicit `#nullable enable`
            // context so it compiles cleanly regardless of the consumer's project-level nullable setting.
            string text = "#nullable enable\n" + rewritten.GetText(Encoding.UTF8);

            // Hint name: drop the prefix so "Synto.Runtime.TemplateAttribute.cs" -> "TemplateAttribute.g.cs".
            string hintName = resourceName.Substring(prefix.Length);
            if (hintName.EndsWith(".cs", StringComparison.Ordinal))
                hintName = hintName.Substring(0, hintName.Length - ".cs".Length);

            builder.Add(new InjectedFile($"{hintName}.g.cs", text));
        }

        return builder.ToImmutable();
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            throw new InvalidOperationException($"Embedded surface resource '{resourceName}' was not found.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private readonly struct InjectedFile
    {
        public InjectedFile(string hintName, string text)
        {
            HintName = hintName;
            Text = text;
        }

        public string HintName { get; }

        public string Text { get; }
    }

    /// <summary>
    /// Rewrites the accessibility of type and delegate declarations from <c>public</c> to
    /// <c>internal</c>. Members (properties, methods, fields, ...) are left untouched: their authored
    /// accessibility is what the injected copy should keep, and an internal containing type already
    /// hides them from outside the consumer's assembly. The Synto surface only declares top-level
    /// types, so in practice this just flips each file's single public type to internal.
    /// </summary>
    private sealed class PublicToInternalRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
            => ReplacePublicWithInternal(node, node.Modifiers, node.WithModifiers);

        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node)
            => ReplacePublicWithInternal(node, node.Modifiers, node.WithModifiers);

        public override SyntaxNode? VisitEnumDeclaration(EnumDeclarationSyntax node)
            => ReplacePublicWithInternal(node, node.Modifiers, node.WithModifiers);

        public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
            => ReplacePublicWithInternal(node, node.Modifiers, node.WithModifiers);

        public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node)
            => ReplacePublicWithInternal(node, node.Modifiers, node.WithModifiers);

        public override SyntaxNode? VisitDelegateDeclaration(DelegateDeclarationSyntax node)
            => ReplacePublicWithInternal(node, node.Modifiers, node.WithModifiers);

        private static SyntaxNode ReplacePublicWithInternal<TNode>(
            TNode node,
            SyntaxTokenList modifiers,
            Func<SyntaxTokenList, TNode> withModifiers)
            where TNode : SyntaxNode
        {
            int index = modifiers.IndexOf(SyntaxKind.PublicKeyword);
            if (index < 0)
                return node;

            SyntaxToken oldToken = modifiers[index];
            SyntaxToken newToken = SyntaxFactory
                .Token(SyntaxKind.InternalKeyword)
                .WithTriviaFrom(oldToken);

            return withModifiers(modifiers.Replace(oldToken, newToken));
        }
    }

    /// <summary>
    /// Retargets a shift-group file's namespace from <c>Synto</c> (or <c>Synto.Foo</c>) to
    /// <c>Synto.Generators</c> (or <c>Synto.Generators.Foo</c>), leaving any non-<c>Synto</c> namespace
    /// untouched. Applied only to the designated shift-group resources so the embedded internal
    /// cacheability files (authored under <c>namespace Synto</c>) inject downstream under the distinct
    /// <c>Synto.Generators</c> namespace, while the marker surface keeps its authored namespaces.
    /// </summary>
    private sealed class NamespaceShiftRewriter : CSharpSyntaxRewriter
    {
        private const string FromRoot = "Synto";
        private const string ToRoot = "Synto.Generators";

        public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
            => node.WithName(Shift(node.Name));

        public override SyntaxNode? VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
            => node.WithName(Shift(node.Name));

        private static NameSyntax Shift(NameSyntax name)
        {
            string text = name.ToString();

            string shifted;
            if (text == FromRoot)
                shifted = ToRoot;
            else if (text.StartsWith(FromRoot + ".", StringComparison.Ordinal))
                shifted = ToRoot + text.Substring(FromRoot.Length);
            else
                return name; // not a Synto namespace — leave it untouched.

            return SyntaxFactory.ParseName(shifted).WithTriviaFrom(name);
        }
    }
}
