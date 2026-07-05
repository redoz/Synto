using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Generators.Templating.Builders;
using Synto.Generators.Templating.Decorations;

namespace Synto.Generators.Templating.Emit;

/// <summary>
/// The patches the terminal quoter applies while quoting one template's source tree. This is 1:1 the argument
/// list of <see cref="Quoting.TemplateQuoterFactory.Create"/>: each field is one quoter channel. Plain mutable
/// carrier populated by the plan-phase steps and consumed once at emission; never equatable, never captured
/// into cached pipeline state (only the resulting source text flows out).
/// </summary>
internal sealed class QuotePlan
{
    // Node-keyed terminal substitutions: emit the mapped expression INSTEAD of quoting the node (the quoter
    // short-circuits and does not descend). Which file-local helper(s) the caller emits is decided by SCANNING
    // the finished factory syntax (see TemplateDocumentBuilder.FindReferencedHelpers), so no usage flags here.
    public Dictionary<SyntaxNode, ExpressionSyntax> Replacements { get; } = new();

    // Node-keyed drops: the quoter returns null for these (and the global branch-prune folds fully-trimmed
    // parents in — see BranchPruneIdentifier).
    public HashSet<SyntaxNode> Trim { get; } = new();

    // Builder-call channel: recognized facade-call invocation nodes mapped to their resolved BuilderCall. The
    // quoter short-circuits at the invocation and assembles the fully-qualified builder invocation, quoting each
    // [Quoted]/[TypeArg] island by RE-ENTERING the full pipeline (so nested builder calls and splice injections
    // inside builder args compose). Consumed by BuilderStep.
    public Dictionary<SyntaxNode, BuilderCall> BuilderCalls { get; } = new();

    // Interpolation staged-fold channel: string-typed staged-root REFERENCE nodes mapped to their factory-time
    // raw value accessor (the factory parameter / hoisted local). Consumed by InterpolationFoldStep to
    // bake bare staged-string interpolation holes into literal text.
    public Dictionary<SyntaxNode, ExpressionSyntax> StringRoots { get; } = new();

    // The member segment each valid [Splice] generator contributes to its enclosing type's member list, keyed
    // at the generator method's declaration node so the quoter splices it at that position.
    public Dictionary<SyntaxNode, ExpressionSyntax> MemberSegments { get; } = new();

    // Post-quote decoration hooks discovered by ResolveDecorations: per-node ordered Apply… chains the final
    // quoter folds onto each decorated declaration. Null when the decoration surface isn't referenced.
    public IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>>? DecorationHooks { get; set; }
}
