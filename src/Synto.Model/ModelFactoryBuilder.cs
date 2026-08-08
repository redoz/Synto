using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto;
using Synto.Formatting;
using Synto.GenKit;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Generators.Model;

/// <summary>
/// Emits the sibling cache-safe <c>record</c> + its <c>From(TSymbol)</c> factory from a validated
/// <see cref="ModelInfo"/>. Pure syntax construction: a positional <c>sealed record</c> whose members are the
/// projected fields, plus a static <c>From</c> factory whose <c>new(…)</c> arguments are the getter RHS
/// expressions. At Task 5 those RHS are verbatim (still referencing <c>Symbol.*</c>); Task 6 rebinds them to
/// the <c>symbol</c> parameter. Mirrors <c>MatchComposer</c>'s output-shape ownership.
/// </summary>
internal static class ModelFactoryBuilder
{
    public static (string FileName, string Source) Build(ModelInfo info)
    {
        var recordName = info.RecordName;

        // Rebind SyntoModel<T>.Symbol references in each getter RHS to the `symbol` From-parameter. The renamer
        // matches by bound symbol, so the map key is the inherited base `Symbol` property (SymbolEqualityComparer).
        var renamer = BuildSymbolRenamer(info);

        var parameters = string.Join(", ", info.Fields.Select(field => $"{RenderFieldType(field)} {field.Name}"));
        var arguments = string.Join(", ", info.Fields.Select(field => RenderArgument(field, renamer)));

        var record = (RecordDeclarationSyntax)ParseMemberDeclaration(
            $"sealed record {recordName}({parameters}) {{ public static {recordName} From({info.SymbolTypeDisplay} symbol) => new({arguments}); }}")!;

        MemberDeclarationSyntax topLevel = info.ContainingNamespace is { } ns
            ? FileScopedNamespaceDeclaration(ParseName(ns)).AddMembers(record)
            : record;

        var compilationUnit = CompilationUnit()
            .AddMembers(topLevel)
            .WithLeadingTrivia(
                Trivia(
                    NullableDirectiveTrivia(
                        Token(SyntaxKind.EnableKeyword),
                        isActive: true)));

        var source = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace()).GetText(Encoding.UTF8).ToString();

        var qualifiedName = info.ContainingNamespace is { } containing ? $"{containing}.{recordName}" : recordName;
        return (qualifiedName.SanitizeHintName() + ".g.cs", source);
    }

    /// <summary>
    /// Builds the renamer that rebinds the inherited <c>SyntoModel&lt;T&gt;.Symbol</c> property references to the
    /// <c>From</c> factory's <c>symbol</c> parameter. Returns <see langword="null"/> when the spec's base carries no
    /// <c>Symbol</c> member (a malformed spec) — then the getter RHS flow through verbatim.
    /// </summary>
    private static SymbolReferenceRenamer? BuildSymbolRenamer(ModelInfo info)
    {
        var symbolProperty = info.SpecSymbol.BaseType?.GetMembers("Symbol").FirstOrDefault();
        if (symbolProperty is null)
            return null;

        var map = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default) { [symbolProperty] = "symbol" };
        return new SymbolReferenceRenamer(info.SemanticModel, map);
    }

    /// <summary>
    /// The emitted record field's declared type. Plain getters keep the getter's declared type verbatim;
    /// <c>Project.Each/One</c> nested-model getters are rewritten from the spec-collection type to the cache-safe
    /// record type — <c>EquatableArray&lt;Record&gt;</c> for <c>Each</c>, a nullable <c>Record?</c> for <c>One</c>.
    /// </summary>
    private static string RenderFieldType(ProjectedField field) => field.Kind switch
    {
        ProjectionKind.Each => $"global::Synto.Generators.EquatableArray<{field.NestedRecordName}>",
        ProjectionKind.One => $"{field.NestedRecordName}?",
        _ => field.DeclaredType.ToString(),
    };

    /// <summary>
    /// The <c>From</c> factory argument for a field. Plain getters flow the (Symbol-rebound) RHS through verbatim;
    /// <c>Project.Each/One</c> getters are rewritten to real nested-model projections over the (Symbol-rebound)
    /// shim argument — <c>Each</c> maps each element through the nested <c>Record.From</c> and materializes an
    /// <c>EquatableArray</c>; <c>One</c> is the nullable-aware <c>arg is null ? null : Record.From(arg)</c>.
    /// </summary>
    private static string RenderArgument(ProjectedField field, SymbolReferenceRenamer? renamer)
    {
        switch (field.Kind)
        {
            case ProjectionKind.Each:
                {
                    var arg = Rewrite(field.ProjectArgument!, renamer);
                    return $"global::Synto.Generators.EquatableArrayExtensions.ToEquatableArray(global::System.Linq.Enumerable.Select({arg}, {field.NestedRecordName}.From))";
                }

            case ProjectionKind.One:
                {
                    var arg = Rewrite(field.ProjectArgument!, renamer);
                    return $"{arg} is null ? null : {field.NestedRecordName}.From({arg})";
                }

            default:
                return Rewrite(field.Rhs, renamer);
        }
    }

    private static string Rewrite(ExpressionSyntax expression, SymbolReferenceRenamer? renamer) =>
        (renamer is null ? expression : (ExpressionSyntax)renamer.Visit(expression)!).ToString();
}
