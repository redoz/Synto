//HintName: ImplementsAttribute.g.cs
#nullable enable
using System;

namespace Synto.Templating;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ImplementsAttribute<TInterface> : Attribute
{
}
