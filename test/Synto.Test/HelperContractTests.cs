extern alias SyntoCore;

using System;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntoCore::Synto;
using Xunit;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto.Test;

/// <summary>
/// Direct input→output tests for the emitted runtime helpers <c>ToSyntax</c> (LiteralSyntaxExtensions) and
/// <c>OrNullLiteralExpression</c> (QuoteSyntaxExtensions). These are reached through the public Synto.Core
/// copies via the <c>SyntoCore</c> extern alias (the generator emits file-local copies into generated code,
/// not into this assembly), mirroring <see cref="RuntimeTypeExtensionsTests"/>.
/// </summary>
public class HelperContractTests
{
    [Theory]
    // string -> quoted string literal
    [InlineData("hello", "\"hello\"")]
    // integral / floating literals
    [InlineData(42, "42")]
    [InlineData(3.5, "3.5")]
    // bool literals
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    // char literal
    [InlineData('a', "'a'")]
    public void ToSyntaxRendersLiteral(object value, string expected)
    {
        // value is boxed as object, so this also exercises the generic ToSyntax<T> type-switch that
        // dispatches to the typed overloads.
        Assert.Equal(expected, value.ToSyntax().ToString());
    }

    [Fact]
    public void ToSyntaxNullStringRendersNullLiteral()
    {
        string? value = null;
        Assert.Equal("null", value.ToSyntax().ToString());
    }

    [Fact]
    public void ToSyntaxNullViaGenericSwitchRendersNullLiteral()
    {
        object? value = null;
        Assert.Equal("null", value.ToSyntax().ToString());
    }

    [Fact]
    public void ToSyntaxUnsupportedTypeThrows()
    {
        // the generic switch's fall-through arm: a type with no literal mapping throws rather than emit
        // an unparseable runtime representation.
        Assert.Throws<NotImplementedException>(() => new object().ToSyntax());
    }

    [Fact]
    public void OrNullLiteralExpressionReturnsNonNullExpressionUnchanged()
    {
        ExpressionSyntax expr = IdentifierName("foo");

        // the non-null branch returns the very same instance (it is just `expr ?? null-literal`)
        Assert.Same(expr, expr.OrNullLiteralExpression());
    }

    [Fact]
    public void OrNullLiteralExpressionMapsNullToNullLiteral()
    {
        ExpressionSyntax? expr = null;

        Assert.Equal("null", expr.OrNullLiteralExpression().ToString());
    }
}
