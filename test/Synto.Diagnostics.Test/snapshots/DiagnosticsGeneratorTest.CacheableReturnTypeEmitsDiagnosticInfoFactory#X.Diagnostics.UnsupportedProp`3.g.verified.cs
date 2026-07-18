//HintName: X.Diagnostics.UnsupportedProp`3.g.cs
#nullable enable
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using Synto.Generators;

namespace X;
internal static partial class Diagnostics
{
    private static DiagnosticDescriptor _UnsupportedProp = new("TST0001", "Unsupported property type", "Property '{0}' has unsupported type '{1}'", "MyGen.Usage", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning, true);
    public static partial DiagnosticInfo UnsupportedProp(LocationInfo? location, string name, string type)
    {
        return new DiagnosticInfo(_UnsupportedProp, location, new EquatableArray<string>(ImmutableArray.Create(
                                                                                             name, 
                                                                                             type)));
    }
}