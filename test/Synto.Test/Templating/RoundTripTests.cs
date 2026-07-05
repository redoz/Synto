using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Synto.Templating;
using Xunit;
using static Synto.Templating.Template;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;


#pragma warning disable CS8321

namespace Synto.Test.Templating;


public partial class RoundTripTests
{
    private abstract partial class Factory { };

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
        [Template<Factory>(Options = TemplateOption.Single)]
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
        [Template<Factory>(Options = TemplateOption.Single)]
        static void Hello([Unquote] string message)
        {
            Console.WriteLine("Hello " + message);
        }

        StatementSyntax node = Factory.Hello("World");

        AssertGenerated("Console.WriteLine(\"Hello \" + \"World\");", node);
    }

    [Fact]
    public void InterpolationFold()
    {
        // The interpolation staged-fold (spec 2026-06-28): a BARE staged-string hole in a regular $"…" string
        // is baked into the surrounding literal text at factory-build time via label.ToInterpolatedText(),
        // instead of being re-emitted as a runtime hole. This is the round-trip counterpart to
        // InterpolationFoldTest's snapshot coverage — it compiles and EXECUTES the folded factory and renders
        // the result back, so a stable-but-wrong escaping/fusion regression fails here even though the snapshot
        // would still match. The value "a{b}\"" carries every brace/quote escape hazard: ToInterpolatedText
        // must double the braces ({{ }}) and escape the quote (\") so the fused literal text is well-formed.
        [Template<Factory>(Options = TemplateOption.Single)]
        static void Interp([Unquote] string label)
        {
            Console.WriteLine($"Field {label} column");
        }

        StatementSyntax node = Factory.Interp("a{b}\"");

        AssertGenerated("Console.WriteLine($\"Field a{{b}}\\\" column\");", node);
    }

    [Fact]
    public void Test2()
    {
        // Syntax<T> composition: the expression produced by one template is spliced into a Syntax<string>
        // splice point (message()) of another, round-tripping to the composed call.
        [Template<Factory>(Options = TemplateOption.Single)]
        static void Inner()
        {
            new StringBuilder("Something").Append("Foo").ToString();
        }


        [Template<Factory>(Options = TemplateOption.Single)]
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
        [Template<Factory>(Options = TemplateOption.Single)]
        static void Greeting([Unquote] string message)
        {
            Console.WriteLine("Hello " + message);
        }


        [Template<Factory>(Options = TemplateOption.Bare)]
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
        [Template<Factory>(Options = TemplateOption.None)]
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

        [Template<Factory>(Options = TemplateOption.Bare)]
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
    public void QuoteLoop()
    {

        [Template<Factory>(Options = TemplateOption.Bare)]
        static void QuoteLoop([Quote] int count)
        {
            int ret = 0;
            for (int i = 0; i < count; i++)
            {
                ret++;
            }
        }

        BlockSyntax node = Factory.QuoteLoop(4);

        // Deliberate counterpoint to Test5 (the [Unquote] Loop that unrolls): a [Quote] parameter is a value
        // lift but NEVER a staging root (spec §3), so the `for` driven by the quoted `count` stays a real,
        // literal-bounded runtime loop (`for (int i = 0; i < 4; i++)`) instead of unrolling into four `ret++`.
        string expected = """
                              {
                                  int ret = 0;
                                  for (int i = 0; i < 4; i++)
                                  {
                                      ret++;
                                  }
                              }
                              """;

        AssertGenerated(expected, node);
    }

    [Fact]
    public void InlinedGenericTypeArgument()
    {
        [Template<Factory>(Options = TemplateOption.Single)]
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
        [Template<Factory>(Options = TemplateOption.Single)]
        static void Use([Unquote] Rgb color)
        {
            Console.WriteLine(color);
        }

        StatementSyntax node = Factory.Use(new Rgb(255));

        AssertGenerated("Console.WriteLine(new Rgb(255));", node);
    }

    // A [Splice] MEMBER generator (spec §2–§3): the static `Accessors` method runs at factory-build time and its
    // `foreach` over the lifted `Parameter<IReadOnlyList<Col>>()` produces ONE accessor MEMBER per column, spliced
    // into the produced `Reader` type's member list. Contrast the statement-level unroll (Test5's [Unquote] `for`
    // that emits four `ret++` *statements* into a block): here the live loop yields *members* of the type, not
    // statements of a body. The generator method itself is trimmed from the produced type.
    // Reader/Counter are [Template<Factory>] carriers: Synto quotes them and the `public class …`
    // shape below is the exact round-trip contract asserted in the tests. The carrier-shape analyzer
    // rules are therefore false positives here and are disabled through the end of Counter.
#pragma warning disable CA1515 // public type could be internal
#pragma warning disable CA1034 // nested type is externally visible
#pragma warning disable CA1852 // type can be sealed
#pragma warning disable CA1812 // apparently never instantiated (invoked only via generated Factory.*)
    [Template<Factory>]
    public class Reader
    {
        [Splice]
        static IEnumerable<MemberDeclarationSyntax> Accessors()
        {
            var columns = Parameter<IReadOnlyList<Col>>();
            foreach (var c in columns)
                yield return MethodDeclaration(PredefinedType(Token(IntKeyword)), Identifier("Get" + c.Name))
                    .AddModifiers(Token(PublicKeyword))
                    .WithExpressionBody(ArrowExpressionClause(
                        LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal))))
                    .WithSemicolonToken(Token(SemicolonToken));
        }
    }

    [Fact]
    public void SpliceMembers()
    {
        IReadOnlyList<Col> cols = new[] { new Col(0, "Id"), new Col(1, "Name") };

        ClassDeclarationSyntax node = Factory.Reader(cols);

        string expected = """
                          public class Reader
                          {
                              public int GetId() => 0;
                              public int GetName() => 1;
                          }
                          """;

        AssertGenerated(expected, node);
    }

    // The SINGLE-return [Splice] member shape (spec §2–§3): the static `Count` method returns one
    // `MemberDeclarationSyntax` directly (not an `IEnumerable<>`), so the factory emits it as a bare
    // `BuildList<MemberDeclarationSyntax>(call)` arg relying on the `MemberDeclarationSyntax -> ListSegment`
    // implicit conversion — a DISTINCT emission branch from the enumerable `ListSegment.Run(...)` shape in
    // SpliceMembers above. The lifted `Parameter<int>()` folds into the factory's `count` argument.
    [Template<Factory>]
    public class Counter
    {
        [Splice]
        static MethodDeclarationSyntax Count()
        {
            var count = Parameter<int>();
            return MethodDeclaration(PredefinedType(Token(IntKeyword)), Identifier("Count"))
                .AddModifiers(Token(PublicKeyword))
                .WithExpressionBody(ArrowExpressionClause(
                    LiteralExpression(NumericLiteralExpression, Literal(count))))
                .WithSemicolonToken(Token(SemicolonToken));
        }
    }
#pragma warning restore CA1812
#pragma warning restore CA1852
#pragma warning restore CA1034
#pragma warning restore CA1515

    [Fact]
    public void SpliceSingleMember()
    {
        ClassDeclarationSyntax node = Factory.Counter(7);

        string expected = """
                          public class Counter
                          {
                              public int Count() => 7;
                          }
                          """;

        AssertGenerated(expected, node);
    }

}

/// <summary>A fixed column descriptor for the <c>[Splice]</c> member-generator round-trip.</summary>
internal readonly record struct Col(int Ordinal, string Name);

/// <summary>A user type that has no built-in value-to-syntax conversion.</summary>
internal readonly record struct Rgb(int Value);

/// <summary>A user-authored [Runtime] converter for <see cref="Rgb"/>, discovered and called by the generator.</summary>
[Runtime]
internal static class RgbConverter
{
    public static ExpressionSyntax ToSyntax(this Rgb rgb) =>
        ObjectCreationExpression(IdentifierName("Rgb"))
            .AddArgumentListArguments(
                Argument(LiteralExpression(NumericLiteralExpression, Literal(rgb.Value))));
}
