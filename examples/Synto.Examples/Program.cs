// See https://aka.ms/new-console-template for more information

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto;
using Spectre.Console;
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
                new Choice("Inline type parameters", Test6),
                new Choice("Inline type parameters in class declaration", Test7),
                
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
        static void Hello([Inline] string message)
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
        static void Outer([Inline(AsSyntax = true)] string message)
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

        static void Greeting([Inline] string message)
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
        static void Loop([Inline] int count)
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

    public static string Test6()
    {
#pragma warning disable CS8321 // Local function is declared but never used
        [Template(typeof(Factory))]
        static T InlinedTypeArg<[Inline] T, T2>()
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
    private class Test7Class<[Inline] T1, T2>
    {
        static void InlinedTypeArg<[Inline] T3, T4>([Inline] T3 inlinedValue)
        {
            List<T1> list = new List<T1>();
            List<T2> list2 = new List<T2>();
            List<T3> list3 = new List<T3>();
            List<T4> list4 = new List<T4>();
            list3.Add(inlinedValue);
        }

#pragma warning disable CS0693 // Type parameter has the same name as the type parameter from outer type
        static T1 HiddenTypeArg<[Inline] T1>()
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

#pragma warning restore CS8321 // Local function is declared but never used
}


static partial class Factory { };



