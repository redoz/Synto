// See https://aka.ms/new-console-template for more information
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static void Simple()
{
    Console.WriteLine("Hello World");
}

partial class Factory { };

//[Template(typeof(Factory), Options = TemplateOption.Single)]

StatementSyntax node = Factory.Simple();

var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();

Console.WriteLine("Hello, World!");
Console.WriteLine(source);






