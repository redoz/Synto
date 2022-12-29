using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Synto.Templating;

namespace Synto.Test.Templating;

[UsesVerify]
public class SimpleTemplateTest
{
    static Compilation CreateCompilation()
    {
        return CSharpCompilation.Create("Test",
            Array.Empty<SyntaxTree>(),
            references: new[]
            {
                //MetadataReference.CreateFromFile(typeof(CSharpSyntaxNode).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(TemplateAttribute).Assembly.Location),
            }
        );
    }

    static GeneratorDriver CreateDriver()
    {
        var generator = new TemplateFactoryGenerator();
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
        return compilation;
    }

    private async Task VerifyTemplate(string source)
    {
        var compilation = CompilationWithSource(source);
        var result = _driver.RunGenerators(compilation);
        //var diagnostics = result.GetRunResult();
        //Assert.Empty(diagnostics.Diagnostics);
        var ret = await Verify(result).UseDirectory("snapshots");
        Assert.NotEmpty(ret.Files);
    }

    [Fact]
    public async Task LocalFunctionAsSingle()
    {
        await VerifyTemplate("""
using System;
using Synto;
using Synto.Templating;

partial class Factory {}

public class TestClass {
    public void TestMethod() {
        [Template(typeof(Factory), Options = TemplateOption.Single)]
        void LocalFunction() {
            Console.WriteLine("Hello world");
        }
    }
}
""");
    }

    [Fact]
    public async Task LocalFunctionAsBare()
    {
        await VerifyTemplate("""
using System;
using Synto;
using Synto.Templating;

partial class Factory {}

public class TestClass {
    public void TestMethod() {
        [Template(typeof(Factory), Options = TemplateOption.Bare)]
        void LocalFunction() {
            Console.WriteLine("Hello world");
        }
    }
}
""");
    }

    [Fact]
    public async Task LocalFunctionAsDefault()
    {
        await VerifyTemplate("""
using System;
using Synto;
using Synto.Templating;

partial class Factory {}

public class TestClass {
    public void TestMethod() {
        [Template(typeof(Factory), Options = TemplateOption.Default)]
        void LocalFunction() {
            Console.WriteLine("Hello world");
        }
    }
}
""");
    }

    [Fact]
    public async Task StaticLocalFunctionAsSingle()
    {
        await VerifyTemplate("""
using System;
using Synto;
using Synto.Templating;

partial class Factory {}

public class TestClass {
    public void TestMethod() {
        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void LocalFunction() {
            Console.WriteLine("Hello world");
        }
    }
}
""");
    }

    [Fact]
    public async Task StaticLocalFunctionAsBare()
    {
        await VerifyTemplate("""
using System;
using Synto;
using Synto.Templating;

partial class Factory {}

public class TestClass {
    public void TestMethod() {
        [Template(typeof(Factory), Options = TemplateOption.Bare)]
        static void LocalFunction() {
            Console.WriteLine("Hello world");
        }
    }
}
""");
    }

    [Fact]
    public async Task StaticLocalFunctionAsDefault()
    {
        await VerifyTemplate("""
using System;
using Synto;
using Synto.Templating;

partial class Factory {}

public class TestClass {
    public void TestMethod() {
        [Template(typeof(Factory), Options = TemplateOption.Default)]
        static void LocalFunction() {
            Console.WriteLine("Hello world");
        }
    }
}
""");
    }


    [Fact]
    public async Task FunctionWithMultipleStatementAsSingle()
    {
        await VerifyTemplate("""
using System;
using Synto;
using Synto.Templating;

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
""");
    }

    [Fact]
    public async Task FunctionAsSingle()
    {
        await VerifyTemplate("""
using System;
using Synto;
using Synto.Templating;

partial class Factory {}

public class TestClass {
    [Template(typeof(Factory), Options = TemplateOption.Single)]
    void LocalFunction() {
        Console.WriteLine("Hello world");
    } 
}
""");
    }
}
