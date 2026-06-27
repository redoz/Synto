using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Synto.Templating;
using Xunit;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;


#pragma warning disable CS8321

namespace Synto.Test;


public partial class RoundTripTests
{
    private static partial class Factory { };

    // Normalize line endings so the assertions are stable regardless of how the
    // source file was checked out (LF vs CRLF) or which platform the test runs on.
    private static void AssertGenerated(string expected, SyntaxNode node)
    {
        var actual = node.NormalizeWhitespace(eol: "\n").GetText(Encoding.UTF8).ToString().Trim();
        Assert.Equal(expected.Replace("\r\n", "\n", StringComparison.Ordinal), actual);
    }

    [Fact]
    public void Test0()
    {
        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Simple()
        {
            Console.WriteLine("Hello World");
        }

        StatementSyntax node = Factory.Simple();

        AssertGenerated("Console.WriteLine(\"Hello World\");", node);
    }

    [Fact]
    public void Test1()
    {
        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Hello([Unquote] string message)
        {
            Console.WriteLine("Hello " + message);
        }

        StatementSyntax node = Factory.Hello("World");

        AssertGenerated("Console.WriteLine(\"Hello \" + \"World\");", node);
    }

    [Fact]
    public void Test2()
    {
        // Syntax<T> composition: the expression produced by one template is spliced into a Syntax<string>
        // splice point (message()) of another, round-tripping to the composed call.
        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Inner()
        {
            new StringBuilder("Something").Append("Foo").ToString();
        }


        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Outer(Syntax<string> message)
        {
            Console.WriteLine("Hello " + message());
        }

        StatementSyntax node = Factory.Outer(Factory.Inner().Expression);

        AssertGenerated(
            "Console.WriteLine(\"Hello \" + new StringBuilder(\"Something\").Append(\"Foo\").ToString());",
            node);
    }

    [Fact]
    public void Test3()
    {
        // Bare-block repeater: a scalar Syntax splice point (item()) used twice in a Bare (multi-statement)
        // body, each replaced by the same spliced expression, round-tripping to the repeated block.
        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Greeting([Unquote] string message)
        {
            Console.WriteLine("Hello " + message);
        }


        [Template(typeof(Factory), Options = TemplateOption.Bare)]
        static void Repeater(Syntax item)
        {
            item();
            item();
        }

        BlockSyntax node = Factory.Repeater(Factory.Greeting("World").Expression);

        AssertGenerated("""
            {
                Console.WriteLine("Hello " + "World");
                Console.WriteLine("Hello " + "World");
            }
            """,
            node);
    }

    [Fact]
    public void Test4()
    {
        [Template(typeof(Factory), Options = TemplateOption.None)]
        static void FullMethod()
        {
            Console.WriteLine("Hello World");
        }

        StatementSyntax node = Factory.FullMethod();

        AssertGenerated("""
            static void FullMethod()
            {
                Console.WriteLine("Hello World");
            }
            """, node);
    }


    [Fact]
    public void Test5()
    {

        [Template(typeof(Factory), Options = TemplateOption.Bare)]
        static void Loop([Unquote] int count)
        {
            int ret = 0;
            for (int i = 0; i < count; i++)
            {
                ret++;
            }
        }

        BlockSyntax node = Factory.Loop(4);

        // An [Unquote] parameter is a STAGING root: the `for` driven by `count` unrolls at factory-build time
        // (count == 4) into four `ret++` statements, rather than being emitted as a literal-bounded loop. This
        // is the designed unquote/staged-control behavior (spec §3-4), distinct from a plain value lift that
        // would preserve the loop with a literal bound.
        string expected = """
                              {
                                  int ret = 0;
                                  ret++;
                                  ret++;
                                  ret++;
                                  ret++;
                              }
                              """;

        AssertGenerated(expected, node);
    }

    [Fact]
    public void InlinedGenericTypeArgument()
    {
        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Make<[Unquote] T>()
        {
            List<T> list = new List<T>();
        }

        StatementSyntax node = Factory.Make<List<int>>();

        // the inlined type argument is rendered fully-qualified (no using context exists at runtime);
        // before the fix this produced the unparseable CLR name "List`1[[System.Int32, ...]]".
        AssertGenerated(
            "List<System.Collections.Generic.List<System.Int32>> list = new List<System.Collections.Generic.List<System.Int32>>();",
            node);
    }

    [Fact]
    public void RuntimeConverter()
    {
        // End-to-end proof of [Runtime] converter pluggability: the generator (running on THIS project)
        // discovers RgbConverter from the inlined parameter's type and emits a direct call to it, so the
        // built factory actually converts the custom Rgb value to `new Rgb(255)` syntax at runtime.
        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Use([Unquote] Rgb color)
        {
            Console.WriteLine(color);
        }

        StatementSyntax node = Factory.Use(new Rgb(255));

        AssertGenerated("Console.WriteLine(new Rgb(255));", node);
    }

}

/// <summary>A user type that has no built-in value-to-syntax conversion.</summary>
public readonly struct Rgb
{
    public Rgb(int value) => Value = value;

    public int Value { get; }
}

/// <summary>A user-authored [Runtime] converter for <see cref="Rgb"/>, discovered and called by the generator.</summary>
[Runtime]
internal static class RgbConverter
{
    public static ExpressionSyntax ToSyntax(this Rgb rgb) =>
        ObjectCreationExpression(IdentifierName("Rgb"))
            .AddArgumentListArguments(
                Argument(LiteralExpression(NumericLiteralExpression, Literal(rgb.Value))));
}
