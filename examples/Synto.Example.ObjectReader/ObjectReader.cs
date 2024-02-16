using System.Collections;
using System.Data;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Synto.Formatting;

namespace Synto.Example.ObjectReader;

public static class ObjectReader
{
    public static IDataReader Create<T>(IEnumerable<T> data, params string[] properties)
    {
        throw new NotImplementedException();
    }
}



internal partial class TemplateFactory
{
}





//internal class ObjectReaderCreateExpression(InvocationExpressionSyntax invocation, ITypeSymbol targetType)
//{
//    public InvocationExpressionSyntax Invocation { get; } = invocation;
//    public ITypeSymbol TargetType { get; } = targetType;
//}

//internal class ObjectReaderSyntaxReceiver : ISyntaxContextReceiver, IReadOnlyList<ObjectReaderCreateExpression>
//{
//    private readonly List<ObjectReaderCreateExpression> _matches;

//    public ObjectReaderSyntaxReceiver()
//    {
//        _matches = new List<ObjectReaderCreateExpression>();
//    }

//    public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
//    {
//        var objectReaderType = context.SemanticModel.Compilation.GetTypeByMetadataName(typeof(ObjectReader).FullName!);
//        Debug.Assert(objectReaderType is not null);
        

//        if (context.Node is InvocationExpressionSyntax {Expression: MemberAccessExpressionSyntax memberAccess, ArgumentList.Arguments.Count: > 1} invocation
//            && SymbolEqualityComparer.Default.Equals(objectReaderType, ModelExtensions.GetSymbolInfo(context.SemanticModel, memberAccess.Expression).Symbol))
//        {
//            var methodSymbolInfo = context.SemanticModel.GetSymbolInfo(invocation) ;

//            var method = methodSymbolInfo.Symbol as IMethodSymbol;

//            Debug.Assert(method is not null);

//            var targetType = method.TypeArguments.Single();



//            _matches.Add(new ObjectReaderCreateExpression(invocation, targetType));

//            // Debugger.Launch();

//            //if (memberAccess.Name is GenericNameSyntax { Identifier.ValueText: nameof(ObjectReader.Create), TypeArgumentList.Arguments: { Count: 1 } argList })
//            //{
//            //    var targetType = context.SemanticModel.GetTypeInfo(argList[0]);

//            //    _matches.Add(new ObjectReaderCreateExpression(invocation, targetType));
//            //}
//            //else if (memberAccess.Name is IdentifierNameSyntax {Identifier.ValueText: nameof(ObjectReader.Create)})
//            //{
//            //    // Debugger.Launch();
//            //    var targetType = context.SemanticModel.GetTypeInfo(invocation.ArgumentList.Arguments[0]);

//            //    _matches.Add(new ObjectReaderCreateExpression(invocation, targetType));
//            //}
//        }
//    }

//    public IEnumerator<ObjectReaderCreateExpression> GetEnumerator()
//    {
//        return _matches.GetEnumerator();
//    }

//    IEnumerator IEnumerable.GetEnumerator()
//    {
//        return ((IEnumerable) _matches).GetEnumerator();
//    }

//    public int Count => _matches.Count;

//    public ObjectReaderCreateExpression this[int index] => _matches[index];
//}

//[Generator]
//public class ObjectReaderGenerator : ISourceGenerator {
//    public void Initialize(GeneratorInitializationContext context)
//    {
//        context.RegisterForSyntaxNotifications(() => new ObjectReaderSyntaxReceiver());
//    }


//    public void Execute(GeneratorExecutionContext context)
//    {
//        if (context.SyntaxContextReceiver is not ObjectReaderSyntaxReceiver syntaxReceiver)
//            return;

//        foreach (var createExpr in syntaxReceiver)
//        {
//            try
//            {
//                var compilationUnit = GenerateObjectReader(context, createExpr);

//                //compilationUnit = compilationUnit.AddMembers(TemplateFactory.ObjectReader());

//                var sourceText = SyntaxFormatter.Format(compilationUnit.NormalizeWhitespace()).GetText(Encoding.UTF8);

//                // TODO fix the source file name generated, it should be unique
//                context.AddSource($"{createExpr.TargetType}.cs", sourceText);
//            }
//            catch (Exception ex)
//            {
//                context.ReportDiagnostic(Diagnostics.InternalError(ex));
//            }
//        }

//    }

//    private CompilationUnitSyntax GenerateObjectReader(GeneratorExecutionContext context, ObjectReaderCreateExpression objectReaderCreateExpression)
//    {
//        var semanticModel = context.Compilation.GetSemanticModel(objectReaderCreateExpression.Invocation.SyntaxTree);
        
//        var targetType = objectReaderCreateExpression.TargetType;

//        Debug.Assert(targetType is not null);
//        var cleanName = targetType.MetadataName.Replace('`', '_');
//        var baseType = SyntaxFactory.GenericName(SyntaxFactory.Identifier(nameof(ObjectReader)), SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList<TypeSyntax>(targetType.GetQualifiedNameSyntax())));
//        var classDecl = SyntaxFactory.ClassDeclaration(SyntaxFactory.Identifier($"ObjectReaderFor{cleanName}"))
//            .WithBaseList(SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(SyntaxFactory.SimpleBaseType(baseType))));



//        return SyntaxFactory.CompilationUnit()
//            .AddMembers(classDecl);
//    }
//}