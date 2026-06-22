using System.Data;

namespace Synto.Example.ObjectReader.Api;

/// <summary>
/// FastMember-equivalent entry point: exposes a sequence of <typeparamref name="T"/> as an
/// <see cref="IDataReader"/>. A call whose <paramref name="members"/> are a compile-time-constant
/// list is intercepted by the ObjectReader generator and routed to a type-specialized reader with
/// no runtime reflection.
/// </summary>
public static class ObjectReader
{
    /// <summary>Create an <see cref="IDataReader"/> over <paramref name="source"/> exposing the named members as columns.</summary>
    public static IDataReader Create<T>(IEnumerable<T> source, params string[] members)
        => throw new NotSupportedException(
            "ObjectReader.Create was not intercepted by the source generator. 'members' must be a " +
            "compile-time-constant list of names, and interceptors must be enabled " +
            "(<InterceptorsNamespaces> must include Synto.Example.ObjectReader.Generated).");
}
