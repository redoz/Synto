using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

public delegate void Syntax();

public delegate TExpression Syntax<TExpression>();