//HintName: InheritsAttribute.g.cs
#nullable enable
using System;

namespace Synto.Templating;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method, AllowMultiple = false)]
internal sealed class InheritsAttribute<TBase> : Attribute
{
}
