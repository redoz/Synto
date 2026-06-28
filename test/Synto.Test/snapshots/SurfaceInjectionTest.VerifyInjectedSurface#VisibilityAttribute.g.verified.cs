//HintName: VisibilityAttribute.g.cs
#nullable enable
using System;

namespace Synto.Templating;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method, AllowMultiple = false)]
internal sealed class VisibilityAttribute : Attribute
{
    public VisibilityAttribute(Access access) => Access = access;

    public Access Access { get; }
}
