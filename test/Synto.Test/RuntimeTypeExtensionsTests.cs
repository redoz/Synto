extern alias SyntoCore;

using System;
using System.Collections.Generic;
// ToTypeSyntax is no longer injected into this assembly as an internal helper (the generator now emits
// it as a file-local copy into each generated file that uses it). This is a direct unit test of the
// runtime helper, so reach the PUBLIC copy in the referenced Synto.Core assembly via the extern alias.
using SyntoCore::Synto;
using Xunit;

namespace Synto.Test;

public class RuntimeTypeExtensionsTests
{
    [Theory]
    // primitives / simple types
    [InlineData(typeof(int), "System.Int32")]
    [InlineData(typeof(string), "System.String")]
    // closed generics (the case the old ParseTypeName(typeof(T).FullName) approach got wrong)
    [InlineData(typeof(List<int>), "System.Collections.Generic.List<System.Int32>")]
    [InlineData(typeof(List<List<int>>), "System.Collections.Generic.List<System.Collections.Generic.List<System.Int32>>")]
    [InlineData(typeof(Dictionary<int, string>), "System.Collections.Generic.Dictionary<System.Int32, System.String>")]
    // arrays
    [InlineData(typeof(int[]), "System.Int32[]")]
    [InlineData(typeof(int[][]), "System.Int32[][]")]
    [InlineData(typeof(int[,]), "System.Int32[,]")]
    [InlineData(typeof(List<int>[]), "System.Collections.Generic.List<System.Int32>[]")]
    // nested types
    [InlineData(typeof(PlainOuter.Inner), "Synto.Test.PlainOuter.Inner")]
    // nested type whose enclosing type is generic (regression: the '+Inner' segment used to be dropped)
    [InlineData(typeof(GenericOuter<int>.Inner), "Synto.Test.GenericOuter<System.Int32>.Inner")]
    // generic nested in generic, each level carrying its own argument
    [InlineData(typeof(GenericOuter<int>.Middle<string>), "Synto.Test.GenericOuter<System.Int32>.Middle<System.String>")]
    public void RendersParseableCSharpName(Type type, string expected)
    {
        Assert.Equal(expected, type.ToTypeSyntax().ToString());
    }
}

// These fixtures only ever exist as typeof() targets, so they are deliberately never instantiated.
#pragma warning disable CA1812 // Avoid uninstantiated internal classes
internal static class PlainOuter
{
    internal sealed class Inner { }
}

internal sealed class GenericOuter<T>
{
    internal sealed class Inner { }

    internal sealed class Middle<U> { }
}
#pragma warning restore CA1812
