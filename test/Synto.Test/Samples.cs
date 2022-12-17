using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Text;
using Xunit;
#pragma warning disable CS8321

namespace Synto.Test;

public partial class Samples
{
    private partial class Factory { };

    [Fact]
    public void Test0()
    {
        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Simple()
        {
            Console.WriteLine("Hello World");
        }

        StatementSyntax node = Factory.Simple();

        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        Assert.Equal("Console.WriteLine(\"Hello World\");", source);
    }

    [Fact]
    public void Test1()
    {
        [Template(typeof(Factory), Options = TemplateOption.Single)]
        static void Hello(string message)
        {
            Console.WriteLine("Hello " + message);
        }

        StatementSyntax node = Factory.Hello("World");

        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

        Assert.Equal("Console.WriteLine(\"Hello \" + \"World\");", source);
    }

    [Fact]
    public void Test2()
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

        Assert.Equal("Console.WriteLine(\"Hello \" + new StringBuilder(\"Something\").Append(\"Foo\").ToString());", source);
    }

    [Fact]
    public void Test3()
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

        Assert.Equal("""
        {
            Console.WriteLine("Hello " + "World");
            Console.WriteLine("Hello " + "World");
        }
        """, 
        source);
    }


    [Fact]
    public void Test4()
    {

        [Template(typeof(Factory), Options = TemplateOption.Bare)]
        static void NoUnroll(int count)
        {
            int ret = 0;
            for (int i = 0; i < count; i++)
            {
                ret++;
            }
        }

        BlockSyntax node = Factory.NoUnroll(4);


        var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();
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


//[Fact]
//public void Test5()
//{
//    [Template(typeof(Factory), Options = TemplateOption.Bare)]
//    static void Unroll([Unquote] int count)
//    {
//        int ret = 0;
//        for (int i = 0; i < count; i++)
//        {
//            ret++;
//        }
//    }

//    BlockSyntax node = Factory.Unroll(4);


//    var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();
//    string expected = """ 
//                          {
//                              int ret = 0;
//                                  ret++;
//                                  ret++;
//                                  ret++;
//                                  ret++; 
//                          }
//                          """;

//    Assert.Equal(expected, source);
//}
