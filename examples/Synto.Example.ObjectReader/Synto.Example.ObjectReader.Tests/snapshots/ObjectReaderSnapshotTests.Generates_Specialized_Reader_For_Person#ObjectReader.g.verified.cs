//HintName: ObjectReader.g.cs
namespace Synto.Example.ObjectReader.Generated;
file sealed class ObjectReader_Person_0 : global::System.Data.IDataReader
{
    private readonly global::System.Collections.Generic.IEnumerator<global::Person> _e;
    private bool _closed;
    private bool _onRow;
    public ObjectReader_Person_0(global::System.Collections.Generic.IEnumerable<global::Person> source) => _e = source.GetEnumerator();
    public int FieldCount
    {
        get
        {
            return 2;
        }
    }

    public string GetName(int i)
    {
        if (i == 0)
            return "Name";
        if (i == 1)
            return "Age";
        throw OutOfRange(i);
    }

    public int GetOrdinal(string name)
    {
        if (name == "Name")
            return 0;
        if (name == "Age")
            return 1;
        throw NoColumn(name);
    }

    public global::System.Type GetFieldType(int i)
    {
        if (i == 0)
            return typeof(global::System.String);
        if (i == 1)
            return typeof(global::System.Int32);
        throw OutOfRange(i);
    }

    public object GetValue(int i)
    {
        if (!_onRow)
        {
            throw new global::System.InvalidOperationException("No current row. Call Read() and ensure it returned true before reading values.");
        }

        if (i == 0)
            return (object? )_e.Current.Name ?? global::System.DBNull.Value;
        if (i == 1)
            return (object? )_e.Current.Age ?? global::System.DBNull.Value;
        throw OutOfRange(i);
    }

    public bool GetBoolean(int i)
    {
        throw new global::System.InvalidCastException($"Field {i} is not a Boolean column.");
    }

    public byte GetByte(int i)
    {
        throw new global::System.InvalidCastException($"Field {i} is not a Byte column.");
    }

    public char GetChar(int i)
    {
        throw new global::System.InvalidCastException($"Field {i} is not a Char column.");
    }

    public global::System.DateTime GetDateTime(int i)
    {
        throw new global::System.InvalidCastException($"Field {i} is not a DateTime column.");
    }

    public decimal GetDecimal(int i)
    {
        throw new global::System.InvalidCastException($"Field {i} is not a Decimal column.");
    }

    public double GetDouble(int i)
    {
        throw new global::System.InvalidCastException($"Field {i} is not a Double column.");
    }

    public float GetFloat(int i)
    {
        throw new global::System.InvalidCastException($"Field {i} is not a Single column.");
    }

    public global::System.Guid GetGuid(int i)
    {
        throw new global::System.InvalidCastException($"Field {i} is not a Guid column.");
    }

    public short GetInt16(int i)
    {
        throw new global::System.InvalidCastException($"Field {i} is not an Int16 column.");
    }

    public int GetInt32(int i)
    {
        if (i == 1)
            return _e.Current.Age;
        throw new global::System.InvalidCastException($"Field {i} is not an Int32 column.");
    }

    public long GetInt64(int i)
    {
        throw new global::System.InvalidCastException($"Field {i} is not an Int64 column.");
    }

    public string GetString(int i)
    {
        if (i == 0)
            return _e.Current.Name;
        throw new global::System.InvalidCastException($"Field {i} is not a String column.");
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