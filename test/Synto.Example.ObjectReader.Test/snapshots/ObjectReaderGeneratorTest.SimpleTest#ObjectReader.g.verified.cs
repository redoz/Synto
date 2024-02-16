//HintName: ObjectReader.g.cs
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    file class InterceptsLocationAttribute(string filePath, int line, int character) : Attribute
    {
    }
}

namespace Synto.Example.ObjectReader.Generated
{
    internal abstract partial file class ObjectReaderTemplate : IDataReader
    {
        private readonly IEnumerable<TestClass> _data;
        private readonly IEnumerator<TestClass> _enumerator;
        private bool _canRead;
        private bool _isClosed;
        protected ObjectReaderTemplate(IEnumerable<TestClass> data)
        {
            _data = data;
            _enumerator = _data.GetEnumerator();
            _isClosed = false;
        }

        public bool GetBoolean(int i)
        {
            throw new NotImplementedException();
        }

        public byte GetByte(int i)
        {
            throw new NotImplementedException();
        }

        public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length)
        {
            throw new NotImplementedException();
        }

        public char GetChar(int i)
        {
            throw new NotImplementedException();
        }

        public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length)
        {
            throw new NotImplementedException();
        }

        public IDataReader GetData(int i)
        {
            throw new NotImplementedException();
        }

        public string GetDataTypeName(int i)
        {
            throw new NotImplementedException();
        }

        public DateTime GetDateTime(int i)
        {
            throw new NotImplementedException();
        }

        public decimal GetDecimal(int i)
        {
            throw new NotImplementedException();
        }

        public double GetDouble(int i)
        {
            throw new NotImplementedException();
        }

        public Type GetFieldType(int i)
        {
            throw new NotImplementedException();
        }

        public float GetFloat(int i)
        {
            throw new NotImplementedException();
        }

        public Guid GetGuid(int i)
        {
            throw new NotImplementedException();
        }

        public short GetInt16(int i)
        {
            throw new NotImplementedException();
        }

        public int GetInt32(int i)
        {
            throw new NotImplementedException();
        }

        public long GetInt64(int i)
        {
            throw new NotImplementedException();
        }

        public string GetName(int i)
        {
            throw new NotImplementedException();
        }

        public int GetOrdinal(string name)
        {
            throw new NotImplementedException();
        }

        public virtual string GetString(int i)
        {
            throw new NotImplementedException();
        }

        public virtual object GetValue(int i)
        {
            throw new NotImplementedException();
        }

        public virtual int GetValues(object[] values)
        {
            throw new NotImplementedException();
        }

        public virtual bool IsDBNull(int i)
        {
            throw new NotImplementedException();
        }

        public virtual int FieldCount { get; }

        public virtual object this[int i] => throw new NotImplementedException();
        public virtual object this[string name] => throw new NotImplementedException();
        public void Dispose()
        {
            _enumerator.Dispose();
        }

        public void Close()
        {
            if (_isClosed)
                throw new InvalidOperationException("Reader already closed");
            _isClosed = true;
            _canRead = false;
        }

        public DataTable? GetSchemaTable()
        {
            throw new NotImplementedException();
        }

        public bool NextResult()
        {
            Close();
            return false;
        }

        public bool Read()
        {
            if (!_canRead)
                throw new InvalidOperationException("Cannot read past end of enumerator.");
            return _canRead = _enumerator.MoveNext();
        }

        public int Depth => 1;
        public bool IsClosed => _isClosed;
        public int RecordsAffected => -1;
    }

    internal static class ObjectReaderFactory
    {
        [System.Runtime.CompilerServices.InterceptsLocation("", 12, 21)]
        public static IDataReader Create0(IEnumerable<TestClass> data, params string[] properties)
        {
            return new ObjectReader0(data);
        }
    }
}