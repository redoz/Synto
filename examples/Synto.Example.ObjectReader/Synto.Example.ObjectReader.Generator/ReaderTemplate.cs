using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Generators;
using Synto.Templating;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using static Synto.Templating.Template;

namespace Synto.Example.ObjectReader.Generator;

// The dog-food (plan Task 9): the WHOLE IDataReader skeleton is authored once as real C# and quoted by a Synto
// [Template]. The data-driven members are now expressed with the LIVE-STAGED surface — a live `foreach` over the
// resolved column list (lifted to the factory via `Parameter<EquatableArray<ColumnInfo>>()`), with `Member`/`TypeOf`
// builders splicing the per-column member access / type — so the generator no longer glues text with
// `StringBuilder`/`ParseMemberDeclaration`. ObjectReaderGenerator calls Factory.ObjectReaderTemplate(elementType,
// columns, fieldCount), renames the class + ctor, makes it `file sealed`, and adds the `: IDataReader` base list.
//
// Every member compiles here (this is real source in the netstandard2.0 generator assembly): the live surface is
// inert at carrier-compile time (Parameter/Member/TypeOf return default!), and the `foreach`/`.Where(...)` shapes
// are ordinary C# — the generator unrolls them at factory-build time.
#pragma warning disable CA1812 // ObjectReaderTemplate is a template carrier, only ever quoted — never instantiated.
#pragma warning disable CA1024 // GetSchemaTable etc. mirror the IDataReader signatures verbatim.

[Template(typeof(Factory))]
internal sealed class ObjectReaderTemplate<[Splice] T>
{
    private readonly global::System.Collections.Generic.IEnumerator<T> _e;
    private bool _closed;
    private bool _onRow;

    public ObjectReaderTemplate(global::System.Collections.Generic.IEnumerable<T> source) => _e = source.GetEnumerator();

    // ---- data-driven members: each repeats over the live column list (unrolled at factory time) -------------

    // Degenerate value hole: the column count → an int literal (lifted via a live int parameter).
    public int FieldCount
    {
        get
        {
            var fieldCount = Parameter<int>();
            return fieldCount;
        }
    }

    public string GetName(int i)
    {
        var columns = Parameter<EquatableArray<ColumnInfo>>();
        foreach (var c in columns)            // live control → unrolls in the factory
            if (i == c.Ordinal)               // c.Ordinal → int literal
                return c.Name;                // c.Name    → string literal
        throw OutOfRange(i);
    }

    public int GetOrdinal(string name)
    {
        var columns = Parameter<EquatableArray<ColumnInfo>>();
        foreach (var c in columns)
            if (name == c.Name)               // c.Name → string literal
                return c.Ordinal;             // c.Ordinal → int literal
        throw NoColumn(name);
    }

    public global::System.Type GetFieldType(int i)
    {
        var columns = Parameter<EquatableArray<ColumnInfo>>();
        foreach (var c in columns)
            if (i == c.Ordinal)
                return TypeOf(c.ColumnTypeName);   // type-name hole → typeof(<that type>)
        throw OutOfRange(i);
    }

    public object GetValue(int i)
    {
        if (!_onRow)                          // invariant guard — literal, precedes the repeater
        {
            throw new global::System.InvalidOperationException("No current row. Call Read() and ensure it returned true before reading values.");
        }

        var columns = Parameter<EquatableArray<ColumnInfo>>();
        foreach (var c in columns)
            if (i == c.Ordinal)
                // Member<object>(_e.Current, c.Name) → `_e.Current.<Name>` (identifier hole), then box + DBNull.
                return (object?)Member<object>(_e.Current, c.Name) ?? global::System.DBNull.Value;
        throw OutOfRange(i);
    }

    // ---- cast-less typed getters: ONE child [Template] (GetterTemplate.TypedGetter), invoked once per CLR type ----
    // The 12 near-identical typed getters collapse to a [Splice] member-generator that calls the child factory once
    // per getter — supplying the return-type TypeSyntax, the CLR-type filter, and the exception label — then renames
    // each result via .WithIdentifier(...). Generated output is byte-identical to the former hand-written getters.

    [Splice]
    static IEnumerable<MemberDeclarationSyntax> TypedGetters()
    {
        var columns = Parameter<EquatableArray<ColumnInfo>>();
        yield return Factory.TypedGetter(PredefinedType(Token(BoolKeyword)), columns, "global::System.Boolean", "a Boolean").WithIdentifier(Identifier("GetBoolean"));
        yield return Factory.TypedGetter(PredefinedType(Token(ByteKeyword)), columns, "global::System.Byte", "a Byte").WithIdentifier(Identifier("GetByte"));
        yield return Factory.TypedGetter(PredefinedType(Token(CharKeyword)), columns, "global::System.Char", "a Char").WithIdentifier(Identifier("GetChar"));
        yield return Factory.TypedGetter(ParseTypeName("global::System.DateTime"), columns, "global::System.DateTime", "a DateTime").WithIdentifier(Identifier("GetDateTime"));
        yield return Factory.TypedGetter(PredefinedType(Token(DecimalKeyword)), columns, "global::System.Decimal", "a Decimal").WithIdentifier(Identifier("GetDecimal"));
        yield return Factory.TypedGetter(PredefinedType(Token(DoubleKeyword)), columns, "global::System.Double", "a Double").WithIdentifier(Identifier("GetDouble"));
        yield return Factory.TypedGetter(PredefinedType(Token(FloatKeyword)), columns, "global::System.Single", "a Single").WithIdentifier(Identifier("GetFloat"));
        yield return Factory.TypedGetter(ParseTypeName("global::System.Guid"), columns, "global::System.Guid", "a Guid").WithIdentifier(Identifier("GetGuid"));
        yield return Factory.TypedGetter(PredefinedType(Token(ShortKeyword)), columns, "global::System.Int16", "an Int16").WithIdentifier(Identifier("GetInt16"));
        yield return Factory.TypedGetter(PredefinedType(Token(IntKeyword)), columns, "global::System.Int32", "an Int32").WithIdentifier(Identifier("GetInt32"));
        yield return Factory.TypedGetter(PredefinedType(Token(LongKeyword)), columns, "global::System.Int64", "an Int64").WithIdentifier(Identifier("GetInt64"));
        yield return Factory.TypedGetter(PredefinedType(Token(StringKeyword)), columns, "global::System.String", "a String").WithIdentifier(Identifier("GetString"));
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
#pragma warning restore CA1812

/// <summary>Synto template factory target. The injected <c>TemplateFactorySourceGenerator</c> fills the other
/// partial with <c>ObjectReaderTemplate(TypeSyntax T, int fieldCount, EquatableArray&lt;ColumnInfo&gt; columns)</c>
/// returning the skeleton's <c>ClassDeclarationSyntax</c>.</summary>
internal static partial class Factory
{
}
