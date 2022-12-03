using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Synto.Test;

public partial class UnitTest1
{
    private partial class SF { };

    [Fact]
    public void Test0()
    {
        [Template(typeof(SF), Options = TemplateOption.Single)]
        static void Simple()
        {
            Console.WriteLine("Hello World");
        }

        StatementSyntax node = SF.Simple();

        var source = node.NormalizeWhitespace().GetText(Encoding.UTF8).ToString().Trim();

        Assert.Equal("Console.WriteLine(\"Hello World\");", source);
    }

    [Fact]
    public void Test1()
    {
        [Template(typeof(SF), Options = TemplateOption.Single)]
        static void Hello(string message)
        {
            Console.WriteLine("Hello " + message);
        }

        StatementSyntax node = SF.Hello("World");

        var source = node.NormalizeWhitespace().GetText(Encoding.UTF8).ToString().Trim();

        Assert.Equal("Console.WriteLine(\"Hello \" + \"World\");", source);
    }

    [Fact]
    public void Test2()
    {
        [Template(typeof(SF), Options = TemplateOption.Single)]
        static void Inner()
        {
            new StringBuilder("Something").Append("Foo").ToString();
        }


        [Template(typeof(SF), Options = TemplateOption.Single)]
        static void Outer(Syntax<string> message)
        {
            Console.WriteLine("Hello " + message());
        }

        StatementSyntax node = SF.Outer(SF.Inner().Expression);

        var source = node.NormalizeWhitespace().GetText(Encoding.UTF8).ToString().Trim();

        Assert.Equal("Console.WriteLine(\"Hello \" + new StringBuilder(\"Something\").Append(\"Foo\").ToString());", source);
    }

    [Fact]
    public void Test3()
    {


        [Template(typeof(SF), Options = TemplateOption.Single)]
        static void Greeting(string message)
        {
            Console.WriteLine("Hello " + message);
        }


        [Template(typeof(SF), Options = TemplateOption.Bare)]
        static void Repeater(Syntax item)
        {
            item();
            item();
        }

        BlockSyntax node = SF.Repeater(SF.Greeting("World").Expression);

        var source = node.NormalizeWhitespace().GetText(Encoding.UTF8).ToString().Trim();

        Assert.Equal("""
                     {
                         Console.WriteLine("Hello " + "World");
                         Console.WriteLine("Hello " + "World");
                     }
                     """
                     , source);
    }


    [Fact]
    public void Test4()
    {
        [Template(typeof(SF), Options = TemplateOption.Bare)]
        static void NoUnroll(int count)
        {
            int ret = 0;
            for (int i = 0; i < count; i++)
            {
                ret++;
            }
        }

        BlockSyntax node = SF.NoUnroll(4);


        var source = node.NormalizeWhitespace().GetText(Encoding.UTF8).ToString().Trim();
        string expected = """ 
                              {
                                  int ret = 0;
                                  for (int i = 0; i < 4; i++)
                                  {
                                      ret++;
                                  }
                              }
                              """;

        Assert.Equal(expected, source);
    }


}