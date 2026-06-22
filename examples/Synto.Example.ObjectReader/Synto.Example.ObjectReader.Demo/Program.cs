using System.Data;

namespace Synto.Example.ObjectReader.Demo;

// 'ObjectReader' the type collides with the 'Synto.Example.ObjectReader' namespace; alias it so the bare
// Create call binds to the API class (parent-namespace members outrank compilation-unit usings).
using ObjectReader = global::Synto.Example.ObjectReader.Api.ObjectReader;

public static class Program
{
    /// <summary>The sample rows the Demo exposes. Public so the test suite can drive the same call path.</summary>
    public static Person[] SampleData() =>
    [
        new Person("Ada", 36, "Countess"),
        new Person("Alan", 41),
    ];

    private static void Main()
    {
        Person[] people = SampleData();

        // The source generator intercepts this constant-member call and routes it to a specialized reader.
        using IDataReader reader = ObjectReader.Create(people, "Name", "Age");

        Console.WriteLine($"{reader.GetName(0),-12}{reader.GetName(1),6}");
        Console.WriteLine(new string('-', 18));
        while (reader.Read())
        {
            Console.WriteLine($"{reader.GetValue(0),-12}{reader.GetValue(1),6}");
        }
    }
}
