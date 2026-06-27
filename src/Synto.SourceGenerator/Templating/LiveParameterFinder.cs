using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

/// <summary>
/// A resolved live template parameter (plan Task 1): one factory parameter lifted from one or more
/// <c>Template.Parameter&lt;T&gt;()</c> sites sharing an identity of <c>(name, T)</c>.
/// </summary>
internal sealed class LiveParameter
{
    public LiveParameter(string name, ITypeSymbol type, ISymbol? symbol, IReadOnlyList<SyntaxNode> references, IReadOnlyList<SyntaxNode> trimNodes)
    {
        Name = name;
        Type = type;
        Symbol = symbol;
        References = references;
        TrimNodes = trimNodes;
    }

    /// <summary>The resolved factory parameter name (explicit <c>parameterName</c> or the bound variable name).</summary>
    public string Name { get; }

    /// <summary>The live value's type <c>T</c> (becomes the factory parameter's type).</summary>
    public ITypeSymbol Type { get; }

    /// <summary>
    /// The declared local symbol for a declaration-position site (<c>var columns = Parameter&lt;T&gt;();</c>),
    /// used as a live root the binding-time classifier seeds liveness from. <c>null</c> for inline-only sites
    /// (which cannot drive control flow).
    /// </summary>
    public ISymbol? Symbol { get; }

    /// <summary>Nodes replaced by the value lift (identifier uses for a declaration; the call itself when inline).</summary>
    public IReadOnlyList<SyntaxNode> References { get; }

    /// <summary>Nodes removed entirely (the <c>var x = Parameter&lt;T&gt;();</c> declaration statement, if any).</summary>
    public IReadOnlyList<SyntaxNode> TrimNodes { get; }
}

/// <summary>The result of discovering live parameters: the resolved parameters plus any naming diagnostics.</summary>
internal sealed class LiveParameterResult
{
    public LiveParameterResult(IReadOnlyList<LiveParameter> parameters, IReadOnlyList<DiagnosticInfo> diagnostics)
    {
        Parameters = parameters;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<LiveParameter> Parameters { get; }
    public IReadOnlyList<DiagnosticInfo> Diagnostics { get; }
}

internal sealed class LiveParameterFinder : CSharpSyntaxWalker
{
    private sealed class Site
    {
        public Site(InvocationExpressionSyntax invocation, ITypeSymbol type, string? explicitName, string? implicitName, LocalDeclarationStatementSyntax? declaration, ISymbol? local)
        {
            Invocation = invocation;
            Type = type;
            ExplicitName = explicitName;
            ImplicitName = implicitName;
            Declaration = declaration;
            Local = local;
        }

        public InvocationExpressionSyntax Invocation { get; }
        public ITypeSymbol Type { get; }
        public string? ExplicitName { get; }
        public string? ImplicitName { get; }
        public LocalDeclarationStatementSyntax? Declaration { get; }
        public ISymbol? Local { get; }
        public List<SyntaxNode> References { get; } = new();

        public bool IsDeclaration => Declaration is not null;
        public string? ResolvedName => ExplicitName ?? ImplicitName;
    }

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

    public static LiveParameterResult FindLiveParameters(SemanticModel semanticModel, SyntaxNode node)
    {
        var finder = new LiveParameterFinder(semanticModel);
        finder.Visit(node);
        return finder.Resolve();
    }

    private readonly SemanticModel _semanticModel;
    private readonly INamedTypeSymbol? _templateSymbol;
    private readonly List<Site> _sites = new();
    private readonly Dictionary<ISymbol, Site> _siteByLocal = new(SymbolEqualityComparer.Default);

    private LiveParameterFinder(SemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
        _templateSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(global::Synto.Templating.Template).FullName!);
    }

    public override void DefaultVisit(SyntaxNode node)
    {
        if (_siteByLocal.Count > 0 && node is IdentifierNameSyntax identifier)
        {
            var symbol = _semanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol is not null && _siteByLocal.TryGetValue(symbol, out var site))
            {
                site.References.Add(identifier);
                return;
            }
        }

        base.DefaultVisit(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (_templateSymbol is not null
            && _semanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol method
            && method.Name == nameof(global::Synto.Templating.Template.Parameter)
            && method.TypeArguments.Length == 1
            && SymbolEqualityComparer.Default.Equals(method.ContainingType, _templateSymbol))
        {
            RegisterSite(node, method.TypeArguments[0]);
        }

        base.VisitInvocationExpression(node);
    }

    private void RegisterSite(InvocationExpressionSyntax node, ITypeSymbol type)
    {
        string? explicitName = null;
        if (node.ArgumentList.Arguments.Count == 1
            && _semanticModel.GetConstantValue(node.ArgumentList.Arguments[0].Expression) is { HasValue: true, Value: string name })
        {
            explicitName = name;
        }

        // Declaration position: `var x = Parameter<T>();` (single declarator).
        if (node.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
            && declarator.Parent is VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax localDecl } variableDeclaration
            && variableDeclaration.Variables.Count == 1
            && _semanticModel.GetDeclaredSymbol(declarator) is { } local)
        {
            var site = new Site(node, type, explicitName, declarator.Identifier.Text, localDecl, local);
            _sites.Add(site);
            _siteByLocal[local] = site;
            return;
        }

        // Inline position: the call itself is the single lift site.
        var inlineSite = new Site(node, type, explicitName, implicitName: null, declaration: null, local: null);
        inlineSite.References.Add(node);
        _sites.Add(inlineSite);
    }

    private LiveParameterResult Resolve()
    {
        var diagnostics = new List<DiagnosticInfo>();
        var valid = new List<Site>();

        // First pass: a resolvable name is required (SY1010 for inline-with-no-name).
        foreach (var site in _sites)
        {
            if (site.ResolvedName is null)
            {
                diagnostics.Add(Diagnostics.LiveParameterMissingName(site.Invocation.GetLocation()));
                continue;
            }

            valid.Add(site);
        }

        var parameters = new List<LiveParameter>();

        foreach (var group in valid.GroupBy(s => s.ResolvedName, System.StringComparer.Ordinal))
        {
            var groupSites = group.ToList();

            // Conflicting (name, T): same name declared with more than one type.
            var distinctTypes = groupSites
                .Select(s => s.Type.ToDisplayString(TypeFormat))
                .Distinct(System.StringComparer.Ordinal)
                .ToList();

            if (distinctTypes.Count > 1)
            {
                var conflicting = groupSites.First(s => s.Type.ToDisplayString(TypeFormat) != distinctTypes[0]);
                diagnostics.Add(Diagnostics.LiveParameterTypeConflict(
                    conflicting.Invocation.GetLocation(),
                    group.Key!,
                    distinctTypes[0],
                    distinctTypes[1]));
                continue;
            }

            // Explicit-name collision: two distinct sites both name the same parameter explicitly.
            if (groupSites.Count > 1 && groupSites.Count(s => s.ExplicitName is not null) > 1)
            {
                var second = groupSites.Where(s => s.ExplicitName is not null).Skip(1).First();
                diagnostics.Add(Diagnostics.LiveParameterNameCollision(second.Invocation.GetLocation(), group.Key!));
                continue;
            }

            var references = groupSites.SelectMany(s => s.References).ToList();
            var trimNodes = groupSites.Where(s => s.Declaration is not null).Select(s => (SyntaxNode)s.Declaration!).ToList();
            var symbol = groupSites.Select(s => s.Local).FirstOrDefault(s => s is not null);

            parameters.Add(new LiveParameter(group.Key!, groupSites[0].Type, symbol, references, trimNodes));
        }

        return new LiveParameterResult(parameters, diagnostics);
    }
}
