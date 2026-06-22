using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

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
            var info = new DiagnosticInfo(
                DiagnosticKind.InternalError,
                invocationLocation,
                new EquatableArray<string>(new[] { ex.GetType().FullName ?? "System.Exception", ex.Message }));

            return new ObjectReaderModel(
                string.Empty,
                string.Empty,
                default,
                new EquatableArray<DiagnosticInfo>(new[] { info }),
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
        var diagnostics = new List<DiagnosticInfo>();
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
                diagnostics.Add(new DiagnosticInfo(
                    DiagnosticKind.MemberNotFound,
                    LocationInfo.CreateFrom(arguments[i].Expression.GetLocation()),
                    new EquatableArray<string>(new[] { memberName, targetQualified })));
                continue;
            }

            columns.Add(new ColumnInfo(memberName, columnType));
        }

        if (!allConstant)
        {
            // A non-constant member list is never intercepted (C-4 runtime fallback applies); report SOR0002
            // and flow a diagnostics-only model so nothing is emitted for this call site.
            diagnostics.Add(new DiagnosticInfo(DiagnosticKind.MembersNotConstant, invocationLocation, default));
            return new ObjectReaderModel(
                targetQualified,
                target.Name,
                default,
                new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()),
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
            new EquatableArray<ColumnInfo>(columns.ToArray()),
            new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()),
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
            foreach (DiagnosticInfo info in model.Diagnostics)
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
            members.Add(SyntaxFactory.ParseMemberDeclaration(BuildReader(model, index))!);
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
        spc.AddSource("InterceptsLocationAttribute.g.cs", SourceText.From(InterceptsLocationAttributeSource, Encoding.UTF8));
    }

    // Materialize an equatable DiagnosticInfo into a real Diagnostic via the Synto.Diagnostics-generated
    // factory methods (the dog-food, D5). Runs only in the output stage, off the cached pipeline value.
    private static Diagnostic ToDiagnostic(DiagnosticInfo info)
    {
        Location? location = info.Location?.ToLocation();
        return info.Kind switch
        {
            DiagnosticKind.MemberNotFound => Diagnostics.MemberNotFound(location, info.Arguments[0], info.Arguments[1]),
            DiagnosticKind.MembersNotConstant => Diagnostics.MembersNotConstant(location),
            _ => Diagnostics.InternalError(location, info.Arguments[0], info.Arguments[1]),
        };
    }

    // The SDK does not ship System.Runtime.CompilerServices.InterceptsLocationAttribute, so the generator
    // emits the (BCL-only, self-contained — C-6) definition the version-stamped [InterceptsLocation] usage
    // binds to. Friction (R3): interceptor plumbing the consumer never sees but the generator must provide.
    private const string InterceptsLocationAttributeSource =
        """
        // <auto-generated/>
        namespace System.Runtime.CompilerServices
        {
            [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
            internal sealed class InterceptsLocationAttribute : global::System.Attribute
            {
                public InterceptsLocationAttribute(int version, string data)
                {
                    _ = version;
                    _ = data;
                }
            }
        }
        """;

    private static string BuildReader(ObjectReaderModel model, int index)
    {
        string reader = $"ObjectReader_{model.TargetTypeShortName}_{index}";
        string t = model.TargetTypeQualifiedName;

        var nameArms = new StringBuilder();
        var ordinalArms = new StringBuilder();
        var fieldTypeArms = new StringBuilder();
        var valueArms = new StringBuilder();
        for (int i = 0; i < model.Columns.Count; i++)
        {
            ColumnInfo column = model.Columns[i];
            nameArms.Append($"            {i} => \"{column.Name}\",\n");
            ordinalArms.Append($"            \"{column.Name}\" => {i},\n");
            fieldTypeArms.Append($"            {i} => typeof({column.ColumnTypeName}),\n");
            valueArms.Append($"            {i} => (object?)_e.Current.{column.Name} ?? global::System.DBNull.Value,\n");
        }

        return
            $$"""
            file sealed class {{reader}} : global::System.Data.IDataReader
            {
                private readonly global::System.Collections.Generic.IEnumerator<{{t}}> _e;
                private bool _closed;
                private bool _onRow;

                public {{reader}}(global::System.Collections.Generic.IEnumerable<{{t}}> source) => _e = source.GetEnumerator();

                public int FieldCount => {{model.Columns.Count}};

                public string GetName(int i) => i switch
                {
            {{nameArms}}        _ => throw OutOfRange(i),
                };

                public int GetOrdinal(string name) => name switch
                {
            {{ordinalArms}}        _ => throw NoColumn(name),
                };

                public global::System.Type GetFieldType(int i) => i switch
                {
            {{fieldTypeArms}}        _ => throw OutOfRange(i),
                };

                public object GetValue(int i)
                {
                    if (!_onRow)
                    {
                        throw new global::System.InvalidOperationException("No current row. Call Read() and ensure it returned true before reading values.");
                    }

                    return i switch
                    {
            {{valueArms}}            _ => throw OutOfRange(i),
                    };
                }

                public int GetValues(object[] values)
                {
                    if (values is null)
                    {
                        throw new global::System.ArgumentNullException(nameof(values));
                    }

                    int count = values.Length < FieldCount ? values.Length : FieldCount;
                    for (int i = 0; i < count; i++)
                    {
                        values[i] = GetValue(i);
                    }

                    return count;
                }

                public bool IsDBNull(int i) => GetValue(i) is global::System.DBNull;

                public object this[int i] => GetValue(i);

                public object this[string name] => GetValue(GetOrdinal(name));

                public bool GetBoolean(int i) => (bool)GetValue(i);
                public byte GetByte(int i) => (byte)GetValue(i);
                public char GetChar(int i) => (char)GetValue(i);
                public global::System.DateTime GetDateTime(int i) => (global::System.DateTime)GetValue(i);
                public decimal GetDecimal(int i) => (decimal)GetValue(i);
                public double GetDouble(int i) => (double)GetValue(i);
                public float GetFloat(int i) => (float)GetValue(i);
                public global::System.Guid GetGuid(int i) => (global::System.Guid)GetValue(i);
                public short GetInt16(int i) => (short)GetValue(i);
                public int GetInt32(int i) => (int)GetValue(i);
                public long GetInt64(int i) => (long)GetValue(i);
                public string GetString(int i) => (string)GetValue(i);
                public string GetDataTypeName(int i) => GetFieldType(i).Name;

                public int Depth => 0;
                public bool IsClosed => _closed;
                public int RecordsAffected => -1;

                public bool Read()
                {
                    if (_closed)
                    {
                        throw new global::System.InvalidOperationException("The data reader is closed.");
                    }

                    _onRow = _e.MoveNext();
                    return _onRow;
                }

                public bool NextResult() => false;

                public void Close()
                {
                    if (!_closed)
                    {
                        _closed = true;
                        _onRow = false;
                        _e.Dispose();
                    }
                }

                public void Dispose() => Close();

                public global::System.Data.DataTable? GetSchemaTable() => null;

                public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length)
                    => throw new global::System.NotSupportedException("GetBytes is not supported; expose the member as a typed column and use GetValue instead.");

                public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length)
                    => throw new global::System.NotSupportedException("GetChars is not supported; expose the member as a typed column and use GetValue instead.");

                public global::System.Data.IDataReader GetData(int i)
                    => throw new global::System.NotSupportedException("GetData (nested data readers) is not supported by the ObjectReader.");

                private static global::System.IndexOutOfRangeException OutOfRange(int i)
                    => new global::System.IndexOutOfRangeException($"Field index {i} is out of range.");

                private static global::System.IndexOutOfRangeException NoColumn(string name)
                    => new global::System.IndexOutOfRangeException($"Column '{name}' was not found.");
            }
            """;
    }

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
