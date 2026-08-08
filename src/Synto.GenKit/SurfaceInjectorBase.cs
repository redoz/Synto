using System;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.GenKit;

/// <summary>
/// Injects a small consumer-facing marker surface (marker types such as <c>TemplateAttribute</c>,
/// <c>UnquoteAttribute</c>, <c>SpliceAttribute</c>, <c>RuntimeAttribute</c>, <c>TemplateOption</c>)
/// as <c>internal</c> source into the consumer's compilation. This lets a consumer depend on the
/// <c>Synto</c> generator package alone, with no reference to the <c>Synto.Core</c> runtime assembly.
/// </summary>
/// <remarks>
/// <para>
/// The marker surface is authored once in <c>src\Synto.Core</c> and embedded into the OWNING generator
/// assembly as a manifest resource (see the <c>Synto.Runtime.*</c> &lt;EmbeddedResource&gt; items in the
/// owning csproj). Each concrete subclass scans <see cref="object.GetType"/>'s assembly, so it injects
/// exactly the surface embedded into the DLL that declares it. Each resource is parsed and its top-level
/// type/delegate declarations are rewritten from <c>public</c> to <c>internal</c> before being emitted, so
/// the single source of truth never drifts from what is injected.
/// </para>
/// <para>
/// The markers must stay <c>internal</c> (rather than <c>file</c>-scoped): a consumer references them
/// from their own hand-written files, and the factory generators' <c>ForAttributeWithMetadataName</c>
/// discovery needs the marker types visible across the whole compilation.
/// </para>
/// <para>
/// Injection runs at post-initialization (unconditionally) so injected marker attributes are visible to
/// the factory generators' attribute discovery in the same pass; a regular source output would not be.
/// </para>
/// <para>
/// This is deliberately kept SEPARATE from the factory generators: the in-memory snapshot tests
/// instantiate those generators directly, and keeping injection separate keeps their output stable.
/// </para>
/// <para>
/// Subclasses carry the <c>[Generator]</c> attribute and, if their embedded surface uses a different
/// logical-name prefix, override <see cref="ResourcePrefix"/>.
/// </para>
/// </remarks>
public abstract class SurfaceInjectorBase : IIncrementalGenerator
{
    /// <summary>The LogicalName prefix shared by every embedded surface resource (see the owning csproj).</summary>
    protected virtual string ResourcePrefix => "Synto.Runtime.";

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

    private ImmutableArray<InjectedFile> BuildSurface()
    {
        var assembly = GetType().Assembly;
        var prefix = ResourcePrefix;
        var builder = ImmutableArray.CreateBuilder<InjectedFile>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            string source = ReadResource(assembly, resourceName);

            var tree = CSharpSyntaxTree.ParseText(source);
            var root = tree.GetCompilationUnitRoot();

            var rewritten = (CompilationUnitSyntax)new PublicToInternalRewriter().Visit(root);

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
}
