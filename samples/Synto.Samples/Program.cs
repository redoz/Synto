// See https://aka.ms/new-console-template for more information
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Synto;

MethodDeclarationSyntax node = Factory.Simple();

var source = node.NormalizeWhitespace(eol: Environment.NewLine).GetText(Encoding.UTF8).ToString().Trim();


Console.WriteLine(source);


class Templates
{
    [Template(typeof(Factory), Options = TemplateOption.Default)]
    static void Simple()
    {
        Console.WriteLine("Hello World");
    }
}

partial class Factory { };



