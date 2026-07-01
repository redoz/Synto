using System;
using System.Collections.Generic;

namespace Synto.Generators.Templating;

/// <summary>
/// Shared uniqueness sets for the names the plan phase mints: factory parameter names and inlined
/// (staged) type-parameter names. Steps add to these to guarantee a generated name is unique within one
/// factory build. (The minting/dedup loop stays inline at each call site; this carrier only owns the sets.)
/// </summary>
internal sealed class NameAllocator
{
    public HashSet<string> ParamNames { get; } = new(StringComparer.Ordinal);

    public HashSet<string> InlinedTypeParamNames { get; } = new(StringComparer.Ordinal);
}
