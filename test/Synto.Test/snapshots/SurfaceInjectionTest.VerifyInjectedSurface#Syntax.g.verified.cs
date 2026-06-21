//HintName: Syntax.g.cs
#nullable enable
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Templating;

internal delegate void Syntax();

internal delegate TExpression Syntax<TExpression>();
