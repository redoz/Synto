# Synto

This is an experimental library designed to make it easy to construct Roslyn syntax trees for when you're working with source generators. It does this by using a source generator that takes syntax trees as parsed by the compiler and "quotes" them to expose the syntax tree itself in your code.

This project was inspired by the excellent [RoslynQuoter](https://github.com/KirillOsenkov/RoslynQuoter) by [Kirill Osenkov](https://github.com/KirillOsenkov/), the [online version](https://roslynquoter.azurewebsites.net/) has been invaluable in understanding the C# syntax structure.


See [examples/Synto.Examples/Program.cs](examples/Synto.Examples/Program.cs) for some simple examples of what currently works.

Templates also support a **live (staged) binding-time split**: data marked live (via `Template.Parameter<T>()`, `Template.Live<T>()`, or `[Live]`) has its dataflow trace emitted verbatim as executing C# into the generated factory — so a `foreach`/`for`/`while`/`if` over live data is unrolled or branch-specialized at template-invocation time, while everything else is quoted as usual. A generic, user-extensible **syntax-builder** surface (`[SyntaxBuilder]`, with built-in `Member`/`TypeOf`) lifts live values into output syntax. The [ObjectReader example](examples/Synto.Example.ObjectReader/) dog-foods this end to end. See [docs/superpowers/specs/2026-06-27-live-staged-templates-design.md](docs/superpowers/specs/2026-06-27-live-staged-templates-design.md) for the design.


## Useful Links
[Incremental Source Generators](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md)

[Source Generators Cookbook](https://github.com/dotnet/roslyn/blob/main/docs/features/source-generators.cookbook.md)
