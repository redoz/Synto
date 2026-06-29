using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// Binds a recognized user <c>[SyntaxBuilder]</c> facade call to its factory-time builder invocation: walks the
/// builder's <see cref="FacadeShape"/> and pairs each parameter with the call's value/type argument (reusing the
/// shape's <c>FreshReturnTypeParam</c> cursor to skip the synthesized leading <c>TResult</c> type-arg). Records
/// SY1015/SY1016/SY1017 binding diagnostics. Runs inside the generator transform; nothing captured into pipeline
/// state.
/// </summary>
internal static class FacadeArgumentBinder
{
    public static BuilderCall? Bind(SemanticModel semanticModel, InvocationExpressionSyntax invocation, IMethodSymbol builder, List<DiagnosticInfo> diagnostics)
    {
        var shape = FacadeShape.Derive(builder, out var annotationError, out var returnShapeError);
        if (returnShapeError is not null)
        {
            diagnostics.Add(TemplateDiagnostics.BuilderBadReturnShape(invocation.GetLocation(), builder.Name, returnShapeError));
            return null;
        }
        if (annotationError is not null)
        {
            diagnostics.Add(TemplateDiagnostics.FacadeSynthesisError(invocation.GetLocation(), builder.Name, annotationError));
            return null;
        }

        // Facade type arguments (e.g. `int` in `Cast<int>(x)`) and value arguments (e.g. `x`).
        var typeArgs = GetTypeArguments(invocation);
        var valueArgs = invocation.ArgumentList.Arguments;

        var bindings = new List<BuilderArgBinding>();
        int typeArgCursor = shape!.FreshReturnTypeParam ? 1 : 0; // skip the leading TResult facade type-arg
        int valueCursor = 0;

        foreach (var p in shape.Parameters)
        {
            switch (p.Kind)
            {
                case BuilderArgKind.QuotedTypeArg:
                    if (typeArgCursor >= typeArgs.Count)
                    {
                        diagnostics.Add(TemplateDiagnostics.BuilderArgBindingMismatch(invocation.GetLocation(), p.ParameterName, "missing type argument"));
                        return null;
                    }
                    bindings.Add(new BuilderArgBinding(BuilderArgKind.QuotedTypeArg, p.ParameterName, null, typeArgs[typeArgCursor]));
                    typeArgCursor++;
                    break;

                default:
                    if (valueCursor >= valueArgs.Count)
                    {
                        diagnostics.Add(TemplateDiagnostics.BuilderArgBindingMismatch(invocation.GetLocation(), p.ParameterName, "missing argument"));
                        return null;
                    }
                    bindings.Add(new BuilderArgBinding(p.Kind, p.ParameterName, valueArgs[valueCursor].Expression, null));
                    valueCursor++;
                    break;
            }
        }

        string builderType = builder.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new BuilderCall(invocation, builderType, builder.Name, bindings);
    }

    private static SeparatedSyntaxList<TypeSyntax> GetTypeArguments(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            GenericNameSyntax g => g.TypeArgumentList.Arguments,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax g } => g.TypeArgumentList.Arguments,
            _ => default,
        };
    }
}
