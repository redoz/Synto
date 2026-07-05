extern alias SyntoCore;

using System;
using Microsoft.CodeAnalysis;
using SyntoCore::Synto;
using Xunit;

namespace Synto.Test;

// Namespace-level sample enums so their reflected FullName is a clean `Synto.Test.<Name>` (the qualified
// enum-member access the batteries-included value-lift must emit).
internal enum SampleColor
{
    Red,
    Green,
    Blue,
}

[Flags]
internal enum SampleBits
{
    None = 0,
    A = 1,
    B = 2,
}

internal static class SampleContainer
{
    // A NESTED enum: its reflected FullName uses `+` for the nesting boundary, which the lift must normalize to
    // a legal C# `.`.
    internal enum Shade
    {
        First,
        Second,
    }
}

/// <summary>
/// Direct input→output tests for the batteries-included <c>ToSyntax&lt;T&gt;</c> enum branch (plan Task 3): an
/// enum value lifts to a qualified enum-member access, or — for an undefined/flags-combination value — a cast of
/// the underlying integral literal. Reached through the PUBLIC Synto.Core copy via the <c>SyntoCore</c> extern
/// alias (mirrors <see cref="HelperContractTests"/>).
/// </summary>
public class LiteralSyntaxExtensionsTest
{
    [Fact]
    public void NamedEnumValue_LiftsToQualifiedMemberAccess()
    {
        Assert.Equal(
            "global::Synto.Test.SampleColor.Red",
            SampleColor.Red.ToSyntax().NormalizeWhitespace().ToFullString());
    }

    [Fact]
    public void NestedEnumType_ResolvesWithDotNotPlus()
    {
        Assert.Equal(
            "global::Synto.Test.SampleContainer.Shade.Second",
            SampleContainer.Shade.Second.ToSyntax().NormalizeWhitespace().ToFullString());
    }

    [Fact]
    public void UndefinedEnumValue_LiftsToCastOfUnderlyingLiteral()
    {
        Assert.Equal(
            "(global::Synto.Test.SampleColor)99",
            ((SampleColor)99).ToSyntax().NormalizeWhitespace().ToFullString());
    }

    [Fact]
    public void FlagsCombination_LiftsToCastOfUnderlyingLiteral()
    {
        // A | B == 3 maps to no single named member -> the cast form.
        Assert.Equal(
            "(global::Synto.Test.SampleBits)3",
            (SampleBits.A | SampleBits.B).ToSyntax().NormalizeWhitespace().ToFullString());
    }
}
