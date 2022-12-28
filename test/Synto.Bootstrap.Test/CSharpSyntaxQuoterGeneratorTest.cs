using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Synto.Bootstrap.Test;

[UsesVerify]
public class CSharpSyntaxQuoterGeneratorTest
{
    [Fact]
    public Task VerifySnapshot()
    {
        var driver = GeneratorDriver();

        return Verify(driver);
    }

    static GeneratorDriver GeneratorDriver()
    {
        var syntaxTree = CSharpSyntaxTree.ParseText("""
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace Synto;
public partial class CSharpSyntaxQuoter : CSharpSyntaxVisitor<ExpressionSyntax> {
    // this is not supported by the generator
    public override ExpressionSyntax? VisitIdentifierName(IdentifierNameSyntax node) => base.VisitIdentifierName(node);
}

""");
        var outputPath = Path.GetDirectoryName(typeof(CSharpSyntaxVisitor<>).GetTypeInfo().Assembly.Location)!;
        var allFiles = Directory.EnumerateFiles(outputPath, "*.dll", SearchOption.TopDirectoryOnly);
        var compilation = CSharpCompilation.Create("Test",
            new[] {syntaxTree},
            allFiles.Select(file => MetadataReference.CreateFromFile(file))
        );
        var generator = new CSharpSyntaxQuoterGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        return driver.RunGenerators(compilation);
    }
}