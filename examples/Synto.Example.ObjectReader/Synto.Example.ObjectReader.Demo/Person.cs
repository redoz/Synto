namespace Synto.Example.ObjectReader.Demo;

/// <summary>The Demo's POCO. Exposed as <c>IDataReader</c> columns by name via the source generator.</summary>
public sealed record Person(string Name, int Age, string? Nickname = null);
