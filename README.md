# Synto

This is an experimental library designed to make it easy to construct Roslyn syntax trees for when you're working with source generators. It does this by using a source generator that takes syntax trees as parsed by the compiler and "quotes" them to expose the syntax tree itself in your code.

This project was inspired by the excellent [RoslynQuoter](https://github.com/KirillOsenkov/RoslynQuoter) by [Kirill Osenkov](https://github.com/KirillOsenkov/), the [online version](https://roslynquoter.azurewebsites.net/) has been invaluable in understanding the C# syntax structure.


See [test/Synto.Test/Samples.cs](test/Synto.Test/Samples.cs) for some simple examples of what currently works.


## Notes
The way this is being packaged is probably not the best way to do this, for now it seems to work but long-term it should probably be cleaned up.