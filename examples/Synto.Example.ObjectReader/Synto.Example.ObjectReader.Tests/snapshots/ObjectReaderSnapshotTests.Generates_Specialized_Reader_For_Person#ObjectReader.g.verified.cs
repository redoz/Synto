//HintName: ObjectReader.g.cs
namespace Synto.Example.ObjectReader.Generated;
file sealed class ObjectReader_Person_0 : global::System.Data.IDataReader
{
    private readonly global::System.Collections.Generic.IEnumerator<global::Person> _e;
    private bool _closed;
    private bool _onRow;
    public ObjectReader_Person_0(global::System.Collections.Generic.IEnumerable<global::Person> source) => _e = source.GetEnumerator();
    public int FieldCount => 2;

    public string GetName(int i) => i switch
    {
        0 => "Name",
        1 => "Age",
        _ => throw OutOfRange(i),
    };
    public int GetOrdinal(string name) => name switch
    {
        "Name" => 0,
        "Age" => 1,
        _ => throw NoColumn(name),
    };
    public global::System.Type GetFieldType(int i) => i switch
    {
        0 => typeof(global::System.String),
        1 => typeof(global::System.Int32),
        _ => throw OutOfRange(i),
    };
    public object GetValue(int i)
    {
        if (!_onRow)
        {
            throw new global::System.InvalidOperationException("No current row. Call Read() and ensure it returned true before reading values.");
        }

        return i switch
        {
            0 => (object? )_e.Current.Name ?? global::System.DBNull.Value,
            1 => (object? )_e.Current.Age ?? global::System.DBNull.Value,
            _ => throw OutOfRange(i),
        };
    }

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
    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length) => throw new global::System.NotSupportedException("GetBytes is not supported; expose the member as a typed column and use GetValue instead.");
    public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length) => throw new global::System.NotSupportedException("GetChars is not supported; expose the member as a typed column and use GetValue instead.");
    public global::System.Data.IDataReader GetData(int i) => throw new global::System.NotSupportedException("GetData (nested data readers) is not supported by the ObjectReader.");
    private static global::System.IndexOutOfRangeException OutOfRange(int i) => new global::System.IndexOutOfRangeException($"Field index {i} is out of range.");
    private static global::System.IndexOutOfRangeException NoColumn(string name) => new global::System.IndexOutOfRangeException($"Column '{name}' was not found.");
}

file static class ObjectReaderInterceptors
{
    [global::System.Runtime.CompilerServices.InterceptsLocationAttribute(1, "KCzMKZw+u4+7ZWZt1VhbmvgAAABTb3VyY2UuY3M=")]
    public static global::System.Data.IDataReader Create_0<T>(global::System.Collections.Generic.IEnumerable<T> source, params string[] members) => new ObjectReader_Person_0((global::System.Collections.Generic.IEnumerable<global::Person>)(object)source);
}