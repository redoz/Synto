using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Match;

/// <summary>
/// Validates the PUBLIC <c>Synto.Core</c> marker <em>shape</em> for the matching DSL: a consumer snippet
/// that writes the markers must BIND against the public <c>Synto.Core</c> copy (referenced via
/// <see cref="SyntoCoreAssembly"/>). No generator runs here — this asserts only that the public marker
/// types exist with the member shapes a consumer writes.
/// </summary>
/// <remarks>
/// The injected-<b>internal</b> gate (the <c>public</c>-&gt;<c>internal</c> rewrite that
/// <c>SurfaceInjectionGenerator</c> performs) is NOT what this test covers: that is the existing
/// <c>SurfaceInjectionTest</c> golden plus the round-trips. This test deliberately references the public
/// Core copy to validate the consumer-facing marker shape in isolation.
/// </remarks>
public class MatchSurfaceTests
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    private static CSharpCompilation Compile(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        return CSharpCompilation.Create("MatchSurfaceTest",
            [syntaxTree],
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                // Roslyn — the later matching markers (Stmt/Statement/Capture<TNode>) reference syntax types,
                // so keep the shared closure Roslyn-aware for the whole surface.
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.SyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CodeAnalysis.CSharp.SyntaxKind).Assembly.Location),
                // The PUBLIC Synto.Core surface (via the SyntoCore extern alias) — the consumer snippet
                // binds [Match<>] / MatchOption against these public marker types.
                MetadataReference.CreateFromFile(SyntoCoreAssembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public void InjectedMatchAttributeAndOptionBind()
    {
        // A consumer applies [Match<M>(MatchOption.Bare)] to a method. The public-Core marker shape must
        // bind: generic attribute Match<TMatcher>, a MatchOption ctor argument. No generator runs.
        var compilation = Compile(
            """
            using Synto.Matching;

            partial class M { }

            public partial class Consumer
            {
                [Match<M>(MatchOption.Bare)]
                static object One() => null!;
            }
            """);

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "Consumer snippet using the public Synto.Core matching markers failed to bind: "
            + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }

    [Fact]
    public void InjectedCaptureAttributesBind()
    {
        // A consumer marks pattern parameters with the two [Capture] hole markers: the non-generic
        // [Capture] (an open expression hole) and the generic [Capture<TNode>] (narrowed to a Roslyn
        // syntax type). The public-Core marker shape must bind both forms. No generator runs.
        var compilation = Compile(
            """
            using Synto.Matching;
            using Microsoft.CodeAnalysis.CSharp.Syntax;

            public partial class Consumer
            {
                static void Pattern([Capture] object x, [Capture<BinaryExpressionSyntax>] object y) { }
            }
            """);

        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.True(errors.Count == 0,
            "Consumer snippet using the public Synto.Core [Capture] markers failed to bind: "
            + string.Join("; ", errors.Select(d => d.Id + " " + d.GetMessage())));
    }
}
