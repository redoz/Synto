using System;

namespace Synto.Templating;

/// <summary>
/// Marks a <c>static class</c> as a value-to-syntax converter provider. Synto discovers a converter for an
/// <c>[Inline]</c> parameter of a custom (non-built-in) type by looking for a <c>[Runtime]</c> class exposing
/// an extension method <c>ExpressionSyntax ToSyntax(this T)</c> for that type, and calls it from the generated
/// factory. It is a pure marker — the converter is inferred entirely from the inlined parameter's type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RuntimeAttribute : Attribute
{
}
