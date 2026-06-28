using System;

namespace Synto.Templating;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method, AllowMultiple = false)]
public sealed class InheritsAttribute<TBase> : Attribute
{
}
