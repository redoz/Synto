using System;

namespace Synto.Matching;

/// <summary>
/// Marks a method as a matcher template: the generator fills in <typeparamref name="TMatcher"/> from the
/// method's body, matching consumer syntax against that shape.
/// </summary>
/// <typeparam name="TMatcher">The partial matcher type the generator completes.</typeparam>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MatchAttribute<TMatcher> : Attribute
{
    /// <summary>Creates the attribute with the given <paramref name="option"/> cardinality.</summary>
    public MatchAttribute(MatchOption option = MatchOption.None)
    {
        Option = option;
    }

    /// <summary>The cardinality of the syntax shape this matcher targets.</summary>
    public MatchOption Option { get; }
}
