using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// Supplies the runtime helper classes that <see cref="TemplateFactorySourceGenerator"/> can emit into
/// generated factory files (<c>ToSyntax</c> from <c>LiteralSyntaxExtensions</c>, <c>ToTypeSyntax</c> from
/// <c>RuntimeTypeExtensions</c>, and <c>OrNullLiteralExpression</c> from <c>QuoteSyntaxExtensions</c>),
/// keyed by the public extension-method name a generated factory would call.
/// </summary>
/// <remarks>
/// <para>
/// The helpers are authored once in <c>src\Synto</c> and embedded into this assembly as manifest
/// resources (the <c>Synto.Helper.*</c> items in the csproj). At emit time each is rewritten from a
/// <c>public</c> class in <c>namespace Synto;</c> into a <c>file static class</c> with no namespace, so a
/// private copy lives in the same compilation unit as the factory that uses it. Because <c>file</c>
/// types are invisible across files, this copy can never collide with anything — in particular not with
/// <c>Synto.Core</c>'s public copies of the same helpers when a consumer references both.
/// </para>
/// <para>
/// Emitting the helper into the factory's own (enclosing or global) namespace also means the
/// <c>value.ToSyntax()</c> / <c>typeof(T).ToTypeSyntax()</c> extension-method calls resolve with no
/// <c>using Synto;</c> at all, which is why the generated files no longer carry that using.
/// </para>
/// <para>
/// Which helper(s) actually get emitted into a given file is decided by SCANNING the generated factory
/// syntax for member-access calls to one of <see cref="Entries"/>' <c>MethodName</c> values (see
/// <see cref="TemplateFactorySourceGenerator"/>). Adding a future emitted helper is just adding its
/// embedded resource and a new <see cref="HelperEntry"/> here.
/// </para>
/// </remarks>
internal static class FileLocalHelpers
{
    // The LogicalName prefix shared by every embedded helper resource (see csproj). Distinct from the
    // "Synto.Runtime." prefix SurfaceInjectionGenerator consumes so the two surfaces stay separate.
    private const string ResourcePrefix = "Synto.Helper.";

    private const string LiteralResource = ResourcePrefix + "LiteralSyntaxExtensions.cs";
    private const string RuntimeTypeResource = ResourcePrefix + "RuntimeTypeExtensions.cs";
    private const string QuoteResource = ResourcePrefix + "QuoteSyntaxExtensions.cs";
    private const string CollectionResource = ResourcePrefix + "CollectionSyntaxExtensions.cs";
    private const string InterpolationResource = ResourcePrefix + "InterpolationSyntaxExtensions.cs";
    private const string IdentifierResource = ResourcePrefix + "IdentifierAttributeExtensions.cs";
    private const string VisibilityResource = ResourcePrefix + "VisibilityAttributeExtensions.cs";
    private const string SealedResource = ResourcePrefix + "SealedAttributeExtensions.cs";
    private const string ImplementsResource = ResourcePrefix + "ImplementsAttributeExtensions.cs";
    private const string InheritsResource = ResourcePrefix + "InheritsAttributeExtensions.cs";

    /// <summary>
    /// Every injectable helper, paired with the public extension-method name that, when found as a real
    /// member-access call in a generated factory, triggers injection of that helper's <c>file static class</c>.
    /// This is the single source of truth for the scan-based injection in
    /// <see cref="TemplateFactorySourceGenerator"/>.
    /// </summary>
    public static readonly ImmutableArray<HelperEntry> Entries =
    [
        // Scan keys taken from the Synto.Core helper symbols via nameof so a rename stays in lock-step with the
        // emit-sites (renaming a helper then becomes a generator compile error, not a silent injection miss).
        new HelperEntry(nameof(LiteralSyntaxExtensions.ToSyntax), HelperResourceLoader.Load(LiteralResource)),
        new HelperEntry(nameof(RuntimeTypeExtensions.ToTypeSyntax), HelperResourceLoader.Load(RuntimeTypeResource)),
        new HelperEntry(nameof(QuoteSyntaxExtensions.OrNullLiteralExpression), HelperResourceLoader.Load(QuoteResource)),
        new HelperEntry(nameof(CollectionSyntaxExtensions.BuildList), HelperResourceLoader.Load(CollectionResource)),
        new HelperEntry(nameof(InterpolationSyntaxExtensions.ToInterpolatedText), HelperResourceLoader.Load(InterpolationResource)),
        new HelperEntry(nameof(IdentifierAttributeExtensions.ApplyIdentifierAttribute), HelperResourceLoader.Load(IdentifierResource)),
        new HelperEntry(nameof(VisibilityAttributeExtensions.ApplyVisibilityAttribute), HelperResourceLoader.Load(VisibilityResource)),
        new HelperEntry(nameof(SealedAttributeExtensions.ApplySealedAttribute), HelperResourceLoader.Load(SealedResource)),
        new HelperEntry(nameof(ImplementsAttributeExtensions.ApplyImplementsAttribute), HelperResourceLoader.Load(ImplementsResource)),
        new HelperEntry(nameof(InheritsAttributeExtensions.ApplyInheritsAttribute), HelperResourceLoader.Load(InheritsResource)),
    ];

    /// <summary>An injectable helper: the public method name that triggers it plus its rewritten class.</summary>
    public readonly struct HelperEntry
    {
        public HelperEntry(string methodName, Helper helper)
        {
            MethodName = methodName;
            Helper = helper;
        }

        /// <summary>The public extension-method name a generated factory calls (e.g. <c>ToSyntax</c>).</summary>
        public string MethodName { get; }

        /// <summary>The <c>file static class</c> that provides <see cref="MethodName"/>.</summary>
        public Helper Helper { get; }
    }

    /// <summary>A rewritten helper: the using directives it needs plus its <c>file static class</c>.</summary>
    public readonly struct Helper
    {
        public Helper(SyntaxList<UsingDirectiveSyntax> usings, TypeDeclarationSyntax declaration)
        {
            Usings = usings;
            Declaration = declaration;
        }

        public SyntaxList<UsingDirectiveSyntax> Usings { get; }

        public TypeDeclarationSyntax Declaration { get; }
    }
}
