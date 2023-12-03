using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Synto.Templating;

namespace Synto.Test.Templating;

[UsesVerify]
public class SimpleTemplateTest
{
    private static readonly MetadataReference CorlibReference = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
    private static readonly MetadataReference NetStandardReference = MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location);
    private static readonly MetadataReference SystemRuntimeReference = MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=8.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a").Location);
    

    static Compilation CreateCompilation()
    {
        

        return CSharpCompilation.Create("Test",
            Array.Empty<SyntaxTree>(),
            references:
            [
                CorlibReference,
                NetStandardReference,
                SystemRuntimeReference,
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(TemplateAttribute).Assembly.Location)
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
    }

    static GeneratorDriver CreateDriver()
    {
        var generator = new TemplateFactorySourceGenerator();
        return CSharpGeneratorDriver.Create(generator);
    }

    private readonly Compilation _baseCompilation;
    private readonly GeneratorDriver _driver;

    public SimpleTemplateTest()
    {
        // probably shift this into a class fixture for perf
        _baseCompilation = CreateCompilation();
        _driver = CreateDriver();
    }

    private Compilation CompilationWithSource(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        Assert.Empty(syntaxTree.GetDiagnostics());

        var compilation = _baseCompilation.AddSyntaxTrees(syntaxTree);
        //var compilation = _baseCompilation.Add
        return compilation;
    }

    private async Task VerifyTemplate(string source)
    {
        var compilation = CompilationWithSource(source);
        Assert.Empty(compilation.GetDiagnostics().Where(diag => diag.Severity == DiagnosticSeverity.Error));
        var result = _driver.RunGenerators(compilation);
        //var diagnostics = result.GetRunResult();
        //Assert.Empty(diagnostics.Diagnostics);
        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    [Fact]
    public async Task LocalFunctionAsSingle()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Single)]
                    void LocalFunction() {
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }

    [Fact]
    public async Task LocalFunctionAsBare()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Bare)]
                    void LocalFunction() {
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }

    [Fact]
    public async Task LocalFunctionAsDefault()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Default)]
                    void LocalFunction() {
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }

    [Fact]
    public async Task StaticLocalFunctionAsSingle()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Single)]
                    static void LocalFunction() {
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }

    [Fact]
    public async Task StaticLocalFunctionAsBare()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Bare)]
                    static void LocalFunction() {
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }

    [Fact]
    public async Task StaticLocalFunctionAsDefault()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Default)]
                    static void LocalFunction() {
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }


    [Fact]
    public async Task FunctionWithMultipleStatementAsSingle()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto;

            partial class Factory {}

            public class TestClass {
                public void TestMethod() {
                    [Template(typeof(Factory), Options = TemplateOption.Single)]
                    void LocalFunction() {
                        Console.WriteLine("Hello world");
                        Console.WriteLine("Hello world");
                    }
                }
            }
            """
        );
    }

    [Fact]
    public async Task FunctionAsSingle()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto;

            partial class Factory {}

            public class TestClass {
                [Template(typeof(Factory), Options = TemplateOption.Single)]
                void LocalFunction() {
                    Console.WriteLine("Hello world");
                }
            }
            """
        );
    }


    [Fact]
    public async Task ClassTemplate()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto;

            partial class Factory {}

            [TemplateAttribute(typeof(Factory))]
            public class TestClass {
                void LocalFunction() {
                    Console.WriteLine("Hello world");
                }
            }
            """
        );
    }


    [Fact]
    public async Task InlineGenericType()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto;

            partial class Factory {}


            public class TestClass {
               [Template(typeof(Factory))]
                void LocalFunction<T>([Inline]T value) {
                    Console.WriteLine($"Hello world {value}");
                }
            }
            """
        );
    }

    [Fact]
    public async Task InlineGenericTypeAsSyntax()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto;

            partial class Factory {}


            public class TestClass {
               [Template(typeof(Factory))]
                void LocalFunction<T>([Inline(AsSyntax = true)]T value) {
                    Console.WriteLine($"Hello world {value}");
                }
            }
            """
        );
    }


    [Fact]
    public async Task WithSyntaxOfString()
    {
        await VerifyTemplate(
            """
            using System;
            using Synto;

            partial class Factory {}


            public class TestClass {
               [Template(typeof(Factory))]
                void LocalFunction(Syntax<string> value) {
                    Console.WriteLine($"Hello world" + value());
                }
            }
            """
        );
    }


}
