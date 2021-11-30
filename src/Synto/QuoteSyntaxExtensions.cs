using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto;

internal static class QuoteSyntaxExtensions
{
    public static ExpressionSyntax OrQuotedNullLiteral(this ExpressionSyntax? expr)
    {
        return expr ?? SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
    }
}

