using System;

namespace Synto;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Delegate | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class RuntimeAttribute : Attribute
{
    public string Key { get; }

    public const string Default = nameof(Default);
    public RuntimeAttribute(string key = Default)
    {
        Key = key;
    }
}