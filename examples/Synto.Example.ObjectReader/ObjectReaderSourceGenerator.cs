using System.Collections.Immutable;
using System.Data;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Formatting;

namespace Synto.Example.ObjectReader;

internal readonly struct SourceGeneratorTarget
{
    private SourceGeneratorTarget(SemanticModel semanticModel, InvocationExpressionSyntax invocationSyntax, INamedTypeSymbol targetType)
    {
        SemanticModel = semanticModel;
        InvocationSyntax = invocationSyntax;
        TargetType = targetType;
    }

    public SemanticModel SemanticModel { get; }
    public InvocationExpressionSyntax InvocationSyntax { get; }
    public INamedTypeSymbol TargetType { get; }

    public static SourceGeneratorTarget? Create(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var objectReaderTypeSymbol = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(ObjectReader).FullName!);
        Debug.Assert(objectReaderTypeSymbol is not null);

        InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;

        // TODO this probably won't work if this was pulled in with a `using static`
        MemberAccessExpressionSyntax memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;

        var expressionSymbol = context.SemanticModel.GetSymbolInfo(memberAccess.Expression);

        if (expressionSymbol.Symbol is not INamedTypeSymbol typeSymbol || !SymbolEqualityComparer.Default.Equals(objectReaderTypeSymbol, typeSymbol))
            return null;

        var createSymbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess.Name);

        if (createSymbolInfo.Symbol is not IMethodSymbol { TypeArguments: { Length: 1 } typeArgs } || typeArgs[0] is not INamedTypeSymbol targetType)
            return null;

        if (invocation.ArgumentList.Arguments.Count < 2)
            return null;

        return new SourceGeneratorTarget(context.SemanticModel, invocation, targetType);
    }
}

[Generator(LanguageNames.CSharp)]
public class ObjectReaderSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        static bool Predicate(SyntaxNode node, CancellationToken _) =>
            node is InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name: GenericNameSyntax
                    {
                        Identifier.ValueText: "Create"
                    }
                    or IdentifierNameSyntax
                    {
                        Identifier.ValueText: "Create"
                    }
                },
                ArgumentList.Arguments.Count: >= 2
            };

        var syntaxProvider = context.SyntaxProvider.CreateSyntaxProvider(
                Predicate,
                static (ctx, cancellationToken) => SourceGeneratorTarget.Create(ctx, cancellationToken))
            .Where(target => target.HasValue)
            .Select((target, _) => target!.Value);


        context.RegisterSourceOutput(syntaxProvider.Collect(), Execute);
    }



    private static void Execute(SourceProductionContext context, ImmutableArray<SourceGeneratorTarget> targets)
    {
        
        var compilationUnit = SyntaxFactory.CompilationUnit();

        var compilerServicesNamespace = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName("System.Runtime.CompilerServices"))
            .AddMembers(
                Factory.InterceptsLocationAttribute()
                    .WithModifiers(
                        SyntaxFactory.TokenList(
                            SyntaxFactory.Token(
                                SyntaxKind.FileKeyword))));

        compilationUnit = compilationUnit.AddMembers(compilerServicesNamespace);


        var generatedNamespace = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName("Synto.Example.ObjectReader.Generated"));

        var classDecl = SyntaxFactory.ClassDeclaration(SyntaxFactory.Identifier("ObjectReaderFactory")).WithModifiers(
            SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.InternalKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)));

        int n = 0;
        foreach (var target in targets)
        {
            var output = CreateDataReader(target, n++);
            if (output is ({ } factory, { } impl))
            {
                classDecl = classDecl.AddMembers(factory);
                generatedNamespace = generatedNamespace.AddMembers(impl);
            }
        }

        generatedNamespace = generatedNamespace.AddMembers(classDecl);

        compilationUnit = compilationUnit.AddMembers(generatedNamespace);



        var sourceText = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace()).GetText(Encoding.UTF8);

        context.AddSource("ObjectReader.g.cs", sourceText);
    }

    private static (MethodDeclarationSyntax Factory, ClassDeclarationSyntax Implementation)? CreateDataReader(SourceGeneratorTarget target, int count)
    {
        List<string> properties = [];

        var invocation = target.InvocationSyntax;
        for (int i = 1; i < invocation.ArgumentList.Arguments.Count; i++)
        {
            var value = target.SemanticModel.GetConstantValue(invocation.ArgumentList.Arguments[i].Expression);
            if (!value.HasValue)
            {
                // TODO add diagnostic for this
                return null;
            }

            if (value.Value is string propertyName)
            {
                var members = target.TargetType.GetMembers(propertyName);

                if (members.Length == 0)
                {
                    // TODO add diagnostic for this
                    return null;
                }

                if (members.Length > 1)
                {
                    // TODO add diagnostic for this
                    return null;
                }

                properties.Add(propertyName);
            }
            else if (value.Value is string[] propertyNames)
            {
                properties.AddRange(propertyNames);
            }
            else
            {
                // TODO add diagnostic for this
                return null;
            }
        }

        var readerTypeName = SyntaxFactory.Identifier($"ObjectReader{count}");


        var method = Factory.Create(
                target.TargetType.GetQualifiedNameSyntax(),
                SyntaxFactory.ObjectCreationExpression(
                    SyntaxFactory.IdentifierName(readerTypeName),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(
                                SyntaxFactory.IdentifierName("data")))),
                    null))
            .WithIdentifier(SyntaxFactory.Identifier($"Create{count}"))
            .AddAttributeLists(
                    InterceptorAttribute(
                        target.SemanticModel.Compilation, 
                        target.InvocationSyntax));



        var type = Factory.ObjectReaderTemplate(target.TargetType.GetQualifiedNameSyntax())
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.FileKeyword));
         
        //int i;
        //return i switch
        //{
        //    0 => _enumerator.Current
        //}

        return (method, type);
    }

    private static AttributeListSyntax InterceptorAttribute(Compilation compilation, InvocationExpressionSyntax invocationSyntax)
    {
        string filePath = compilation.Options.SourceReferenceResolver?.NormalizePath(invocationSyntax.SyntaxTree.FilePath, baseFilePath: null) ?? invocationSyntax.SyntaxTree.FilePath;
        var position = invocationSyntax.GetLocation().GetLineSpan().StartLinePosition;
        var character = position.Character;
        var line = position.Line;

        return SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(
                    (NameSyntax) SyntaxFactory.ParseTypeName("System.Runtime.CompilerServices.InterceptsLocation"),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SeparatedList(
                            [
                                SyntaxFactory.AttributeArgument(filePath.ToSyntax()),
                                SyntaxFactory.AttributeArgument(line.ToSyntax()),
                                SyntaxFactory.AttributeArgument(character.ToSyntax())
                            ]
                        )
                    )
                )
            )
        );
    }

    [Template(typeof(Factory))]
    public static IDataReader Create<[Inline(AsSyntax = true)]T>(
        Syntax<IDataReader> factoryExpr, 
        IEnumerable<T> data,
        params string[] properties)
    {
        return factoryExpr();
    }


    [Template(typeof(Factory))]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    private sealed class InterceptsLocationAttribute(string filePath, int line, int character) : Attribute
    {
    }
}

[Template(typeof(Factory))]
internal abstract partial class ObjectReaderTemplate<[Inline(AsSyntax = true)] T> : IDataReader
{
    private readonly IEnumerable<T> _data;
    private readonly IEnumerator<T> _enumerator;
    private bool _canRead;
    private bool _isClosed;

    protected ObjectReaderTemplate(IEnumerable<T> data)
    {
        _data = data;
        _enumerator = _data.GetEnumerator();
        _isClosed = false;
    }

    public bool GetBoolean(int i)
    {
        throw new NotImplementedException();
    }

    public byte GetByte(int i)
    {
        throw new NotImplementedException();
    }

    public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length)
    {
        throw new NotImplementedException();
    }

    public char GetChar(int i)
    {
        throw new NotImplementedException();
    }

    public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length)
    {
        throw new NotImplementedException();
    }

    public IDataReader GetData(int i)
    {
        throw new NotImplementedException();
    }

    public string GetDataTypeName(int i)
    {
        throw new NotImplementedException();
    }

    public DateTime GetDateTime(int i)
    {
        throw new NotImplementedException();
    }

    public decimal GetDecimal(int i)
    {
        throw new NotImplementedException();
    }

    public double GetDouble(int i)
    {
        throw new NotImplementedException();
    }

    public Type GetFieldType(int i)
    {
        throw new NotImplementedException();
    }

    public float GetFloat(int i)
    {
        throw new NotImplementedException();
    }

    public Guid GetGuid(int i)
    {
        throw new NotImplementedException();
    }

    public short GetInt16(int i)
    {
        throw new NotImplementedException();
    }

    public int GetInt32(int i)
    {
        throw new NotImplementedException();
    }

    public long GetInt64(int i)
    {
        throw new NotImplementedException();
    }

    public string GetName(int i)
    {
        throw new NotImplementedException();
    }

    public int GetOrdinal(string name)
    {
        throw new NotImplementedException();
    }

    public virtual string GetString(int i)
    {
        throw new NotImplementedException();
    }

    public virtual object GetValue(int i)
    {
        throw new NotImplementedException();
    }

    public virtual int GetValues(object[] values)
    {
        throw new NotImplementedException();
    }

    public virtual bool IsDBNull(int i)
    {
        throw new NotImplementedException();
    }

    public virtual int FieldCount { get; }

    public virtual object this[int i] => throw new NotImplementedException();

    public virtual object this[string name] => throw new NotImplementedException();

    public void Dispose()
    {
        _enumerator.Dispose();
    }

    public void Close()
    {
        if (_isClosed)
            throw new InvalidOperationException("Reader already closed");

        _isClosed = true;
        _canRead = false;
    }

    public DataTable? GetSchemaTable()
    {
        throw new NotImplementedException();
    }

    public bool NextResult()
    {
        Close();
        return false;
    }

    public bool Read()
    {
        if (!_canRead)
            throw new InvalidOperationException("Cannot read past end of enumerator.");

        return _canRead = _enumerator.MoveNext();
    }

    // TODO or zero?
    public int Depth => 1;

    public bool IsClosed => _isClosed;

    public int RecordsAffected => -1;
}

internal partial class Factory
{

}