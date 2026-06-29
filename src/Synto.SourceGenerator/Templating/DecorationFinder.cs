using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

/// <summary>
/// Model struct carrying one post-quote decoration hook: the name of the helper method to invoke
/// on the quoted node expression and the argument expressions to pass. Populated by
/// <see cref="DecorationFinder"/> (Task 4) and consumed by <see cref="TemplateSyntaxQuoter"/>.
/// </summary>
internal readonly struct AppliedDecoration
{
    public AppliedDecoration(string helperMethodName, ImmutableArray<ExpressionSyntax> arguments)
        => (HelperMethodName, Arguments) = (helperMethodName, arguments);
    public string HelperMethodName { get; }
    public ImmutableArray<ExpressionSyntax> Arguments { get; }
}

/// <summary>A factory parameter injected by a decoration (the <c>[Identifier]</c> hole): name + declared type.</summary>
internal readonly struct InjectedParameter
{
    public InjectedParameter(string name, TypeSyntax type) => (Name, Type) = (name, type);
    public string Name { get; }
    public TypeSyntax Type { get; }
}

/// <summary>
/// The transform-local result of decoration discovery: the per-node ordered <c>Apply…</c> hook chains, the
/// decoration attribute syntax to trim from the quoted output, and the factory parameters to inject. All three
/// are consumed WITHIN the <c>GenerateTemplate</c> transform to drive the quoter / factory signature; none of
/// them flow out as cached pipeline state.
/// </summary>
internal readonly struct DecorationResult
{
    public DecorationResult(
        IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>> hooks,
        IReadOnlyList<AttributeSyntax> trimAttributes,
        IReadOnlyList<InjectedParameter> injectedParameters)
    {
        Hooks = hooks;
        TrimAttributes = trimAttributes;
        InjectedParameters = injectedParameters;
    }

    public IReadOnlyDictionary<SyntaxNode, ImmutableArray<AppliedDecoration>> Hooks { get; }
    public IReadOnlyList<AttributeSyntax> TrimAttributes { get; }
    public IReadOnlyList<InjectedParameter> InjectedParameters { get; }
}

/// <summary>
/// Walks a <c>[Template]</c> carrier (through the <see cref="TemplateScope"/> ownership boundary, so a nested
/// child <c>[Template]</c>'s decorations are SKIPPED — they belong to the child's own factory) and maps each
/// decorated type declaration to an ordered <see cref="AppliedDecoration"/> chain, the injected
/// <c>[Identifier]</c> parameter, and the decoration attribute nodes to trim.
/// <para>
/// Ordering per declaration is fixed: <c>Identifier</c>, <c>Visibility</c>, <c>Sealed</c>, <c>Inherits</c>,
/// then each <c>Implements</c> in source order — which is also the chained-call emission order
/// (<c>q.ApplyIdentifierAttribute(…).ApplyVisibilityAttribute(…)…</c>).
/// </para>
/// All work is semantic and done INSIDE the <c>GenerateTemplate</c> transform; nothing here is captured into
/// cached pipeline state. Validation (Task 5) accumulates a <see cref="DiagnosticInfo"/> and DROPS the offending
/// decoration (accumulate-and-continue, mirroring <c>ValidateTemplate</c>); the rest of the template still
/// generates.
/// </summary>
internal static class DecorationFinder
{
    public static DecorationResult FindDecorations(
        SemanticModel model,
        SyntaxNode carrier,
        TemplateScope scope,
        DecorationMarkers markers,
        ISet<string> existingParamNames,
        List<DiagnosticInfo> diagnostics)
    {
        var walker = new Walker(model, carrier, scope, markers, existingParamNames, diagnostics);

        // The carrier root may itself carry decorations. When it is a TYPE the type-walk below reaches it;
        // when it is a METHOD (a [Template] on a method) the scoped walk never visits its own AttributeLists,
        // and overriding VisitMethodDeclaration would break the TemplateScopedWalker foreign-skip contract.
        // So inspect the carrier-root node directly for decorations (it is never foreign — it IS the carrier).
        if (carrier is not BaseTypeDeclarationSyntax)
            walker.ProcessDeclaration(carrier);

        walker.Visit(carrier);
        return new DecorationResult(walker.Hooks, walker.TrimAttributes, walker.InjectedParameters);
    }

    private sealed class Walker : TemplateScopedWalker
    {
        private readonly SemanticModel _model;
        private readonly SyntaxNode _carrierRoot;
        private readonly DecorationMarkers _markers;
        private readonly ISet<string> _existingParamNames;
        private readonly List<DiagnosticInfo> _diagnostics;

        public Walker(
            SemanticModel model,
            SyntaxNode carrierRoot,
            TemplateScope scope,
            DecorationMarkers markers,
            ISet<string> existingParamNames,
            List<DiagnosticInfo> diagnostics)
            : base(scope)
        {
            _model = model;
            _carrierRoot = carrierRoot;
            _markers = markers;
            _existingParamNames = existingParamNames;
            _diagnostics = diagnostics;
        }

        public Dictionary<SyntaxNode, ImmutableArray<AppliedDecoration>> Hooks { get; } = new();
        public List<AttributeSyntax> TrimAttributes { get; } = new();
        public List<InjectedParameter> InjectedParameters { get; } = new();

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            ProcessDeclaration(node);
            base.VisitClassDeclaration(node);
        }

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
        {
            ProcessDeclaration(node);
            base.VisitStructDeclaration(node);
        }

        /// <summary>
        /// Classifies a declaration's attributes into the built-in buckets (+ user-defined candidates), validates
        /// each (SY1022–SY1028), drops the offending decoration, and records the surviving ordered hook chain.
        /// <paramref name="node"/> may be a type declaration (reached by the scoped type-walk) or the carrier-root
        /// method (inspected directly so SY1022 fires for e.g. <c>[Implements&lt;T&gt;]</c> on a method).
        /// </summary>
        public void ProcessDeclaration(SyntaxNode node)
        {
            if (node is not MemberDeclarationSyntax member || member.AttributeLists.Count == 0)
                return;

            if (_model.GetDeclaredSymbol(node) is not { } declaredSymbol)
                return;

            // Buckets, classified by resolved attribute symbol (never by string-matching the attribute name).
            AttributeData? identifier = null;
            var visibilities = new List<AttributeData>();
            AttributeData? @sealed = null;
            var inheritsAll = new List<AttributeData>();
            var implements = new List<AttributeData>();
            var userDefined = new List<AttributeData>();
            var decorationAttributeSyntax = new List<AttributeSyntax>();

            foreach (var attribute in declaredSymbol.GetAttributes())
            {
                if (attribute.AttributeClass is not { } attributeClass)
                    continue;

                bool isDecoration = true;

                if (SymbolEqualityComparer.Default.Equals(attributeClass, _markers.Identifier))
                    identifier = attribute;
                else if (SymbolEqualityComparer.Default.Equals(attributeClass, _markers.Visibility))
                    visibilities.Add(attribute);
                else if (SymbolEqualityComparer.Default.Equals(attributeClass, _markers.Sealed))
                    @sealed = attribute;
                else if (IsUnboundMatch(attributeClass, _markers.InheritsUnbound))
                    inheritsAll.Add(attribute);
                else if (IsUnboundMatch(attributeClass, _markers.ImplementsUnbound))
                    implements.Add(attribute);
                else if (IsUserDecorationCandidate(attributeClass))
                    userDefined.Add(attribute);
                else
                    isDecoration = false; // an ordinary attribute — carried through, NOT a decoration.

                if (isDecoration && attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax attributeSyntax)
                    decorationAttributeSyntax.Add(attributeSyntax);
            }

            if (identifier is null && visibilities.Count == 0 && @sealed is null
                && inheritsAll.Count == 0 && implements.Count == 0 && userDefined.Count == 0)
            {
                return;
            }

            bool isType = node is BaseTypeDeclarationSyntax;
            var chain = ImmutableArray.CreateBuilder<AppliedDecoration>();

            // ----- SY1025 conflicts (duplicate/conflicting decorations on one node) ----------------------
            if (visibilities.Count > 1)
            {
                Report(TemplateDiagnostics.DecorationConflict(
                    LocationOf(visibilities[1]), "more than one [Visibility]"));
            }

            if (inheritsAll.Count > 1)
            {
                Report(TemplateDiagnostics.DecorationConflict(
                    LocationOf(inheritsAll[1]), "more than one [Inherits]"));
            }

            if (@sealed is not null && !isType)
            {
                Report(TemplateDiagnostics.DecorationConflict(LocationOf(@sealed), "[Sealed] on a non-type declaration"));
                @sealed = null; // drop it (also a SY1022-shaped target error, but SY1025 owns the [Sealed]-on-non-type case).
            }

            // [Identifier] — this-type TypeDeclarationSyntax; inject a `string identifier` param + pass it.
            if (identifier is not null)
            {
                if (!isType)
                {
                    Report(TemplateDiagnostics.DecorationTargetMismatch(
                        LocationOf(identifier), "[Identifier]", "TypeDeclarationSyntax"));
                }
                else
                {
                    string paramName = "identifier";
                    while (!_existingParamNames.Add(paramName))
                        paramName += '_';

                    InjectedParameters.Add(new InjectedParameter(paramName, PredefinedType(Token(SyntaxKind.StringKeyword))));
                    chain.Add(new AppliedDecoration(
                        nameof(IdentifierAttributeExtensions.ApplyIdentifierAttribute),
                        ImmutableArray.Create<ExpressionSyntax>(IdentifierName(paramName))));
                }
            }

            // [Visibility(Access.X)] — this-type MemberDeclarationSyntax (so a method is fine). SY1023: File is
            // top-level only. Use only the FIRST [Visibility] (a duplicate already reported SY1025).
            if (visibilities.Count > 0)
            {
                var visibility = visibilities[0];
                if (visibility.ConstructorArguments.Length == 1)
                {
                    bool ok = true;

                    if (IsFileAccess(visibility.ConstructorArguments[0].Value) && !ReferenceEquals(node, _carrierRoot))
                    {
                        Report(TemplateDiagnostics.DecorationFileNotTopLevel(
                            LocationOf(visibility), DeclarationName(node)));
                        ok = false;
                    }

                    string? accessMember = AccessMemberName(visibility.ConstructorArguments[0].Value);
                    if (ok && accessMember is not null)
                    {
                        var accessAccess = MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            ParseName(_markers.AccessEnum.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
                            IdentifierName(accessMember));

                        chain.Add(new AppliedDecoration(
                            nameof(VisibilityAttributeExtensions.ApplyVisibilityAttribute),
                            ImmutableArray.Create<ExpressionSyntax>(accessAccess)));
                    }
                }
            }

            // [Sealed] — no args; this-type TypeDeclarationSyntax (non-type already dropped above).
            if (@sealed is not null)
            {
                chain.Add(new AppliedDecoration(
                    nameof(SealedAttributeExtensions.ApplySealedAttribute),
                    ImmutableArray<ExpressionSyntax>.Empty));
            }

            // [Inherits<TBase>] — base list first. this-type TypeDeclarationSyntax. SY1024: T must be a non-sealed
            // class. Use only the FIRST [Inherits] (a duplicate already reported SY1025).
            if (inheritsAll.Count > 0)
            {
                var inherits = inheritsAll[0];
                if (!isType)
                {
                    Report(TemplateDiagnostics.DecorationTargetMismatch(
                        LocationOf(inherits), "[Inherits]", "TypeDeclarationSyntax"));
                }
                else if (TryGetTypeArg(inherits, out var baseType, out var baseFqn))
                {
                    if (baseType.TypeKind != TypeKind.Class || baseType.IsSealed)
                    {
                        Report(TemplateDiagnostics.DecorationBadBaseType(
                            LocationOf(inherits), baseFqn, "[Inherits<T>] requires T to be a non-sealed class"));
                    }
                    else
                    {
                        chain.Add(new AppliedDecoration(
                            nameof(InheritsAttributeExtensions.ApplyInheritsAttribute),
                            ImmutableArray.Create<ExpressionSyntax>(StringLiteral(baseFqn))));
                    }
                }
            }

            // [Implements<TInterface>] — each in source order. this-type TypeDeclarationSyntax. SY1024: T must be
            // an interface.
            foreach (var implementsAttribute in implements)
            {
                if (!isType)
                {
                    Report(TemplateDiagnostics.DecorationTargetMismatch(
                        LocationOf(implementsAttribute), "[Implements]", "TypeDeclarationSyntax"));
                    continue;
                }

                if (TryGetTypeArg(implementsAttribute, out var ifaceType, out var ifaceFqn))
                {
                    if (ifaceType.TypeKind != TypeKind.Interface)
                    {
                        Report(TemplateDiagnostics.DecorationBadBaseType(
                            LocationOf(implementsAttribute), ifaceFqn, "[Implements<T>] requires T to be an interface"));
                    }
                    else
                    {
                        chain.Add(new AppliedDecoration(
                            nameof(ImplementsAttributeExtensions.ApplyImplementsAttribute),
                            ImmutableArray.Create<ExpressionSyntax>(StringLiteral(ifaceFqn))));
                    }
                }
            }

            // ----- User-defined decorations (the open-by-construction path; SY1026/1027/1028) ------------
            foreach (var userAttribute in userDefined)
                ProcessUserDecoration(node, isType, userAttribute, chain);

            if (chain.Count > 0)
                Hooks[node] = chain.ToImmutable();

            // Trim every decoration attribute we recognized, even if it was dropped — a dropped decoration must
            // not leak into the quoted output as a stray attribute.
            TrimAttributes.AddRange(decorationAttributeSyntax);
        }

        /// <summary>
        /// Validates and (if valid) records a single user-defined decoration. Detection signal: a source-declared
        /// method named <c>Apply{AttributeName}</c> exists; validation then checks it is a usable, type-preserving
        /// extension (SY1027 arity/resolvability, SY1028 return type) and that the attribute type is
        /// constructor-args-only (SY1026).
        /// </summary>
        private void ProcessUserDecoration(
            SyntaxNode node,
            bool isType,
            AttributeData attribute,
            ImmutableArray<AppliedDecoration>.Builder chain)
        {
            if (attribute.AttributeClass is not { } attributeClass)
                return;

            string attributeName = attributeClass.Name; // e.g. "FooAttribute"
            string applyName = "Apply" + attributeName;  // convention: ApplyFooAttribute
            int ctorArgCount = attribute.ConstructorArguments.Length;

            // SY1026 — the attribute type must declare NO settable property/field (constructor-params only).
            foreach (var settable in SettableMembers(attributeClass))
            {
                Report(TemplateDiagnostics.DecorationSettableMember(
                    LocationOf(attribute), attributeClass.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), settable));
                return; // drop the decoration; one report per offending attribute is enough.
            }

            // Resolve the Apply{Name} hook: a usable extension method with matching arity (this + ctor-arg count).
            IMethodSymbol? hook = ResolveApplyHook(applyName, ctorArgCount, out bool nameExistsButUnusable);

            // SY1027 — no usable Apply hook (none by name, OR present-but-not-a-usable-extension, OR arity mismatch).
            if (hook is null)
            {
                Report(TemplateDiagnostics.DecorationNoApplyHook(LocationOf(attribute), attributeName, ctorArgCount));
                _ = nameExistsButUnusable;
                return;
            }

            // SY1028 — the hook must be type-preserving (return type == its this-parameter type) so calls compose.
            var thisType = hook.Parameters[0].Type;
            if (!SymbolEqualityComparer.Default.Equals(hook.ReturnType, thisType))
            {
                Report(TemplateDiagnostics.DecorationApplyNonComposing(
                    LocationOf(attribute),
                    attributeName,
                    hook.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    thisType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                return;
            }

            // SY1022 — the decorated node kind must be assignable to the hook's this-type. We compare against the
            // hook's type-parameter constraint (the `where T : …Syntax` bound) when generic, else the concrete
            // this-type. Conservative: a TYPE node satisfies a TypeDeclarationSyntax-bounded hook; a method does not.
            if (!NodeSatisfiesThisType(node, isType, thisType))
            {
                Report(TemplateDiagnostics.DecorationTargetMismatch(
                    LocationOf(attribute),
                    "[" + StripAttributeSuffix(attributeName) + "]",
                    ThisTypeDisplay(thisType)));
                return;
            }

            // Valid user decoration: emit Apply{Name}(<ctor-arg literals>) in source order.
            var args = ImmutableArray.CreateBuilder<ExpressionSyntax>(ctorArgCount);
            foreach (var ctorArg in attribute.ConstructorArguments)
            {
                if (!TryRenderConstant(ctorArg, out var argExpr))
                {
                    // An argument shape we cannot render — drop the whole decoration rather than emit broken code.
                    Report(TemplateDiagnostics.DecorationNoApplyHook(LocationOf(attribute), attributeName, ctorArgCount));
                    return;
                }

                args.Add(argExpr);
            }

            chain.Add(new AppliedDecoration(applyName, args.ToImmutable()));
        }

        private void Report(DiagnosticInfo diagnostic) => _diagnostics.Add(diagnostic);

        /// <summary>Whether <paramref name="attributeClass"/> is a closed construction of <paramref name="unbound"/>.</summary>
        private static bool IsUnboundMatch(INamedTypeSymbol attributeClass, INamedTypeSymbol unbound) =>
            attributeClass is { IsGenericType: true }
            && SymbolEqualityComparer.Default.Equals(attributeClass.ConstructUnboundGenericType(), unbound);

        /// <summary>
        /// A non-built-in attribute is a user-defined decoration candidate when a source-declared static method
        /// named <c>Apply{AttributeName}</c> exists in the compilation's assembly. This is the convention signal;
        /// ordinary attributes (no such method) are carried through verbatim, untouched.
        /// </summary>
        private bool IsUserDecorationCandidate(INamedTypeSymbol attributeClass)
        {
            string applyName = "Apply" + attributeClass.Name;
            foreach (var _ in EnumerateMethodsNamed(applyName))
                return true;

            return false;
        }

        /// <summary>
        /// The <c>Apply{Name}</c> hook to invoke: a usable extension method (static, <c>IsExtensionMethod</c>) whose
        /// total parameter arity is 1 (the receiver) + <paramref name="ctorArgCount"/>. Returns null when none is
        /// usable; sets <paramref name="nameExistsButUnusable"/> if a same-named method exists but doesn't qualify.
        /// </summary>
        private IMethodSymbol? ResolveApplyHook(string applyName, int ctorArgCount, out bool nameExistsButUnusable)
        {
            nameExistsButUnusable = false;
            foreach (var method in EnumerateMethodsNamed(applyName))
            {
                if (method.IsExtensionMethod && method.Parameters.Length == 1 + ctorArgCount)
                    return method;

                nameExistsButUnusable = true;
            }

            return null;
        }

        /// <summary>All source-declared static methods named <paramref name="name"/> in the current assembly.</summary>
        private IEnumerable<IMethodSymbol> EnumerateMethodsNamed(string name)
        {
            foreach (var type in EnumerateTypes(_model.Compilation.Assembly.GlobalNamespace))
            {
                foreach (var member in type.GetMembers(name))
                {
                    if (member is IMethodSymbol { IsStatic: true } method)
                        yield return method;
                }
            }
        }

        private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns)
        {
            foreach (var type in ns.GetTypeMembers())
                yield return type;

            foreach (var child in ns.GetNamespaceMembers())
            {
                foreach (var type in EnumerateTypes(child))
                    yield return type;
            }
        }

        /// <summary>Settable properties (have a set accessor) or non-readonly instance fields on a decoration attribute type.</summary>
        private static IEnumerable<string> SettableMembers(INamedTypeSymbol attributeClass)
        {
            foreach (var member in attributeClass.GetMembers())
            {
                switch (member)
                {
                    case IPropertySymbol { SetMethod: not null } property when !property.IsStatic:
                        yield return property.Name;
                        break;
                    case IFieldSymbol { IsReadOnly: false, IsConst: false, IsStatic: false, IsImplicitlyDeclared: false } field:
                        yield return field.Name;
                        break;
                }
            }
        }

        /// <summary>Whether the decorated node satisfies the hook's this-type (mechanical, syntax-kind based).</summary>
        private static bool NodeSatisfiesThisType(SyntaxNode node, bool isType, ITypeSymbol thisType)
        {
            string thisName = ThisTypeName(thisType);
            return thisName switch
            {
                "TypeDeclarationSyntax" or "BaseTypeDeclarationSyntax" => isType,
                "MemberDeclarationSyntax" or "CSharpSyntaxNode" or "SyntaxNode" => node is MemberDeclarationSyntax,
                "ClassDeclarationSyntax" => node is ClassDeclarationSyntax,
                "StructDeclarationSyntax" => node is StructDeclarationSyntax,
                "MethodDeclarationSyntax" => node is MethodDeclarationSyntax,
                // Unknown bound: be permissive for a type node (the built-in shapes are type-targeted) so a
                // bespoke base type still composes; the C# compiler will catch a genuinely-wrong cast downstream.
                _ => true,
            };
        }

        /// <summary>The simple name of the hook's this-type, unwrapping a generic type-parameter to its constraint.</summary>
        private static string ThisTypeName(ITypeSymbol thisType)
        {
            if (thisType is ITypeParameterSymbol typeParameter)
            {
                foreach (var constraint in typeParameter.ConstraintTypes)
                    return constraint.Name;

                return "SyntaxNode";
            }

            return thisType.Name;
        }

        private static string ThisTypeDisplay(ITypeSymbol thisType)
        {
            if (thisType is ITypeParameterSymbol typeParameter)
            {
                foreach (var constraint in typeParameter.ConstraintTypes)
                    return constraint.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }

            return thisType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static string DeclarationName(SyntaxNode node) => node switch
        {
            BaseTypeDeclarationSyntax t => t.Identifier.Text,
            MethodDeclarationSyntax m => m.Identifier.Text,
            _ => node.Kind().ToString(),
        };

        private bool IsFileAccess(object? constantValue) => string.Equals(AccessMemberName(constantValue), "File", System.StringComparison.Ordinal);

        /// <summary>The <c>Access</c> enum field name whose constant value equals <paramref name="constantValue"/>.</summary>
        private string? AccessMemberName(object? constantValue)
        {
            foreach (var member in _markers.AccessEnum.GetMembers())
            {
                if (member is IFieldSymbol { HasConstantValue: true } field && object.Equals(field.ConstantValue, constantValue))
                    return field.Name;
            }

            return null;
        }

        /// <summary>The generic decoration's single type argument: its symbol + its <c>global::</c>-qualified FQN.</summary>
        private static bool TryGetTypeArg(AttributeData attribute, out ITypeSymbol typeArgument, out string fqn)
        {
            typeArgument = null!;
            fqn = string.Empty;

            if (attribute.AttributeClass is not { TypeArguments.Length: 1 } attributeClass)
                return false;

            typeArgument = attributeClass.TypeArguments[0];
            fqn = typeArgument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return true;
        }

        private static ExpressionSyntax StringLiteral(string value) =>
            LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value));

        /// <summary>Renders a decoration constructor argument (string/primitive/enum) as a literal expression.</summary>
        private static bool TryRenderConstant(TypedConstant constant, out ExpressionSyntax expression)
        {
            expression = null!;

            if (constant.Kind == TypedConstantKind.Error || constant.IsNull)
                return false;

            switch (constant.Value)
            {
                case string s:
                    expression = StringLiteral(s);
                    return true;
                case bool b:
                    expression = LiteralExpression(b ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression);
                    return true;
                case int i:
                    expression = LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(i));
                    return true;
                default:
                    return false;
            }
        }

        private static string StripAttributeSuffix(string name) =>
            name.EndsWith("Attribute", System.StringComparison.Ordinal) ? name.Substring(0, name.Length - "Attribute".Length) : name;

        private static Location? LocationOf(AttributeData attribute) =>
            attribute.ApplicationSyntaxReference?.GetSyntax() is { } syntax ? syntax.GetLocation() : null;
    }
}
