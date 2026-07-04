using Xunit;

namespace Synto.Example.ObjectReader.Tests;

// The locked type name 'ObjectReader' collides with the enclosing namespace
// 'Synto.Example.ObjectReader' (parent-namespace members outrank compilation-unit usings), so a
// namespace-scoped alias is required to bind the bare 'ObjectReader.Create' calls to the API class.
using ObjectReader = global::Synto.Example.ObjectReader.Api.ObjectReader;

public class ObjectReaderApiTests
{
    private sealed record Widget(string Sku, int Qty);

    [Fact]
    public void Create_WhenNotIntercepted_ThrowsDescriptiveNotSupported() // C-4
    {
        // A NON-constant member list is never intercepted (the SOR0002 path); the runtime
        // fallback must throw a descriptive NotSupportedException. Stays valid for every later
        // task because this call is never a candidate for interception.
        var data = new[] { new Widget("A", 1) };
        string[] members = GetMembers(); // runtime value -> not compile-time constant

        // Intentionally non-constant member list: this exercises the runtime-fallback path, so the generator's
        // SOR0002 fires here on purpose — suppress it narrowly at this call site.
#pragma warning disable SOR0002
        var ex = Assert.Throws<NotSupportedException>(() => ObjectReader.Create(data, members));
#pragma warning restore SOR0002
        Assert.Contains("constant", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetMembers() => ["Sku", "Qty"];
}
