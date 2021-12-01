using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Text;
using Xunit;

namespace Synto.Test
{
    public partial class UnitTest1
    {
        private partial class SF { };

        [Fact]
        public void Test0()
        {
            [Template(typeof(SF), Bare = true)]
            static void Simple()
            {
                Console.WriteLine("Hello ");
            }

            StatementSyntax node = SF.Simple();

            var source = node.NormalizeWhitespace().GetText(Encoding.UTF8).ToString().Trim();

            Assert.Equal("Console.WriteLine(\"Hello \" + \"World\");", source);

            var x = SyntaxFactory.ExpressionStatement(
                SyntaxFactory.List<AttributeListSyntax>(Array.Empty<AttributeListSyntax>()), 
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression, 
                        SyntaxFactory.IdentifierName(
                            SyntaxFactory.Token(
                                SyntaxFactory.TriviaList(), 
                                SyntaxKind.IdentifierToken, 
                                "Console", 
                                "Console", 
                                SyntaxFactory.TriviaList())), 
                        SyntaxFactory.Token(
                            SyntaxFactory.TriviaList(), 
                            SyntaxKind.DotToken, 
                            ".", 
                            ".", 
                            SyntaxFactory.TriviaList()), 
                        SyntaxFactory.IdentifierName(SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.IdentifierToken, "WriteLine", "WriteLine", SyntaxFactory.TriviaList()))), SyntaxFactory.ArgumentList(SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.OpenParenToken, "(", "(", SyntaxFactory.TriviaList()), SyntaxFactory.SeparatedList(new[] { SyntaxFactory.Argument(null, SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.None, "", "", SyntaxFactory.TriviaList()), SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.StringLiteralToken, "\"Hello \"", "Hello ", SyntaxFactory.TriviaList()))) }, Array.Empty<SyntaxToken>()), SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.CloseParenToken, ")", ")", SyntaxFactory.TriviaList()))), SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.SemicolonToken, ";", ";", SyntaxFactory.TriviaList()));

        }

        [Fact]
        public void Test1()
        {
            [Template(typeof(SF), Bare = true)]
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
            [Template(typeof(SF), Bare = true)]
            static void Inner()
            {
                new StringBuilder("Something").Append("Foo").ToString();
            }


            [Template(typeof(SF), Bare = true)]
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
            

            [Template(typeof(SF), Bare = true)]
            static void Greeting(string message)
            {
                Console.WriteLine("Hello " + message);
            }


            [Template(typeof(SF), Bare = true)]
            static void Repeater(Syntax item)
            {
                item();
                item();
            }


            BlockSyntax node = SF.Repeater(SF.Greeting("World").Expression);

            var source = node.NormalizeWhitespace().GetText(Encoding.UTF8).ToString().Trim();

            Assert.Equal("{\r\n    Console.WriteLine(\"Hello \" + \"World\");\r\n    Console.WriteLine(\"Hello \" + \"World\");\r\n}", source);
        }


    }
}
