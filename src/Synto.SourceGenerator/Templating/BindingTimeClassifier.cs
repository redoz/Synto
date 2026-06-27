using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// The binding-time of a node inside a <c>[Template]</c> body (plan Task 4 / spec §4). A node is either
/// <see cref="Quoted"/> (independent of every live root — emitted as output-world syntax, the default/unchanged
/// behaviour), <see cref="LiveValue"/> (an expression whose value depends on a live root, consumed in value
/// position — lifted at factory time), or <see cref="LiveControl"/> (a control-flow construct whose driving
/// expression — iteration source / condition — is live, so it runs at factory time and unrolls/specializes).
/// </summary>
internal enum BindingTime
{
    /// <summary>Independent of every live root; emitted verbatim as output-world syntax (default).</summary>
    Quoted,

    /// <summary>An expression whose value transitively depends on a live root; lifted at factory time.</summary>
    LiveValue,

    /// <summary>A control-flow construct whose driving expression is live; runs at factory time.</summary>
    LiveControl,
}

/// <summary>
/// A live root the classifier seeds liveness from (plan Task 4). Discovered upstream by the live finders:
/// a <c>Parameter&lt;T&gt;()</c> parameter, a <c>[Live]</c> method parameter, or a <c>Live&lt;T&gt;(expr)</c>
/// bound local. A <em>bound</em> root carries the factory-time <see cref="BindingExpression"/> that defines it;
/// a bound root whose expression transitively depends on an output-world (quoted) value is an impossible cut.
/// </summary>
internal readonly struct LiveRoot
{
    public LiveRoot(ISymbol symbol, ExpressionSyntax? bindingExpression = null)
    {
        Symbol = symbol;
        BindingExpression = bindingExpression;
    }

    /// <summary>The live symbol (parameter or bound local) liveness propagates from.</summary>
    public ISymbol Symbol { get; }

    /// <summary>
    /// For a bound root (<c>var n = Live&lt;T&gt;(expr);</c>), the factory-time expression that defines it —
    /// it must be evaluable at factory time (depend only on live/constant values). <c>null</c> for a root
    /// supplied by the caller (a <c>Parameter&lt;T&gt;()</c> / <c>[Live]</c> parameter), which can never be an
    /// impossible cut.
    /// </summary>
    public ExpressionSyntax? BindingExpression { get; }
}

/// <summary>
/// The dataflow partition produced by <see cref="BindingTimeClassifier"/>: a per-node <see cref="BindingTime"/>
/// plus the set of impossible cuts (live fragments that transitively depend on an output-world value). This is
/// pure analysis — it changes no emission; the emitter (plan Tasks 6–8) consumes it.
/// </summary>
internal sealed class BindingTimePartition
{
    private readonly Dictionary<SyntaxNode, BindingTime> _classification;

    public BindingTimePartition(Dictionary<SyntaxNode, BindingTime> classification, IReadOnlyList<(SyntaxNode Node, string Reason)> impossibleCuts, IReadOnlyCollection<ISymbol> liveSymbols)
    {
        _classification = classification;
        ImpossibleCuts = impossibleCuts;
        LiveSymbols = liveSymbols;
    }

    /// <summary>
    /// Every symbol liveness reached (the seed roots plus locals whose definition transitively depends on a
    /// root). The staging emitter seeds its region-local live set from this (then augments with loop variables
    /// and accumulators it discovers, which the classifier does not track — plan Task 7).
    /// </summary>
    public IReadOnlyCollection<ISymbol> LiveSymbols { get; }

    /// <summary>The binding-time of <paramref name="node"/>; <see cref="BindingTime.Quoted"/> when unclassified.</summary>
    public BindingTime Classify(SyntaxNode node) =>
        _classification.TryGetValue(node, out var bindingTime) ? bindingTime : BindingTime.Quoted;

    public bool IsLiveValue(SyntaxNode node) => Classify(node) == BindingTime.LiveValue;

    public bool IsLiveControl(SyntaxNode node) => Classify(node) == BindingTime.LiveControl;

    /// <summary>
    /// Nodes forced live (a bound root's binding expression) that transitively depend on a quoted/generated-world
    /// value — reported as <c>SY1013</c> by the staging emitter (plan Task 8).
    /// </summary>
    public IReadOnlyList<(SyntaxNode Node, string Reason)> ImpossibleCuts { get; }
}

/// <summary>
/// Partitions a <c>[Template]</c> body into quoted / live-value / live-control nodes given its live roots
/// (plan Task 4 / spec §4). Liveness propagates from the roots by def-use over local declarations; control flow
/// whose driver is quoted stays quoted (emitted as real output control flow). Conservatism is expected — the
/// classifier under-approximates through opaque boundaries (<c>[Live]</c> is the manual escape hatch).
/// <para>
/// Runs entirely inside the <see cref="TemplateFactorySourceGenerator"/> transform; captures no
/// <see cref="Compilation"/>/<see cref="ISymbol"/>/<see cref="SemanticModel"/>/<see cref="SyntaxNode"/> into
/// pipeline state (cacheability is sacred — spec §8).
/// </para>
/// </summary>
internal sealed class BindingTimeClassifier
{
    private readonly SemanticModel _semanticModel;
    private readonly SyntaxNode _body;
    private readonly IReadOnlyCollection<LiveRoot> _roots;
    private readonly HashSet<ISymbol> _live = new(SymbolEqualityComparer.Default);

    private BindingTimeClassifier(SemanticModel semanticModel, SyntaxNode body, IReadOnlyCollection<LiveRoot> roots)
    {
        _semanticModel = semanticModel;
        _body = body;
        _roots = roots;
        foreach (var root in roots)
            _live.Add(root.Symbol);
    }

    /// <summary>
    /// Classifies every node in <paramref name="body"/> relative to <paramref name="roots"/>, returning the
    /// dataflow partition (per-node binding-time + impossible cuts).
    /// </summary>
    public static BindingTimePartition Classify(SemanticModel semanticModel, SyntaxNode body, IReadOnlyCollection<LiveRoot> roots)
    {
        var classifier = new BindingTimeClassifier(semanticModel, body, roots);
        classifier.PropagateLiveness();
        var classification = classifier.ClassifyNodes();
        var impossibleCuts = classifier.FindImpossibleCuts();
        return new BindingTimePartition(classification, impossibleCuts, classifier._live);
    }

    /// <summary>True if any identifier inside <paramref name="node"/> binds to a live symbol.</summary>
    private bool ReferencesLive(SyntaxNode node)
    {
        foreach (var identifier in node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            var symbol = _semanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol is not null && _live.Contains(symbol))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Seeds liveness from the roots, then propagates to a fixpoint over local declarations: a local whose
    /// initializer references a live value is itself live (its computed value depends on a root).
    /// </summary>
    private void PropagateLiveness()
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var declarator in _body.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (declarator.Initializer is not { } initializer)
                    continue;
                if (_semanticModel.GetDeclaredSymbol(declarator) is not { } local)
                    continue;
                if (_live.Contains(local))
                    continue;
                if (ReferencesLive(initializer.Value))
                {
                    _live.Add(local);
                    changed = true;
                }
            }
        }
    }

    private Dictionary<SyntaxNode, BindingTime> ClassifyNodes()
    {
        var classification = new Dictionary<SyntaxNode, BindingTime>();

        foreach (var node in _body.DescendantNodes())
        {
            switch (node)
            {
                // Control flow whose DRIVER (iteration source / condition) is live runs at factory time.
                case ForEachStatementSyntax forEach when ReferencesLive(forEach.Expression):
                case ForEachVariableStatementSyntax forEachVar when ReferencesLive(forEachVar.Expression):
                case WhileStatementSyntax whileStatement when ReferencesLive(whileStatement.Condition):
                case ForStatementSyntax forStatement when forStatement.Condition is { } condition && ReferencesLive(condition):
                case IfStatementSyntax ifStatement when ReferencesLive(ifStatement.Condition):
                    classification[node] = BindingTime.LiveControl;
                    break;

                // Any other expression whose value depends on a live root is a lifted live value.
                case ExpressionSyntax expression when ReferencesLive(expression):
                    classification[node] = BindingTime.LiveValue;
                    break;
            }
        }

        return classification;
    }

    /// <summary>
    /// A bound root whose binding expression references an output-world value — a parameter/local that is NOT
    /// live (it exists only in the generated output) — cannot be evaluated at factory time: an impossible cut.
    /// </summary>
    private IReadOnlyList<(SyntaxNode Node, string Reason)> FindImpossibleCuts()
    {
        var cuts = new List<(SyntaxNode, string)>();

        foreach (var root in _roots)
        {
            if (root.BindingExpression is not { } expression)
                continue;

            foreach (var identifier in expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                var symbol = _semanticModel.GetSymbolInfo(identifier).Symbol;
                if (symbol is ILocalSymbol or IParameterSymbol && !_live.Contains(symbol))
                {
                    cuts.Add((expression, $"live binding depends on output-world value '{symbol.Name}'"));
                    break;
                }
            }
        }

        return cuts;
    }
}
