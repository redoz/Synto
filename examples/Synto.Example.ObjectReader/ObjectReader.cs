using System.Data;

namespace Synto.Example.ObjectReader;

public static class ObjectReader
{
    public static IDataReader Create<T>(IEnumerable<T> data, params string[] properties)
    {
        throw new NotImplementedException();
    }
}