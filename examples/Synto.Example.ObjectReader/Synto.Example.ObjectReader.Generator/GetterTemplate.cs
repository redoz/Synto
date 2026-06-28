using System.Linq;
using Synto.Generators;
using Synto.Templating;
using static Synto.Templating.Template;

namespace Synto.Example.ObjectReader.Generator;

// Child [Template] (child-templates narrow case): ONE typed getter, specialized per CLR type. The 12 typed getters in
// ReaderTemplate.cs collapse to repeated invocations of this single template (see the TypedGetters member-generator).
// Authored standalone with an inert non-generic `_e` so the carrier compiles alone — only `_e.Current` is quoted, and
// it quotes to the same `_e.Current.<member>` access the real reader exposes (its `_e` is `IEnumerator<T>`). The return
// type is supplied per call as a `TypeSyntax` via the `[Splice] TRet` param; the caller renames via `.WithIdentifier`.
#pragma warning disable CA1812 // template carrier, only ever quoted — never instantiated.
internal sealed class GetterTemplate
{
    private readonly global::System.Collections.IEnumerator _e = default!;

    [Template(typeof(Factory))]
    public TRet TypedGetter<[Splice] TRet>(int i)
    {
        var columns = Parameter<EquatableArray<ColumnInfo>>();
        var clrTypeName = Parameter<string>();
        var typeLabel = Parameter<string>();
        foreach (var c in columns.Where(c => c.ColumnTypeName == clrTypeName))
            if (i == c.Ordinal)
                return Member<TRet>(_e.Current, c.Name);
        throw new global::System.InvalidCastException($"Field {i} is not {typeLabel} column.");
    }
}
#pragma warning restore CA1812
