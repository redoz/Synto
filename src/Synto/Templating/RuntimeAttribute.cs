using System;

namespace Synto.Templating;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Delegate | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public class RuntimeAttribute : Attribute
{
    public const string Default = nameof(Default);
    public RuntimeAttribute(string key = Default) { }
}