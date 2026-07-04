extern alias SyntoCore;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using CoreFormatter = SyntoCore::Synto.Formatting.SyntaxFormatter;
using CoreLiterals = SyntoCore::Synto.LiteralSyntaxExtensions;

namespace Synto.Test;

// #18 (formatter list-stack symmetry) + #19 (non-finite float/double literals) — two contained
// src/Synto runtime fixes. The formatter is internal in Synto.Core (InternalsVisibleTo Synto.Test)
// and reached through the SyntoCore extern alias; ToSyntax is reached the same way to avoid any
// ambiguity with the injected internal surface.
public class FormatterAndNonFiniteLiteralTests
{
    // ----- #18: non-separated SyntaxList must push/pop its own count -----

    // A plain SyntaxList (a lambda block's statements) reached inside an invocation, before any
    // separated list has been pushed, made VisitListElement.Peek() throw on an empty stack.
    [Fact]
    public void Format_InvocationWithPlainListInCalleeSubtree_DoesNotThrow()
    {
        var expr = SyntaxFactory.ParseExpression("(() => { int a = 1; int b = 2; })()");

        var formatted = CoreFormatter.Format(expr);

        Assert.NotNull(formatted);
    }

    // With the enclosing separated list's count leaking in, an interpolated-string argument's single
    // content part got spurious list indentation (a newline injected inside the string literal).
    // The symmetric override makes the content list see its own count (1) → no indentation.
    [Fact]
    public void Format_InterpolatedStringArgument_GetsNoSpuriousIndentation()
    {
        var expr = SyntaxFactory.ParseExpression("Foo(bar, $\"plain\")");

        var formatted = CoreFormatter.Format(expr);

        var text = formatted.ToFullString();
        Assert.Contains("$\"plain\"", text);
        Assert.DoesNotContain("$\"\n", text);
    }

    // ----- #19: non-finite float/double render as member access, not bare identifiers -----

    [Theory]
    [InlineData(double.NaN, "double.NaN")]
    [InlineData(double.PositiveInfinity, "double.PositiveInfinity")]
    [InlineData(double.NegativeInfinity, "double.NegativeInfinity")]
    public void ToSyntax_NonFiniteDouble_RendersMemberAccess(double value, string expected)
    {
        var rendered = CoreLiterals.ToSyntax(value).ToString();
        Assert.Equal(expected, rendered);
        Assert.Empty(SyntaxFactory.ParseExpression(rendered).GetDiagnostics());
    }

    [Theory]
    [InlineData(float.NaN, "float.NaN")]
    [InlineData(float.PositiveInfinity, "float.PositiveInfinity")]
    [InlineData(float.NegativeInfinity, "float.NegativeInfinity")]
    public void ToSyntax_NonFiniteFloat_RendersMemberAccess(float value, string expected)
    {
        var rendered = CoreLiterals.ToSyntax(value).ToString();
        Assert.Equal(expected, rendered);
        Assert.Empty(SyntaxFactory.ParseExpression(rendered).GetDiagnostics());
    }

    [Fact]
    public void ToSyntax_FiniteDouble_StaysNumericLiteral()
    {
        Assert.Equal("1.5", CoreLiterals.ToSyntax(1.5d).ToString());
    }
}
