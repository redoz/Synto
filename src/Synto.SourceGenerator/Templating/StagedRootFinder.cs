using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;

namespace Synto;

/// <summary>
/// A live bound local discovered from a <c>var n = Template.Unquote&lt;T&gt;(expr);</c> declaration (plan
/// Task 2). At depth-0 the bound expression runs at factory-build time (hoisted into the factory body as a
/// real runtime local) and each use of the local is lifted into the produced syntax.
/// </summary>
internal sealed class StagedLocal
{
    public StagedLocal(string name, InvocationExpressionSyntax liveCall, ExpressionSyntax valueExpression, LocalDeclarationStatementSyntax declaration, IReadOnlyList<SyntaxNode> references)
    {
        Name = name;
        StagedCall = liveCall;
        ValueExpression = valueExpression;
        Declaration = declaration;
        References = references;
    }

    /// <summary>The bound local's name (becomes the runtime local hoisted into the factory body).</summary>
    public string Name { get; }

    /// <summary>The recognized <c>Unquote&lt;T&gt;(...)</c> invocation (the inert carrier call).</summary>
    public InvocationExpressionSyntax StagedCall { get; }

    /// <summary>The single argument to <c>Unquote&lt;T&gt;(...)</c> — the expression evaluated at factory-build time.</summary>
    public ExpressionSyntax ValueExpression { get; }

    /// <summary>The full <c>var n = Unquote(...);</c> declaration statement (trimmed from the quoted output).</summary>
    public LocalDeclarationStatementSyntax Declaration { get; }

    /// <summary>The identifier use-sites of the local (lifted to <c>n.ToSyntax()</c>).</summary>
    public IReadOnlyList<SyntaxNode> References { get; }
}

/// <summary>
/// A live method parameter discovered from a <c>[Unquote]</c> attribute (plan Task 2). The value is supplied to
/// the generated factory at invocation time and lifted into the produced syntax, exactly like an
/// value at depth-0; the live capability (driving factory-time control flow) is exercised in
/// later staging tasks.
/// </summary>
internal sealed class StagedParameterRoot
{
    public StagedParameterRoot(ParameterSyntax parameter, ITypeSymbol type, IReadOnlyList<SyntaxNode> references)
    {
        Parameter = parameter;
        Type = type;
        References = references;
    }

    /// <summary>The marked template-method parameter (trimmed and re-declared as a factory parameter).</summary>
    public ParameterSyntax Parameter { get; }

    /// <summary>The live value's type <c>T</c> (becomes the factory parameter's type).</summary>
    public ITypeSymbol Type { get; }

    /// <summary>The identifier use-sites of the parameter (lifted to <c>value.ToSyntax()</c>).</summary>
    public IReadOnlyList<SyntaxNode> References { get; }
}

/// <summary>The result of discovering live bound roots: depth-0 live locals and <c>[Unquote]</c> parameters.</summary>
internal sealed class StagedRootResult
{
    public StagedRootResult(IReadOnlyList<StagedLocal> locals, IReadOnlyList<StagedParameterRoot> parameters)
    {
        Locals = locals;
        Parameters = parameters;
    }

    public IReadOnlyList<StagedLocal> Locals { get; }
    public IReadOnlyList<StagedParameterRoot> Parameters { get; }
}

/// <summary>
/// Discovers live bound roots in a <c>[Template]</c> body (plan Task 2): both
/// <c>Template.Unquote&lt;T&gt;()</c>-initialized locals (call-form, recognized by binding — mirrors
/// <see cref="StagedParameterFinder"/>) and <c>[Unquote]</c> method parameters (recognized by attribute symbol —
/// mirrors <see cref="SpliceParameterFinder"/>). Depth-0 only: a live local that is part of a control-flow
/// region is left for the staging emitter (plan Task 6), not hoisted here.
/// </summary>
internal sealed class StagedRootFinder : CSharpSyntaxWalker
{
    private sealed class LocalSite
    {
        public LocalSite(string name, InvocationExpressionSyntax liveCall, ExpressionSyntax value, LocalDeclarationStatementSyntax declaration)
        {
            Name = name;
            StagedCall = liveCall;
            Value = value;
            Declaration = declaration;
        }

        public string Name { get; }
        public InvocationExpressionSyntax StagedCall { get; }
        public ExpressionSyntax Value { get; }
        public LocalDeclarationStatementSyntax Declaration { get; }
        public List<SyntaxNode> References { get; } = new();
    }

    private sealed class ParameterSite
    {
        public ParameterSite(ParameterSyntax parameter, ITypeSymbol type)
        {
            Parameter = parameter;
            Type = type;
        }

        public ParameterSyntax Parameter { get; }
        public ITypeSymbol Type { get; }
        public List<SyntaxNode> References { get; } = new();
    }

    public static StagedRootResult FindStagedRoots(SemanticModel semanticModel, SyntaxNode node)
    {
        var finder = new StagedRootFinder(semanticModel);
        finder.Visit(node);
        return finder.Resolve();
    }

    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol? _templateSymbol;
    private readonly INamedTypeSymbol? _liveAttributeSymbol;

    // Insertion-ordered so the emitted factory parameters / preamble are deterministic across runs.
    private readonly List<LocalSite> _locals = new();
    private readonly List<ParameterSite> _parameters = new();
    private readonly Dictionary<ISymbol, LocalSite> _localBySymbol = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<ISymbol, ParameterSite> _parameterBySymbol = new(SymbolEqualityComparer.Default);

    private StagedRootFinder(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
        _templateSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(global::Synto.Templating.Template).FullName!);
        _liveAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(UnquoteAttribute).FullName!);
    }

    public override void DefaultVisit(SyntaxNode node)
    {
        if ((_localBySymbol.Count > 0 || _parameterBySymbol.Count > 0) && node is IdentifierNameSyntax identifier)
        {
            var symbol = _semanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol is not null)
            {
                if (_localBySymbol.TryGetValue(symbol, out var localSite))
                {
                    localSite.References.Add(identifier);
                    return;
                }

                if (_parameterBySymbol.TryGetValue(symbol, out var parameterSite))
                {
                    parameterSite.References.Add(identifier);
                    return;
                }
            }
        }

        base.DefaultVisit(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (_templateSymbol is not null
            && _semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol method
            && method.Name == nameof(global::Synto.Templating.Template.Unquote)
            && method.TypeArguments.Length == 1
            && SymbolEqualityComparer.Default.Equals(method.ContainingType, _templateSymbol))
        {
            RegisterLocal(node);
        }

        base.VisitInvocationExpression(node);
    }

    private void RegisterLocal(InvocationExpressionSyntax node)
    {
        // Depth-0 only: `var n = Unquote(<expr>);` declared directly in the template method body block. A live
        // local nested inside a control-flow region stays in the verbatim scaffold (plan Task 6), so it is
        // intentionally NOT recognized here.
        if (node.ArgumentList.Arguments.Count == 1
            && node.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
            && declarator.Parent is VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax localDecl } variableDeclaration
            && variableDeclaration.Variables.Count == 1
            && localDecl.Parent is BlockSyntax { Parent: BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax }
            && _semanticModel.GetDeclaredSymbol(declarator) is { } local)
        {
            var site = new LocalSite(declarator.Identifier.Text, node, node.ArgumentList.Arguments[0].Expression, localDecl);
            _locals.Add(site);
            _localBySymbol[local] = site;
        }
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        if (_liveAttributeSymbol is not null)
        {
            foreach (var attributeList in node.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    if (SymbolEqualityComparer.Default.Equals(_semanticModel.GetTypeInfo(attribute).Type, _liveAttributeSymbol)
                        && _semanticModel.GetDeclaredSymbol(node) is { } parameterSymbol
                        && !_parameterBySymbol.ContainsKey(parameterSymbol))
                    {
                        var site = new ParameterSite(node, parameterSymbol.Type);
                        _parameters.Add(site);
                        _parameterBySymbol[parameterSymbol] = site;
                    }
                }
            }
        }

        base.VisitParameter(node);
    }

    private StagedRootResult Resolve()
    {
        var locals = new List<StagedLocal>();
        foreach (var site in _locals)
            locals.Add(new StagedLocal(site.Name, site.StagedCall, site.Value, site.Declaration, site.References));

        var parameters = new List<StagedParameterRoot>();
        foreach (var site in _parameters)
            parameters.Add(new StagedParameterRoot(site.Parameter, site.Type, site.References));

        return new StagedRootResult(locals, parameters);
    }
}
