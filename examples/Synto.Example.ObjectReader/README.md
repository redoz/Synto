# ObjectReader — a FastMember-equivalent `IDataReader`, source-generated

`ObjectReader` turns a sequence of objects into an `IDataReader` with **no runtime reflection**. One call:

```csharp
using IDataReader reader = ObjectReader.Create(people, "Name", "Age");
while (reader.Read())
    Console.WriteLine($"{reader.GetValue(0)}  {reader.GetValue(1)}");
```

When `members` is a compile-time-constant list, the `ObjectReader` source generator **intercepts** the
`Create<T>` call (via the supported `SemanticModel.GetInterceptableLocation` model) and routes it to a
**type-specialized** `IDataReader` whose switch arms read members directly (`_e.Current.Name`) — the
FastMember idea, resolved entirely at compile time. A non-constant member list is never intercepted and the
runtime fallback throws a descriptive `NotSupportedException`.

## What this example is for

This is a deliberate **dog-food**: build a real, semi-complex generator *with Synto* and measure the friction.
The generator (`Synto.Example.ObjectReader.Generator`) consumes Synto purely as analyzers — no `Synto.Core`
runtime dependency — and exercises three Synto surfaces:

- **Synto Templating (`[Template]` / `[Inline]`).** The invariant `IDataReader` skeleton (the enumerator field +
  constructor, `GetValues`/`IsDBNull`/indexers, the typed getters, `Read`/`Close`/`Dispose`, the unsupported
  members, helpers) is authored once as real C# in `ReaderTemplate.cs` and quoted by a single
  `[Template(typeof(Factory))]` class. The element type flows in as a syntax hole via `[Inline(AsSyntax = true)] T`.
  The five **list-driven** members (`FieldCount` + the four switches) cannot be a fixed template — they are raw
  `SyntaxFactory`, swapped in over the templated skeleton (see the friction log).
- **`Synto.Diagnostics` (`[Diagnostic]`).** `SOR0001` (member not found, skipped), `SOR0002` (members not
  constant, not intercepted), and `SOR0000` (internal error — never thrown) are declared as `[Diagnostic]`
  factory methods and reported off an equatable diagnostic carrier.
- **Incremental cacheability.** The pipeline carries only equatable value types (`ObjectReaderModel` /
  `ColumnInfo` / `EquatableArray` / `DiagnosticInfo` / `LocationInfo`) — no `Compilation` / `ISymbol` /
  `SemanticModel` / `SyntaxNode` — so an unrelated edit does not re-run generation.

The generated output is **self-contained**: it references only the BCL (`System.Data`,
`System.Collections.Generic`) and the consumer's own types. No `Synto.*` appears in consumer output.

## Layout

| Project | TFM | Role |
| --- | --- | --- |
| `Synto.Example.ObjectReader.Api` | net10.0 | the `ObjectReader.Create<T>` surface (runtime fallback throws) |
| `Synto.Example.ObjectReader.Generator` | netstandard2.0 | the `IIncrementalGenerator` (intercepts + emits the reader) |
| `Synto.Example.ObjectReader.Demo` | net10.0 | runnable console app |
| `Synto.Example.ObjectReader.Tests` | net10.0 | Verify snapshot + behavioral roundtrip + diagnostics |

Consumers enable interception with
`<InterceptorsNamespaces>$(InterceptorsNamespaces);Synto.Example.ObjectReader.Generated</InterceptorsNamespaces>`.

## Run it

```bash
dotnet run --project examples/Synto.Example.ObjectReader/Synto.Example.ObjectReader.Demo
```

prints the sample rows through the intercepted, reflection-free reader:

```
Name           Age
------------------
Ada             36
Alan            41
```

## See also

- **Friction log:** [`docs/superpowers/notes/2026-06-22-objectreader-synto-friction.md`](../../docs/superpowers/notes/2026-06-22-objectreader-synto-friction.md)
  — where building this against Synto-as-is hurt, and the ranked "Synto could make this easier by …" findings that
  seed a later Synto-improvement spec.
- **Design spec:** [`docs/superpowers/specs/2026-06-22-objectreader-example-design.md`](../../docs/superpowers/specs/2026-06-22-objectreader-example-design.md).
