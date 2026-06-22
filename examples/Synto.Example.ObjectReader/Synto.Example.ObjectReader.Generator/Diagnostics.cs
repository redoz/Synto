using Microsoft.CodeAnalysis;
using Synto.Diagnostics;

namespace Synto.Example.ObjectReader.Generator;

/// <summary>
/// The ObjectReader generator's diagnostics, authored by dog-fooding <c>Synto.Diagnostics</c> (D5): each
/// <c>[Diagnostic]</c>-annotated partial method gets a generated body that builds a <see cref="Diagnostic"/>
/// from a descriptor + the <see cref="Location"/> + message arguments. The generator records equatable
/// <see cref="DiagnosticInfo"/> values in the transform and materializes them here in the output stage.
/// </summary>
internal static partial class Diagnostics
{
    [Diagnostic(
        "SOR0000",
        "ObjectReader generator error",
        "The ObjectReader generator failed unexpectedly ({0}): {1}",
        "ObjectReader.Internal",
        DiagnosticSeverity.Error,
        true)]
    public static partial Diagnostic InternalError(Location? location, string exceptionType, string message);

    [Diagnostic(
        "SOR0001",
        "Member not found",
        "Member '{0}' was not found on type '{1}'; the column is skipped.",
        "ObjectReader.Usage",
        DiagnosticSeverity.Warning,
        true)]
    public static partial Diagnostic MemberNotFound(Location? location, string memberName, string typeName);

    [Diagnostic(
        "SOR0002",
        "ObjectReader members must be constant",
        "ObjectReader.Create members must be a compile-time-constant list of names; the call is not intercepted.",
        "ObjectReader.Usage",
        DiagnosticSeverity.Warning,
        true)]
    public static partial Diagnostic MembersNotConstant(Location? location);
}
