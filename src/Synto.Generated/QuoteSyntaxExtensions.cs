using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.Generated
{
    internal static class QuoteSyntaxExtensions
    {
        public static ExpressionSyntax OrQuotedNullLiteral(this ExpressionSyntax? expr)
        {
            return expr ?? SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
        }
    }
}
