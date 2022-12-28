using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

public static class QuoteSyntaxExtensions
{
    public static ExpressionSyntax OrNullLiteralExpression(this ExpressionSyntax? expr)
    {
        return expr ?? SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
    }
}

