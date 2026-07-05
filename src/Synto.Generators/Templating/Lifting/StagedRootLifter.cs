using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Synto.Generators.Templating.Diagnostics;
using Synto.Generators.Templating.Emit;

namespace Synto.Generators.Templating.Lifting;

/// <summary>
/// The shared staged-root lift core behind BOTH the in-body <c>Unquote&lt;T&gt;()</c> parameter/field path
/// (<c>LiftStagedParameters</c>) and the <c>[Unquote] T value</c>-parameter path (<c>LiftStagedRootParameters</c>).
/// It owns, in an order-preserving way, the pieces the two paths share: the depth-zero reference partition, the
/// GATED value lift (only depth-zero references lift to a factory-time syntax value — a control-only root never
/// demands a converter, which is exactly why a converterless control-only <c>[Unquote]</c> param no longer dies
/// with SY1008), the control-region symbol rename, the string-root interpolation mapping, the trim, and the
/// factory-parameter append.
///
/// The per-path LIFT STRATEGY differs and is supplied by the caller as a delegate invoked ONLY inside the gate:
/// the in-body path passes a direct <c>value.ToSyntax()</c> invocation (the byte-identical seam — in-body output
/// must not move), the param path passes <c>ValueLift.TryEmitValueLift</c> (converter discovery + SY1008/SY1009,
/// pushing its own diagnostic and setting <see cref="TemplateBuildContext.ConverterError"/> on failure). All work
/// is semantic and done inside the transform; nothing is captured into pipeline state.
/// </summary>
internal static class StagedRootLifter
{
    /// <summary>
    /// Lift one staged root into the factory build context. See <see cref="StagedRootLift"/> for the per-root inputs
    /// the caller resolves (name after dedupe, factory-parameter type, references, root symbols, trim nodes, whether
    /// the value type is <c>System.String</c>, and the per-path conversion strategy).
    /// </summary>
    public static void Lift(TemplateBuildContext ctx, in StagedRootLift lift)
    {
        var additionalUsings = ctx.AdditionalUsings;
        string paramName = lift.ParamName;

        // (1) Reference partition: depth-zero references are those NOT consumed by a live control region, a [Splice]
        // generator body, a builder call's staged-argument spine, or a scalar count-fold. Only these are spliced
        // into the quoted output and so need a factory-time syntax value; every other reference is build-time code.
        var depthZeroReferences = lift.References.Where(reference =>
            !ctx.RegionConsumedNodes.Contains(reference)
            && !ctx.SpliceGeneratorNodes.Contains(reference)
            && !ctx.BuilderStagedConsumedNodes.Contains(reference)
            && !ctx.FoldClaimedReferences.Contains(reference)).ToList();

        // (2) Gated lift: emit the `syntaxForParam_<name>` preamble local + a replacement/highlight per depth-zero
        // reference ONLY when there is at least one. Control-only (empty partition) ⇒ no preamble local, no
        // replacements, and — crucially — the per-path conversion strategy is never invoked, so a converterless
        // control-only [Unquote] param never reaches the [Runtime] converter demand (was SY1008).
        if (depthZeroReferences.Count > 0)
        {
            ExpressionSyntax conversion = lift.EmitConversion(IdentifierName(paramName));

            var syntaxForParamIdentifier = Identifier("syntaxForParam_" + paramName);
            ctx.Shape.Preamble.Add(
                LocalDeclarationStatement(
                    VariableDeclaration(
                        additionalUsings.GetTypeName(ParseTypeName(typeof(ExpressionSyntax).FullName!)),
                        SingletonSeparatedList(
                            VariableDeclarator(
                                syntaxForParamIdentifier,
                                null,
                                EqualsValueClause(conversion))))));

            foreach (var reference in depthZeroReferences)
            {
                ctx.Plan.Replacements.Add(reference, IdentifierName(syntaxForParamIdentifier));
                // Tap 2: a depth-zero staged-root reference is lifted to a factory-time value — highlight it (SY1035).
                ctx.LiveFragmentInfos.Add(TemplateDiagnostics.LiveFragment(reference.GetLocation()));
            }
        }

        // (3) Control-region wiring: map EVERY declaration-site root symbol to the shared factory parameter name so
        // the live-region renamer rewrites each member's reference (a foreach driver) to the one factory parameter.
        foreach (var rootSymbol in lift.RootSymbols)
            ctx.RootNames[rootSymbol] = paramName;

        // (4) String-roots: a string-typed root may be baked into surrounding literal text when used as a bare
        // interpolation hole. Map EVERY reference (depth-0 AND consumed) to the raw factory parameter accessor,
        // in scope wherever the factory body / staged-region scaffold runs.
        if (lift.IsStringTyped)
        {
            foreach (var reference in lift.References)
                ctx.Plan.StringRoots[reference] = IdentifierName(paramName);
        }

        // (5) Trim the carrier node(s) and append the factory parameter.
        foreach (var trimNode in lift.TrimNodes)
            ctx.Plan.Trim.Add(trimNode);

        ctx.Shape.Parameters.Add(Parameter(Identifier(paramName)).WithType(lift.ParameterType));
    }
}

/// <summary>
/// The per-root inputs to <see cref="StagedRootLifter.Lift"/>, resolved by the calling path. The factory-parameter
/// <see cref="ParameterType"/> is resolved by the CALLER (declared syntax for built-ins, fully-qualified display
/// for concrete non-built-ins, the declared identifier for inlined type parameters) so it is correct even for a
/// control-only root whose value never reaches the converter.
/// </summary>
internal readonly struct StagedRootLift
{
    public StagedRootLift(
        string paramName,
        TypeSyntax parameterType,
        IReadOnlyList<SyntaxNode> references,
        IReadOnlyList<ISymbol> rootSymbols,
        IReadOnlyList<SyntaxNode> trimNodes,
        bool isStringTyped,
        Func<ExpressionSyntax, ExpressionSyntax> emitConversion)
    {
        ParamName = paramName;
        ParameterType = parameterType;
        References = references;
        RootSymbols = rootSymbols;
        TrimNodes = trimNodes;
        IsStringTyped = isStringTyped;
        EmitConversion = emitConversion;
    }

    /// <summary>The resolved factory-parameter name (already deduped against the caller's name allocator).</summary>
    public string ParamName { get; }

    /// <summary>The factory-parameter type syntax (resolved by the caller — see the type summary).</summary>
    public TypeSyntax ParameterType { get; }

    /// <summary>Every identifier use-site of the root (depth-zero uses lift; consumed uses are build-time code).</summary>
    public IReadOnlyList<SyntaxNode> References { get; }

    /// <summary>Every declaration-site root symbol renamed to the shared factory parameter (control-region wiring).</summary>
    public IReadOnlyList<ISymbol> RootSymbols { get; }

    /// <summary>The carrier node(s) removed from the quoted output (the field/local declaration or the parameter).</summary>
    public IReadOnlyList<SyntaxNode> TrimNodes { get; }

    /// <summary>Whether the value type is <c>System.String</c> (drives the interpolation string-root mapping).</summary>
    public bool IsStringTyped { get; }

    /// <summary>
    /// The per-path conversion strategy: given the value-access expression (<c>IdentifierName(paramName)</c>),
    /// produce the factory-time <c>ExpressionSyntax</c> conversion. Invoked ONLY when the depth-zero partition is
    /// non-empty. In-body passes a direct <c>value.ToSyntax()</c>; the param path passes the
    /// <c>ValueLift.TryEmitValueLift</c> result (and reports SY1008/SY1009 + sets ConverterError on failure).
    /// </summary>
    public Func<ExpressionSyntax, ExpressionSyntax> EmitConversion { get; }
}
