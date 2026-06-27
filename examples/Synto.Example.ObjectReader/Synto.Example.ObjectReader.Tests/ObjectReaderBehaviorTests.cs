using System.Collections.Generic;
using System.Data;
using Xunit;

namespace Synto.Example.ObjectReader.Tests;

// Alias the API class past the 'Synto.Example.ObjectReader' namespace collision (see ObjectReaderApiTests).
using ObjectReader = global::Synto.Example.ObjectReader.Api.ObjectReader;

public class ObjectReaderBehaviorTests
{
    // 'internal' (not 'private'): the generated reader lives in a separate file under
    // Synto.Example.ObjectReader.Generated and names this type by its fully-qualified name, so the target must
    // be at least assembly-visible. A private nested target is unreachable from the emitted reader (friction).
    internal sealed record Person(string Name, int Age, string? Nickname);

    private static Person[] Sample() =>
    [
        new Person("Ada", 36, "Countess"),
        new Person("Alan", 41, null),
    ];

    [Fact]
    public void Create_IsIntercepted_AndReadsRowsDirectly() // R1 + C-1 + C-3
    {
        using IDataReader reader = ObjectReader.Create(Sample(), "Name", "Age", "Nickname");

        Assert.Equal(3, reader.FieldCount);
        Assert.Equal("Name", reader.GetName(0));
        Assert.Equal(typeof(int), reader.GetFieldType(1));
        Assert.Equal(2, reader.GetOrdinal("Nickname"));

        var rows = new List<object[]>();
        while (reader.Read())
        {
            var values = new object[reader.FieldCount];
            reader.GetValues(values);
            rows.Add(values);
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal("Ada", rows[0][0]);
        Assert.Equal(36, rows[0][1]);
        Assert.Equal(DBNull.Value, rows[1][2]); // null member → DBNull.Value (C-3)
    }

    [Fact]
    public void CastlessGetters_ReadMemberDirectly() // the new capability the live-staged migration unlocks (Task 9)
    {
        using IDataReader reader = ObjectReader.Create(Sample(), "Name", "Age");
        Assert.True(reader.Read());
        Assert.Equal("Ada", reader.GetString(0));   // direct _e.Current.Name, no (string)GetValue boxing
        Assert.Equal(36, reader.GetInt32(1));        // direct _e.Current.Age, no (int)GetValue boxing
        // Person has no DateTime column → the where-filter yields zero arms → InvalidCastException.
        Assert.Throws<InvalidCastException>(() => reader.GetDateTime(0));
    }

    [Fact]
    public void Create_FeedsDataTableLoad() // C-3 functional bar via a real ADO.NET sink
    {
        using IDataReader reader = ObjectReader.Create(Sample(), "Name", "Age");
        var table = new DataTable();
        table.Load(reader);

        Assert.Equal(2, table.Columns.Count);
        Assert.Equal("Name", table.Columns[0].ColumnName);
        Assert.Equal(typeof(string), table.Columns[0].DataType);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Alan", table.Rows[1]["Name"]);
    }

    [Fact]
    public void Demo_PrintsTwoDataRows() // proves the Demo's own call path is intercepted end-to-end
    {
        Demo.Person[] people = Demo.Program.SampleData();
        using IDataReader reader = ObjectReader.Create(people, "Name", "Age");

        int rows = 0;
        while (reader.Read())
        {
            rows++;
        }

        Assert.Equal(people.Length, rows);
    }
}
