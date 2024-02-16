using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Diagnostics.Test;

public class DiagnosticsGeneratorTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);

    [Fact]
    public Task WithFileScopedNamespace()
    {
        var driver = GeneratorDriver(
            """
            using Microsoft.CodeAnalysis;
            using Synto.Diagnostics;
            
            namespace X.Y.Z;

            internal static partial class Diagnostics {
                private const string IdPrefix = "TST";
                
                [Diagnostic(IdPrefix + "0000", "Internal Error", "Unhandled exception {0} was thrown: {1}", "Synto.Internal", DiagnosticSeverity.Error, true)]
                public static partial Diagnostic InternalError(Location location, string exceptionType, string exceptionMessage);
            }

            """);

        return Verify(driver).UseDirectory("snapshots");
    }

    [Fact]
    public Task WithBlockScopedNamespaces()
    {
        var driver = GeneratorDriver(
            """
            using Microsoft.CodeAnalysis;
            using Synto.Diagnostics;

            namespace X.Y {
                namespace Z {
                    internal static partial class Diagnostics {
                        private const string IdPrefix = "TST";
                        
                        [Diagnostic(IdPrefix + "0000", "Internal Error", "Unhandled exception {0} was thrown: {1}", "Synto.Internal", DiagnosticSeverity.Error, true)]
                        public static partial Diagnostic InternalError(Location location, string exceptionType, string exceptionMessage);
                    }
                }
            }
            """);

        return Verify(driver).UseDirectory("snapshots");
    }

    static GeneratorDriver GeneratorDriver(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var outputPath = Path.GetDirectoryName(typeof(CSharpSyntaxVisitor<>).GetTypeInfo().Assembly.Location)!;
        var allFiles = Directory.EnumerateFiles(outputPath, "*.dll", SearchOption.TopDirectoryOnly);
        var compilation = CSharpCompilation.Create("Test",
            new[] {syntaxTree},
            allFiles.Select(file => MetadataReference.CreateFromFile(file)).Union(
                [
                    CorlibReference,
                    NetStandardReference,
                    SystemRuntimeReference,
                    MetadataReference.CreateFromFile(typeof(TemplateAttribute).Assembly.Location)
                ]
            )
        );

        var generator = new DiagnosticsGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        return driver.RunGenerators(compilation);
    }
}