//HintName: Demo.M.Narrowed.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Demo;
partial class M
{
    public sealed record NarrowedMatch(global::Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax Call);
    public static NarrowedMatch? Narrowed(SyntaxNode node)
    {
        if (node is not global::Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax cap_call)
            return null;
        return new NarrowedMatch(cap_call);
    }
}