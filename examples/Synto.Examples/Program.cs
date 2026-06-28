// See https://aka.ms/new-console-template for more information

using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto.Templating;
using Spectre.Console;
using static Examples;
using static Synto.Templating.Template;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;


for (; ; )
{
    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<Choice>()
            .Title("Select an example")
            .PageSize(10)
            .MoreChoicesText("[grey](Move up and down to reveal more examples)[/]")
            .AddChoices(new[]
            {
                new Choice("Extract a single statement from a method", Test0),
                new Choice("Use parameters to inject literals into syntax trees", Test1),
                new Choice("Use syntax placeholders to compose syntax trees", Test2),
                new Choice("Expand a syntax placeholder multiple times within a template", Test3),
                new Choice("Create a syntax tree representing the whole template method", Test4),
                new Choice("Use an [Unquote] parameter to unroll a for-loop", Test5),
                new Choice("Use a [Quote] parameter to keep a literal-bounded runtime loop", TestQuoteLoop),
                new Choice("Inline type parameters", Test6),
                new Choice("Inline type parameters in class declaration", Test7),
                new Choice("Use a [Splice] member generator to emit one accessor per column", TestSplice),

                new Choice("Exit", () =>
                {
                    Environment.Exit(0);
                    return "";
                })
            }));


    AnsiConsole.Write(new Rule(choice.Name).LeftJustified());
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine(choice.Example());
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule());
    AnsiConsole.WriteLine();

}

sealed record Choice(string Name, Func<string> Example)
{
    public override string ToString() => Name;
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1052:Static holder types should be Static or NotInheritable", Justification = "This is just an example")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1050:Declare types in namespaces", Justification = "This is just an example")]
internal class Examples
{
#pragma warning disable CS8321 // Local function is declared but never used
    public static string Test0()
    {
        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Simple()
        {
            Console.WriteLine("Hello World");
        }

        StatementSyntax node = Factory.Simple();

        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        return source;
    }


    public static string Test1()
    {
        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Hello([Unquote] string message)
        {
            Console.WriteLine("Hello " + message);
        }

        StatementSyntax node = Factory.Hello("World");

        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        return source;
    }


    public static string Test2()
    {
        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Inner()
        {
            new StringBuilder("Something").Append("Foo").ToString();
        }


        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Outer([Splice] string message)
        {
            Console.WriteLine("Hello " + message);
        }


        StatementSyntax node = Factory.Outer(Factory.Inner().Expression);

        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        return source;
    }

    public static string Test3()
    {

        [Template(typeof(Factory), Options = TemplateOption.Single)]

        static void Greeting([Unquote] string message)
        {
            Console.WriteLine("Hello " + message);
        }


        [Template(typeof(Factory), Options = TemplateOption.Bare)]
        static void Repeater(Syntax statement)
        {
            statement();
            statement();
        }

        BlockSyntax node = Factory.Repeater(Factory.Greeting("World").Expression);

        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        return source;
    }


    public static string Test4()
    {
        [Template(typeof(Factory), Options = TemplateOption.None)]
        static void FullMethod()
        {
            Console.WriteLine("Hello World");
        }

        StatementSyntax node = Factory.FullMethod();

        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        return source;
    }



    public static string Test5()
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


        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        return source;
    }

    public static string TestQuoteLoop()
    {
        // Sibling of Test5: same `for` driven by `count`, but `count` is a [Quote] parameter — a value lift that
        // is NEVER a staging root (spec §3). So instead of unrolling at factory-build time (Test5's four `ret++`),
        // the loop is emitted verbatim with a literal bound: `for (int i = 0; i < 4; i++) { ret++; }`.
        [Template(typeof(Factory), Options = TemplateOption.Bare)]
        static void QuoteLoop([Quote] int count)
        {
            int ret = 0;
            for (int i = 0; i < count; i++)
            {
                ret++;
            }
        }

        BlockSyntax node = Factory.QuoteLoop(4);

        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        return source;
    }

    public static string Test6()
    {
#pragma warning disable CS8321 // Local function is declared but never used
        [Template(typeof(Factory))]
        static T InlinedTypeArg<[Unquote] T, T2>()
        {
            List<T> list = new List<T>();
            List<T2> lis2t = new List<T2>();

            return list.First();
        }
#pragma warning restore CS8321 // Local function is declared but never used

        var node = Factory.InlinedTypeArg<string>();

        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        return source;
    }

    [Template(typeof(Factory))]
    private class Test7Class<[Unquote] T1, T2>
    {
        static void InlinedTypeArg<[Unquote] T3, T4>([Unquote] T3 inlinedValue)
        {
            List<T1> list = new List<T1>();
            List<T2> list2 = new List<T2>();
            List<T3> list3 = new List<T3>();
            List<T4> list4 = new List<T4>();
            list3.Add(inlinedValue);
        }

#pragma warning disable CS0693 // Type parameter has the same name as the type parameter from outer type
        static T1 HiddenTypeArg<[Unquote] T1>()
#pragma warning restore CS0693 // Type parameter has the same name as the type parameter from outer type
        {
            List<T1> list = new List<T1>();

            return list.First();
        }
    }

    public static string Test7()
    {
        var node = Factory.Test7Class<string, int, bool>(4);

        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        return source;
    }

    /// <summary>A fixed column descriptor consumed by the <see cref="DataRecord"/> member generator.</summary>
    public readonly record struct Column(int Ordinal, string Name);

    // An ObjectReader-shaped [Splice] member generator: the static `Accessors` method runs at factory-build time;
    // its `foreach` over the lifted column list (folded into the factory signature via Parameter<…>()) yields ONE
    // `int GetXxx() => <ordinal>;` MEMBER per column, which Synto splices into the produced `DataRecord` type. The
    // fixed `FieldCount` member is quoted verbatim; the generated accessors land at the generator's position.
    [Template(typeof(Factory))]
    public class DataRecord
    {
        public int FieldCount => 0;

        [Splice]
        static IEnumerable<MemberDeclarationSyntax> Accessors()
        {
            var columns = Parameter<IReadOnlyList<Column>>();
            foreach (var c in columns)
                yield return MethodDeclaration(PredefinedType(Token(IntKeyword)), Identifier("Get" + c.Name))
                    .AddModifiers(Token(PublicKeyword))
                    .WithExpressionBody(ArrowExpressionClause(
                        LiteralExpression(NumericLiteralExpression, Literal(c.Ordinal))))
                    .WithSemicolonToken(Token(SemicolonToken));
        }
    }

    public static string TestSplice()
    {
        IReadOnlyList<Column> columns = new[] { new Column(0, "Id"), new Column(1, "Name") };

        ClassDeclarationSyntax node = Factory.DataRecord(columns);

        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        return source;
    }

#pragma warning restore CS8321 // Local function is declared but never used
}


static partial class Factory { };



