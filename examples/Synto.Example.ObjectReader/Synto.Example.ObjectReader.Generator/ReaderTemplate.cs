using Synto.Templating;

namespace Synto.Example.ObjectReader.Generator;

// The dog-food (Task 4 / D3 / R2): the INVARIANT IDataReader skeleton is authored ONCE as real C# and quoted
// by a Synto [Template]. ObjectReaderGenerator calls Factory.ObjectReaderTemplate(elementType) to get the
// reader's ClassDeclarationSyntax, then renames it, makes it `file sealed`, adds the `: IDataReader` base list,
// and replaces the five list-driven members (FieldCount + the GetName/GetOrdinal/GetFieldType/GetValue switches)
// with raw SyntaxFactory — those depend on the resolved column list, so they cannot be expressed as a fixed
// [Template] (logged A->B drop). The element type flows in as a syntax hole via [Inline(AsSyntax = true)] T.
//
// NOTE: every member must compile here (this is real source in the netstandard2.0 generator assembly); the five
// list-driven members carry compiling placeholder bodies that the generator overwrites.
#pragma warning disable CA1812 // ObjectReaderTemplate is a template carrier, only ever quoted — never instantiated.
#pragma warning disable CA1822 // placeholder members need not be static; their real (instance) bodies are injected.
#pragma warning disable CA1024 // GetSchemaTable etc. mirror the IDataReader signatures verbatim.

[Template(typeof(Factory))]
internal sealed class ObjectReaderTemplate<[Inline(AsSyntax = true)] T>
{
    private readonly global::System.Collections.Generic.IEnumerator<T> _e;
    private bool _closed;
    private bool _onRow;

    public ObjectReaderTemplate(global::System.Collections.Generic.IEnumerable<T> source) => _e = source.GetEnumerator();

    // ---- list-driven placeholders (overwritten by ObjectReaderGenerator with raw SyntaxFactory) ----

    public int FieldCount => 0;

    public string GetName(int i) => throw OutOfRange(i);

    public int GetOrdinal(string name) => throw NoColumn(name);

    public global::System.Type GetFieldType(int i) => throw OutOfRange(i);

    public object GetValue(int i)
    {
        if (!_onRow)
        {
            throw new global::System.InvalidOperationException("No current row. Call Read() and ensure it returned true before reading values.");
        }

        throw OutOfRange(i);
    }

    // ---- invariant members (the [Template] payload) ----

    public int GetValues(object[] values)
    {
        if (values is null)
        {
            throw new global::System.ArgumentNullException(nameof(values));
        }

        int count = values.Length < FieldCount ? values.Length : FieldCount;
        for (int i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    public bool IsDBNull(int i) => GetValue(i) is global::System.DBNull;

    public object this[int i] => GetValue(i);

    public object this[string name] => GetValue(GetOrdinal(name));

    public bool GetBoolean(int i) => (bool)GetValue(i);
    public byte GetByte(int i) => (byte)GetValue(i);
    public char GetChar(int i) => (char)GetValue(i);
    public global::System.DateTime GetDateTime(int i) => (global::System.DateTime)GetValue(i);
    public decimal GetDecimal(int i) => (decimal)GetValue(i);
    public double GetDouble(int i) => (double)GetValue(i);
    public float GetFloat(int i) => (float)GetValue(i);
    public global::System.Guid GetGuid(int i) => (global::System.Guid)GetValue(i);
    public short GetInt16(int i) => (short)GetValue(i);
    public int GetInt32(int i) => (int)GetValue(i);
    public long GetInt64(int i) => (long)GetValue(i);
    public string GetString(int i) => (string)GetValue(i);
    public string GetDataTypeName(int i) => GetFieldType(i).Name;

    public int Depth => 0;
    public bool IsClosed => _closed;
    public int RecordsAffected => -1;

    public bool Read()
    {
        if (_closed)
        {
            throw new global::System.InvalidOperationException("The data reader is closed.");
        }

        _onRow = _e.MoveNext();
        return _onRow;
    }

    public bool NextResult() => false;

    public void Close()
    {
        if (!_closed)
        {
            _closed = true;
            _onRow = false;
            _e.Dispose();
        }
    }

    public void Dispose() => Close();

    public global::System.Data.DataTable? GetSchemaTable() => null;

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length)
        => throw new global::System.NotSupportedException("GetBytes is not supported; expose the member as a typed column and use GetValue instead.");

    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length)
        => throw new global::System.NotSupportedException("GetChars is not supported; expose the member as a typed column and use GetValue instead.");

    public global::System.Data.IDataReader GetData(int i)
        => throw new global::System.NotSupportedException("GetData (nested data readers) is not supported by the ObjectReader.");

    private static global::System.IndexOutOfRangeException OutOfRange(int i)
        => new global::System.IndexOutOfRangeException($"Field index {i} is out of range.");

    private static global::System.IndexOutOfRangeException NoColumn(string name)
        => new global::System.IndexOutOfRangeException($"Column '{name}' was not found.");
}

#pragma warning restore CA1024
#pragma warning restore CA1822
#pragma warning restore CA1812

/// <summary>Synto template factory target. The injected <c>TemplateFactorySourceGenerator</c> fills the other
/// partial with <c>ObjectReaderTemplate(ExpressionSyntax T)</c> returning the skeleton's <c>ClassDeclarationSyntax</c>.</summary>
internal static partial class Factory
{
}
