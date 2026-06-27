using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Synto.Generators;

namespace Synto.Example.ObjectReader.Generator;

/// <summary>Tracking names for incremental-step assertions (mirrors Synto's own generators).</summary>
internal static class TrackingNames
{
    public const string Transform = nameof(Transform);
}

/// <summary>
/// Intercepts compile-time-constant <c>Synto.Example.ObjectReader.Api.ObjectReader.Create&lt;T&gt;(source,
/// members…)</c> calls and emits, per call site, a type-specialized <see cref="System.Data.IDataReader"/>
/// plus a <c>[InterceptsLocation]</c> interceptor that routes the call to it — no runtime reflection (C-1).
/// Walking-skeleton stage: the reader is built with raw <c>SyntaxFactory</c> (no Synto <c>[Template]</c> yet);
/// unknown / non-constant members are skipped silently (diagnostics arrive in Task 3).
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ObjectReaderGenerator : IIncrementalGenerator
{
    private const string CreateContainingType = "Synto.Example.ObjectReader.Api.ObjectReader";
    private const string GeneratedNamespace = "Synto.Example.ObjectReader.Generated";

    private static readonly SymbolDisplayFormat FullyQualified = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var models = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsCandidate(node),
                transform: static (ctx, ct) => Transform(ctx, ct))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!.Value)
            .WithTrackingName(TrackingNames.Transform);

        context.RegisterSourceOutput(models.Collect(), static (spc, all) => Emit(spc, all));
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        if (node is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } invocation)
        {
            return false;
        }

        if (invocation.ArgumentList.Arguments.Count < 1)
        {
            return false;
        }

        string? name = access.Name switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => null,
        };

        return name == "Create";
    }

    private static ObjectReaderModel? Transform(GeneratorSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;
        LocationInfo? invocationLocation = LocationInfo.CreateFrom(invocation.GetLocation());

        try
        {
            return TransformCore(ctx, invocation, invocationLocation, ct);
        }
        catch (System.OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // any unexpected failure becomes SOR0000 — it is never thrown out of the pipeline (C-5).
        catch (System.Exception ex)
#pragma warning restore CA1031
        {
            var info = new PendingDiagnostic(
                DiagnosticKind.InternalError,
                invocationLocation,
                new EquatableArray<string>(ImmutableArray.Create(ex.GetType().FullName ?? "System.Exception", ex.Message)));

            return new ObjectReaderModel(
                string.Empty,
                string.Empty,
                default,
                new EquatableArray<PendingDiagnostic>(ImmutableArray.Create(info)),
                string.Empty,
                Intercept: false);
        }
    }

    private static ObjectReaderModel? TransformCore(
        GeneratorSyntaxContext ctx,
        InvocationExpressionSyntax invocation,
        LocationInfo? invocationLocation,
        System.Threading.CancellationToken ct)
    {
        SemanticModel semanticModel = ctx.SemanticModel;

        if (semanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol method)
        {
            return null;
        }

        if (method.Name != "Create"
            || method.ContainingType?.ToDisplayString() != CreateContainingType
            || method.TypeArguments.Length != 1)
        {
            return null;
        }

        ITypeSymbol target = method.TypeArguments[0];
        string targetQualified = target.ToDisplayString(FullyQualified);

        var arguments = invocation.ArgumentList.Arguments;
        var columns = new List<ColumnInfo>();
        var diagnostics = new List<PendingDiagnostic>();
        bool allConstant = true;

        for (int i = 1; i < arguments.Count; i++)
        {
            Optional<object?> constant = semanticModel.GetConstantValue(arguments[i].Expression, ct);
            if (!constant.HasValue || constant.Value is not string memberName)
            {
                // Non-constant member → the whole call is not a constant target (SOR0002, reported once below).
                allConstant = false;
                continue;
            }

            string? columnType = ResolveMemberType(target, memberName);
            if (columnType is null)
            {
                // Unknown member → record SOR0001 and skip the column (C-2).
                diagnostics.Add(new PendingDiagnostic(
                    DiagnosticKind.MemberNotFound,
                    LocationInfo.CreateFrom(arguments[i].Expression.GetLocation()),
                    new EquatableArray<string>(ImmutableArray.Create(memberName, targetQualified))));
                continue;
            }

            columns.Add(new ColumnInfo(columns.Count, memberName, columnType));
        }

        if (!allConstant)
        {
            // A non-constant member list is never intercepted (C-4 runtime fallback applies); report SOR0002
            // and flow a diagnostics-only model so nothing is emitted for this call site.
            diagnostics.Add(new PendingDiagnostic(DiagnosticKind.MembersNotConstant, invocationLocation, default));
            return new ObjectReaderModel(
                targetQualified,
                target.Name,
                default,
                new EquatableArray<PendingDiagnostic>(diagnostics.ToImmutableArray()),
                string.Empty,
                Intercept: false);
        }

        var location = semanticModel.GetInterceptableLocation(invocation, ct);
        if (location is null)
        {
            return null;
        }

        return new ObjectReaderModel(
            targetQualified,
            target.Name,
            new EquatableArray<ColumnInfo>(columns.ToImmutableArray()),
            new EquatableArray<PendingDiagnostic>(diagnostics.ToImmutableArray()),
            location.GetInterceptsLocationAttributeSyntax(),
            Intercept: true);
    }

    private static string? ResolveMemberType(ITypeSymbol target, string memberName)
    {
        for (ITypeSymbol? type = target; type is not null && type.SpecialType != SpecialType.System_Object; type = type.BaseType)
        {
            foreach (ISymbol member in type.GetMembers(memberName))
            {
                if (member.IsStatic || member.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                switch (member)
                {
                    case IPropertySymbol { GetMethod: not null } property:
                        return property.Type.ToDisplayString(FullyQualified);
                    case IFieldSymbol field:
                        return field.Type.ToDisplayString(FullyQualified);
                }
            }
        }

        return null;
    }

    private static void Emit(SourceProductionContext spc, System.Collections.Immutable.ImmutableArray<ObjectReaderModel> models)
    {
        if (models.IsDefaultOrEmpty)
        {
            return;
        }

        // Replay every model's diagnostics (intercepting or diagnostics-only) — materialized here, never
        // thrown (C-5), via the Synto.Diagnostics-generated factory methods (D5).
        foreach (ObjectReaderModel model in models)
        {
            foreach (PendingDiagnostic info in model.Diagnostics)
            {
                spc.ReportDiagnostic(ToDiagnostic(info));
            }
        }

        // Only constant-target calls are intercepted; a diagnostics-only model (e.g. SOR0002) emits nothing.
        var intercepting = new List<ObjectReaderModel>();
        foreach (ObjectReaderModel model in models)
        {
            if (model.Intercept)
            {
                intercepting.Add(model);
            }
        }

        if (intercepting.Count == 0)
        {
            return;
        }

        var members = new List<MemberDeclarationSyntax>();
        var interceptors = new StringBuilder();

        for (int index = 0; index < intercepting.Count; index++)
        {
            ObjectReaderModel model = intercepting[index];
            members.Add(BuildReader(model, index));
            interceptors.Append(BuildInterceptorMethod(model, index));
        }

        members.Add(SyntaxFactory.ParseMemberDeclaration(
            $$"""
            file static class ObjectReaderInterceptors
            {
            {{interceptors}}}
            """)!);

        var unit = SyntaxFactory.CompilationUnit()
            .AddMembers(SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(GeneratedNamespace))
                .AddMembers(members.ToArray()))
            .NormalizeWhitespace();

        spc.AddSource("ObjectReader.g.cs", SourceText.From(unit.ToFullString(), Encoding.UTF8));
        global::Synto.Generators.Interceptors.AddDefinition(spc);
    }

    // Materialize an equatable PendingDiagnostic into a real Diagnostic via the Synto.Diagnostics-generated
    // factory methods (the dog-food, D5). Runs only in the output stage, off the cached pipeline value.
    private static Diagnostic ToDiagnostic(PendingDiagnostic info)
    {
        Location? location = info.Location?.ToLocation();
        return info.Kind switch
        {
            DiagnosticKind.MemberNotFound => Diagnostics.MemberNotFound(location, info.Arguments[0], info.Arguments[1]),
            DiagnosticKind.MembersNotConstant => Diagnostics.MembersNotConstant(location),
            _ => Diagnostics.InternalError(location, info.Arguments[0], info.Arguments[1]),
        };
    }

    // The dog-food (plan Task 9): quote the FULLY data-driven IDataReader from the Synto live-staged [Template]
    // (ReaderTemplate.cs). The template lifts the resolved column list to the factory (`EquatableArray<ColumnInfo>
    // columns` + the degenerate `int fieldCount`), and its live `foreach`/`Member`/`TypeOf` shapes are unrolled at
    // factory time — so there is no raw-SyntaxFactory text-gluing here any more. We only rename the class + ctor to
    // the call-site reader name, make it `file sealed`, and add the `: IDataReader` base list.
    private static MemberDeclarationSyntax BuildReader(ObjectReaderModel model, int index)
    {
        string reader = $"ObjectReader_{model.TargetTypeShortName}_{index}";
        TypeSyntax elementType = SyntaxFactory.ParseTypeName(model.TargetTypeQualifiedName);

        // Synto holes: [Inline(AsSyntax = true)] T splices the element type wherever T appears (the _e field + the
        // ctor's IEnumerable<T> parameter); the live `columns`/`fieldCount` parameters drive the unrolled members.
        ClassDeclarationSyntax skeleton = Factory.ObjectReaderTemplate(elementType, model.Columns.Count, model.Columns);

        var specialized = SyntaxFactory.List(
            skeleton.Members.Select(member => RenameConstructor(member, reader)));

        return skeleton
            .WithIdentifier(SyntaxFactory.Identifier(reader))
            .WithModifiers(SyntaxFactory.TokenList(
                SyntaxFactory.Token(SyntaxKind.FileKeyword),
                SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName("global::System.Data.IDataReader")))))
            .WithMembers(specialized);
    }

    // Rename the templated constructor to match the specialized reader; every other member is emitted verbatim.
    private static MemberDeclarationSyntax RenameConstructor(MemberDeclarationSyntax member, string reader) =>
        member is ConstructorDeclarationSyntax ctor
            ? ctor.WithIdentifier(SyntaxFactory.Identifier(reader))
            : member;

    private static string BuildInterceptorMethod(ObjectReaderModel model, int index)
    {
        string reader = $"ObjectReader_{model.TargetTypeShortName}_{index}";
        string t = model.TargetTypeQualifiedName;

        // R1: the intercepted Create<T> is generic, so the interceptor must share its arity (Create_N<T>); the
        // call-site type argument (T == the target) is bridged to the specialized reader via (object) then a
        // downcast to IEnumerable<target>. Logged as friction — the double-cast is interceptor-binding tax.
        return
            $$"""
                {{model.InterceptsLocationAttribute}}
                public static global::System.Data.IDataReader Create_{{index}}<T>(global::System.Collections.Generic.IEnumerable<T> source, params string[] members)
                    => new {{reader}}((global::System.Collections.Generic.IEnumerable<{{t}}>)(object)source);

            """;
    }
}
