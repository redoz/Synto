namespace Synto.Templating;

/// <summary>
/// The author-facing value view of the operator builders (<see cref="OperatorBuilders"/>). Authors write
/// <c>using static Synto.Templating.Operators;</c> and bare <c>TypeOf(...)</c> / <c>NameOf(...)</c> /
/// <c>SizeOf(...)</c>. This shell is empty: the inert, fixed-return value facades are SYNTHESIZED into it by
/// <c>SyntaxBuilderFacadeGenerator</c> from the <c>[SyntaxBuilder(Returns=…, Facade=typeof(Operators))]</c>
/// builders. Authored once here, injected <c>internal</c> into the consumer compilation.
/// </summary>
public static partial class Operators
{
}
