// See https://aka.ms/new-console-template for more information
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto;
using Spectre.Console;
using Synto.Templating;
using static Examples;


for (;;)
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
                new Choice("Use a parameter to inject a numeric literal into a for-loop", Test5),
                new Choice("Exit", () =>
                {
                    Environment.Exit(0);
                    return "";
                })
            }));

    // Echo the fruit back to the terminal
    AnsiConsole.Write(new Rule(choice.Name).Alignment(Justify.Left));
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine(choice.Example());
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule());
    AnsiConsole.WriteLine();

}

record Choice(string Name, Func<string> Example)
{
    public override string ToString() => Name;
}

public class Examples
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
        static void Hello(string message)
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
        static void Outer(Syntax<string> message)
        {
            Console.WriteLine("Hello " + message());
        }


        StatementSyntax node = Factory.Outer(Factory.Inner().Expression);

        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        return source;
    }


    public static string Test3()
    {

        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Greeting(string message)
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

        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        return source;
    }


    public static string Test4()
    {
        [Template(typeof(Factory), Options = TemplateOption.Default)]
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
        static void Loop(int count)
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
#pragma warning restore CS8321 // Local function is declared but never used
}


partial class Factory { };



