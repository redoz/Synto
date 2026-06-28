using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Formatting;
using Synto.Matching;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// Assembles the generated matcher file: the result record + matcher method + companion predicate + pattern
/// descriptor, wrapped in the target type's ancestry under #nullable enable, then normalized and formatted.
/// Sole owner of the generated output shape. Pure transform-internal helper invoked by <see cref="MatchEmitter"/>.
/// </summary>
internal static class MatchComposer
{
    /// <summary>
    /// Composes the generated file: the nested result record + the matcher method, wrapped in the partial
    /// target's full ancestry (file-scoped namespace via <see cref="ClassDeclarationSyntaxExtensions.WithAncestryFrom"/>,
    /// no block conversion), under <c>#nullable enable</c> with the three Roslyn usings, normalized + formatted.
    /// The <c>IsExternalInit</c> polyfill is NOT part of this file — it is a single per-assembly post-init
    /// output (C3) registered by <see cref="MatchFactorySourceGenerator"/>.
    /// </summary>
    public static (string FileName, string Source) Compose(MatchInfo info, MatchContext ctx, List<StatementSyntax> body)
    {
        string matchName = info.Name + "Match";

        // Record members are the captures in SIGNATURE order (by Ordinal), regardless of walk order. A
        // positional record gives a free Deconstruct. Zero captures -> an empty-member record.
        string memberList = string.Join(", ",
            ctx.Captures.OrderBy(capture => capture.Ordinal).Select(capture => $"{capture.MemberType} {capture.MemberName}"));

        var record = (RecordDeclarationSyntax)ParseMemberDeclaration($"public sealed record {matchName}({memberList});")!;

        var method = ((MethodDeclarationSyntax)ParseMemberDeclaration($"public static {matchName}? {info.Name}(SyntaxNode node) {{ }}")!)
            .WithBody(Block(body.ToArray()));

        // The companion cheap predicate {Name}CouldMatch: the matcher's top-level type/kind/shape gate as a
        // standalone bool (C-FM1 superset). Each dispatch branch recorded the SAME guard it roots on.
        var couldMatch = (MethodDeclarationSyntax)ParseMemberDeclaration(
            $"public static bool {info.Name}CouldMatch(SyntaxNode node) {{ return {ctx.CouldMatchGuard}; }}")!;

        // The {Name}Pattern descriptor bundles the cheap predicate + the full matcher into the injected
        // MatchPattern<TMatch> (the single symbol a consumer hands to ForMatch). Fully-qualified so it binds
        // to the injected-internal copy regardless of the consumer's usings.
        var pattern = (PropertyDeclarationSyntax)ParseMemberDeclaration(
            $"public static global::Synto.Matching.MatchPattern<{matchName}> {info.Name}Pattern {{ get; }} = new({info.Name}CouldMatch, {info.Name});")!;

        var targetClassDecl = (ClassDeclarationSyntax)info.Target.DeclaringSyntaxReferences[0].GetSyntax();

        MemberDeclarationSyntax targetSyntax = ClassDeclaration(targetClassDecl.Identifier)
            .WithModifiers(targetClassDecl.Modifiers)
            .AddMembers(record, method, couldMatch, pattern);

        targetSyntax = targetSyntax.WithAncestryFrom(info.Target);

        var usings = new List<UsingDirectiveSyntax>
        {
            UsingDirective(ParseName("Microsoft.CodeAnalysis")),
            UsingDirective(ParseName("Microsoft.CodeAnalysis.CSharp")),
            UsingDirective(ParseName("Microsoft.CodeAnalysis.CSharp.Syntax")),
        };

        // System.Linq only when the body slices with Skip/Take (a variable/Exactly statement capture) — adding
        // it unconditionally would churn the capture-less goldens.
        if (ctx.NeedsLinq)
            usings.Add(UsingDirective(ParseName("System.Linq")));

        var compilationUnit = CompilationUnit()
            .AddMembers(targetSyntax)
            .AddUsings(usings.ToArray())
            .WithLeadingTrivia(
                Trivia(
                    NullableDirectiveTrivia(
                        Token(SyntaxKind.EnableKeyword),
                        isActive: true)));

        var sourceText = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace()).GetText(Encoding.UTF8).ToString();

        return ($"{info.TargetFullName}.{info.Name}.g.cs", sourceText);
    }
}
