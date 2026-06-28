//HintName: IdentifierAttribute.g.cs
#nullable enable
using System;

namespace Synto.Templating;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
internal sealed class IdentifierAttribute : Attribute
{
}
