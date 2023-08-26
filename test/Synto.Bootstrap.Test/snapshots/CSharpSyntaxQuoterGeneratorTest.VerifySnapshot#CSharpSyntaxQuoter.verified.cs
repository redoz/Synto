//HintName: CSharpSyntaxQuoter.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto;
public partial class CSharpSyntaxQuoter
{
    public override ExpressionSyntax? VisitQualifiedName(QualifiedNameSyntax node)
    {
        // QualifiedName(Visit(node.Left)!, Visit(node.DotToken)!, Visit(node.Right)!)
        return InvocationExpression(
                   IdentifierName(nameof(QualifiedName)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Left)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.DotToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Right)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitGenericName(GenericNameSyntax node)
    {
        // GenericName(Visit(node.Identifier)!, Visit(node.TypeArgumentList)!)
        return InvocationExpression(
                   IdentifierName(nameof(GenericName)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TypeArgumentList)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitTypeArgumentList(TypeArgumentListSyntax node)
    {
        // TypeArgumentList(Visit(node.LessThanToken)!, Visit(node.Arguments)!, Visit(node.GreaterThanToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(TypeArgumentList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LessThanToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Arguments)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.GreaterThanToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAliasQualifiedName(AliasQualifiedNameSyntax node)
    {
        // AliasQualifiedName(Visit(node.Alias)!, Visit(node.ColonColonToken)!, Visit(node.Name)!)
        return InvocationExpression(
                   IdentifierName(nameof(AliasQualifiedName)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Alias)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonColonToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitPredefinedType(PredefinedTypeSyntax node)
    {
        // PredefinedType(Visit(node.Keyword)!)
        return InvocationExpression(
                   IdentifierName(nameof(PredefinedType)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitArrayType(ArrayTypeSyntax node)
    {
        // ArrayType(Visit(node.ElementType)!, Visit(node.RankSpecifiers)!)
        return InvocationExpression(
                   IdentifierName(nameof(ArrayType)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ElementType)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.RankSpecifiers)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitArrayRankSpecifier(ArrayRankSpecifierSyntax node)
    {
        // ArrayRankSpecifier(Visit(node.OpenBracketToken)!, Visit(node.Sizes)!, Visit(node.CloseBracketToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ArrayRankSpecifier)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBracketToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Sizes)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBracketToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitPointerType(PointerTypeSyntax node)
    {
        // PointerType(Visit(node.ElementType)!, Visit(node.AsteriskToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(PointerType)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ElementType)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AsteriskToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitFunctionPointerType(FunctionPointerTypeSyntax node)
    {
        // FunctionPointerType(Visit(node.DelegateKeyword)!, Visit(node.AsteriskToken)!, Visit(node.CallingConvention).OrNullLiteralExpression(), Visit(node.ParameterList)!)
        return InvocationExpression(
                   IdentifierName(nameof(FunctionPointerType)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.DelegateKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AsteriskToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CallingConvention).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitFunctionPointerParameterList(FunctionPointerParameterListSyntax node)
    {
        // FunctionPointerParameterList(Visit(node.LessThanToken)!, Visit(node.Parameters)!, Visit(node.GreaterThanToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(FunctionPointerParameterList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LessThanToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Parameters)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.GreaterThanToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitFunctionPointerCallingConvention(FunctionPointerCallingConventionSyntax node)
    {
        // FunctionPointerCallingConvention(Visit(node.ManagedOrUnmanagedKeyword)!, Visit(node.UnmanagedCallingConventionList).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(FunctionPointerCallingConvention)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ManagedOrUnmanagedKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.UnmanagedCallingConventionList).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitFunctionPointerUnmanagedCallingConventionList(FunctionPointerUnmanagedCallingConventionListSyntax node)
    {
        // FunctionPointerUnmanagedCallingConventionList(Visit(node.OpenBracketToken)!, Visit(node.CallingConventions)!, Visit(node.CloseBracketToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(FunctionPointerUnmanagedCallingConventionList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBracketToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CallingConventions)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBracketToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitFunctionPointerUnmanagedCallingConvention(FunctionPointerUnmanagedCallingConventionSyntax node)
    {
        // FunctionPointerUnmanagedCallingConvention(Visit(node.Name)!)
        return InvocationExpression(
                   IdentifierName(nameof(FunctionPointerUnmanagedCallingConvention)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitNullableType(NullableTypeSyntax node)
    {
        // NullableType(Visit(node.ElementType)!, Visit(node.QuestionToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(NullableType)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ElementType)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.QuestionToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitTupleType(TupleTypeSyntax node)
    {
        // TupleType(Visit(node.OpenParenToken)!, Visit(node.Elements)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(TupleType)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Elements)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitTupleElement(TupleElementSyntax node)
    {
        // TupleElement(Visit(node.Type)!, Visit(node.Identifier)!)
        return InvocationExpression(
                   IdentifierName(nameof(TupleElement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitOmittedTypeArgument(OmittedTypeArgumentSyntax node)
    {
        // OmittedTypeArgument(Visit(node.OmittedTypeArgumentToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(OmittedTypeArgument)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OmittedTypeArgumentToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitRefType(RefTypeSyntax node)
    {
        // RefType(Visit(node.RefKeyword)!, Visit(node.ReadOnlyKeyword)!, Visit(node.Type)!)
        return InvocationExpression(
                   IdentifierName(nameof(RefType)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.RefKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ReadOnlyKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitScopedType(ScopedTypeSyntax node)
    {
        // ScopedType(Visit(node.ScopedKeyword)!, Visit(node.Type)!)
        return InvocationExpression(
                   IdentifierName(nameof(ScopedType)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ScopedKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
    {
        // ParenthesizedExpression(Visit(node.OpenParenToken)!, Visit(node.Expression)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ParenthesizedExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitTupleExpression(TupleExpressionSyntax node)
    {
        // TupleExpression(Visit(node.OpenParenToken)!, Visit(node.Arguments)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(TupleExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Arguments)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
    {
        // PrefixUnaryExpression(Visit(node.Kind())!, Visit(node.OperatorToken)!, Visit(node.Operand)!)
        return InvocationExpression(
                   IdentifierName(nameof(PrefixUnaryExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Operand)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAwaitExpression(AwaitExpressionSyntax node)
    {
        // AwaitExpression(Visit(node.AwaitKeyword)!, Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(AwaitExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AwaitKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
    {
        // PostfixUnaryExpression(Visit(node.Kind())!, Visit(node.Operand)!, Visit(node.OperatorToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(PostfixUnaryExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Operand)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        // MemberAccessExpression(Visit(node.Kind())!, Visit(node.Expression)!, Visit(node.OperatorToken)!, Visit(node.Name)!)
        return InvocationExpression(
                   IdentifierName(nameof(MemberAccessExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
    {
        // ConditionalAccessExpression(Visit(node.Expression)!, Visit(node.OperatorToken)!, Visit(node.WhenNotNull)!)
        return InvocationExpression(
                   IdentifierName(nameof(ConditionalAccessExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WhenNotNull)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitMemberBindingExpression(MemberBindingExpressionSyntax node)
    {
        // MemberBindingExpression(Visit(node.OperatorToken)!, Visit(node.Name)!)
        return InvocationExpression(
                   IdentifierName(nameof(MemberBindingExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitElementBindingExpression(ElementBindingExpressionSyntax node)
    {
        // ElementBindingExpression(Visit(node.ArgumentList)!)
        return InvocationExpression(
                   IdentifierName(nameof(ElementBindingExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ArgumentList)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitRangeExpression(RangeExpressionSyntax node)
    {
        // RangeExpression(Visit(node.LeftOperand).OrNullLiteralExpression(), Visit(node.OperatorToken).OrNullLiteralExpression(), Visit(node.RightOperand).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(RangeExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LeftOperand).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.RightOperand).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitImplicitElementAccess(ImplicitElementAccessSyntax node)
    {
        // ImplicitElementAccess(Visit(node.ArgumentList)!)
        return InvocationExpression(
                   IdentifierName(nameof(ImplicitElementAccess)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ArgumentList)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        // BinaryExpression(Visit(node.Kind())!, Visit(node.Left)!, Visit(node.OperatorToken)!, Visit(node.Right)!)
        return InvocationExpression(
                   IdentifierName(nameof(BinaryExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Left)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Right)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        // AssignmentExpression(Visit(node.Kind())!, Visit(node.Left)!, Visit(node.OperatorToken)!, Visit(node.Right)!)
        return InvocationExpression(
                   IdentifierName(nameof(AssignmentExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Left)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Right)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        // ConditionalExpression(Visit(node.Condition)!, Visit(node.QuestionToken)!, Visit(node.WhenTrue)!, Visit(node.ColonToken)!, Visit(node.WhenFalse)!)
        return InvocationExpression(
                   IdentifierName(nameof(ConditionalExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Condition)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.QuestionToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WhenTrue)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WhenFalse)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitThisExpression(ThisExpressionSyntax node)
    {
        // ThisExpression(Visit(node.Token)!)
        return InvocationExpression(
                   IdentifierName(nameof(ThisExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Token)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitBaseExpression(BaseExpressionSyntax node)
    {
        // BaseExpression(Visit(node.Token)!)
        return InvocationExpression(
                   IdentifierName(nameof(BaseExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Token)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        // LiteralExpression(Visit(node.Kind())!, Visit(node.Token)!)
        return InvocationExpression(
                   IdentifierName(nameof(LiteralExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Token)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitMakeRefExpression(MakeRefExpressionSyntax node)
    {
        // MakeRefExpression(Visit(node.Keyword)!, Visit(node.OpenParenToken)!, Visit(node.Expression)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(MakeRefExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitRefTypeExpression(RefTypeExpressionSyntax node)
    {
        // RefTypeExpression(Visit(node.Keyword)!, Visit(node.OpenParenToken)!, Visit(node.Expression)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(RefTypeExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitRefValueExpression(RefValueExpressionSyntax node)
    {
        // RefValueExpression(Visit(node.Keyword)!, Visit(node.OpenParenToken)!, Visit(node.Expression)!, Visit(node.Comma)!, Visit(node.Type)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(RefValueExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Comma)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitCheckedExpression(CheckedExpressionSyntax node)
    {
        // CheckedExpression(Visit(node.Kind())!, Visit(node.Keyword)!, Visit(node.OpenParenToken)!, Visit(node.Expression)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(CheckedExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitDefaultExpression(DefaultExpressionSyntax node)
    {
        // DefaultExpression(Visit(node.Keyword)!, Visit(node.OpenParenToken)!, Visit(node.Type)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(DefaultExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitTypeOfExpression(TypeOfExpressionSyntax node)
    {
        // TypeOfExpression(Visit(node.Keyword)!, Visit(node.OpenParenToken)!, Visit(node.Type)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(TypeOfExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitSizeOfExpression(SizeOfExpressionSyntax node)
    {
        // SizeOfExpression(Visit(node.Keyword)!, Visit(node.OpenParenToken)!, Visit(node.Type)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(SizeOfExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // InvocationExpression(Visit(node.Expression)!, Visit(node.ArgumentList)!)
        return InvocationExpression(
                   IdentifierName(nameof(InvocationExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ArgumentList)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        // ElementAccessExpression(Visit(node.Expression)!, Visit(node.ArgumentList)!)
        return InvocationExpression(
                   IdentifierName(nameof(ElementAccessExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ArgumentList)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitArgumentList(ArgumentListSyntax node)
    {
        // ArgumentList(Visit(node.OpenParenToken)!, Visit(node.Arguments)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ArgumentList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Arguments)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitBracketedArgumentList(BracketedArgumentListSyntax node)
    {
        // BracketedArgumentList(Visit(node.OpenBracketToken)!, Visit(node.Arguments)!, Visit(node.CloseBracketToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(BracketedArgumentList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBracketToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Arguments)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBracketToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitArgument(ArgumentSyntax node)
    {
        // Argument(Visit(node.NameColon).OrNullLiteralExpression(), Visit(node.RefKindKeyword)!, Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(Argument)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NameColon).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.RefKindKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitExpressionColon(ExpressionColonSyntax node)
    {
        // ExpressionColon(Visit(node.Expression)!, Visit(node.ColonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ExpressionColon)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitNameColon(NameColonSyntax node)
    {
        // NameColon(Visit(node.Name)!, Visit(node.ColonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(NameColon)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitDeclarationExpression(DeclarationExpressionSyntax node)
    {
        // DeclarationExpression(Visit(node.Type)!, Visit(node.Designation)!)
        return InvocationExpression(
                   IdentifierName(nameof(DeclarationExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Designation)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitCastExpression(CastExpressionSyntax node)
    {
        // CastExpression(Visit(node.OpenParenToken)!, Visit(node.Type)!, Visit(node.CloseParenToken)!, Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(CastExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
    {
        // AnonymousMethodExpression(Visit(node.Modifiers)!, Visit(node.DelegateKeyword)!, Visit(node.ParameterList).OrNullLiteralExpression(), Visit(node.Block)!, Visit(node.ExpressionBody).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(AnonymousMethodExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.DelegateKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Block)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExpressionBody).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        // SimpleLambdaExpression(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.Parameter)!, Visit(node.ArrowToken)!, Visit(node.Block).OrNullLiteralExpression(), Visit(node.ExpressionBody).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(SimpleLambdaExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Parameter)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ArrowToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Block).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExpressionBody).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitRefExpression(RefExpressionSyntax node)
    {
        // RefExpression(Visit(node.RefKeyword)!, Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(RefExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.RefKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        // ParenthesizedLambdaExpression(Visit(node.AttributeLists)!, Visit(node.Modifiers).OrNullLiteralExpression(), Visit(node.ReturnType).OrNullLiteralExpression(), Visit(node.ParameterList)!, Visit(node.ArrowToken).OrNullLiteralExpression(), Visit(node.Block).OrNullLiteralExpression(), Visit(node.ExpressionBody).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(ParenthesizedLambdaExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ReturnType).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ArrowToken).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Block).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExpressionBody).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitInitializerExpression(InitializerExpressionSyntax node)
    {
        // InitializerExpression(Visit(node.Kind())!, Visit(node.OpenBraceToken)!, Visit(node.Expressions)!, Visit(node.CloseBraceToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(InitializerExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expressions)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        // ImplicitObjectCreationExpression(Visit(node.NewKeyword)!, Visit(node.ArgumentList)!, Visit(node.Initializer).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(ImplicitObjectCreationExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NewKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ArgumentList)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Initializer).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        // ObjectCreationExpression(Visit(node.NewKeyword)!, Visit(node.Type)!, Visit(node.ArgumentList).OrNullLiteralExpression(), Visit(node.Initializer).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(ObjectCreationExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NewKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ArgumentList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Initializer).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitWithExpression(WithExpressionSyntax node)
    {
        // WithExpression(Visit(node.Expression)!, Visit(node.WithKeyword)!, Visit(node.Initializer)!)
        return InvocationExpression(
                   IdentifierName(nameof(WithExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WithKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Initializer)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAnonymousObjectMemberDeclarator(AnonymousObjectMemberDeclaratorSyntax node)
    {
        // AnonymousObjectMemberDeclarator(Visit(node.NameEquals).OrNullLiteralExpression(), Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(AnonymousObjectMemberDeclarator)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NameEquals).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpressionSyntax node)
    {
        // AnonymousObjectCreationExpression(Visit(node.NewKeyword)!, Visit(node.OpenBraceToken)!, Visit(node.Initializers)!, Visit(node.CloseBraceToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(AnonymousObjectCreationExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NewKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Initializers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
    {
        // ArrayCreationExpression(Visit(node.NewKeyword)!, Visit(node.Type)!, Visit(node.Initializer).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(ArrayCreationExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NewKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Initializer).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
    {
        // ImplicitArrayCreationExpression(Visit(node.NewKeyword)!, Visit(node.OpenBracketToken)!, Visit(node.Commas)!, Visit(node.CloseBracketToken)!, Visit(node.Initializer)!)
        return InvocationExpression(
                   IdentifierName(nameof(ImplicitArrayCreationExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NewKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBracketToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Commas)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBracketToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Initializer)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node)
    {
        // StackAllocArrayCreationExpression(Visit(node.StackAllocKeyword)!, Visit(node.Type)!, Visit(node.Initializer).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(StackAllocArrayCreationExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.StackAllocKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Initializer).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitImplicitStackAllocArrayCreationExpression(ImplicitStackAllocArrayCreationExpressionSyntax node)
    {
        // ImplicitStackAllocArrayCreationExpression(Visit(node.StackAllocKeyword)!, Visit(node.OpenBracketToken)!, Visit(node.CloseBracketToken)!, Visit(node.Initializer)!)
        return InvocationExpression(
                   IdentifierName(nameof(ImplicitStackAllocArrayCreationExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.StackAllocKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBracketToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBracketToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Initializer)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitCollectionExpression(CollectionExpressionSyntax node)
    {
        // CollectionExpression(Visit(node.OpenBracketToken)!, Visit(node.Elements)!, Visit(node.CloseBracketToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(CollectionExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBracketToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Elements)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBracketToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitExpressionElement(ExpressionElementSyntax node)
    {
        // ExpressionElement(Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(ExpressionElement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitSpreadElement(SpreadElementSyntax node)
    {
        // SpreadElement(Visit(node.OperatorToken)!, Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(SpreadElement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitQueryExpression(QueryExpressionSyntax node)
    {
        // QueryExpression(Visit(node.FromClause)!, Visit(node.Body)!)
        return InvocationExpression(
                   IdentifierName(nameof(QueryExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.FromClause)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Body)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitQueryBody(QueryBodySyntax node)
    {
        // QueryBody(Visit(node.Clauses)!, Visit(node.SelectOrGroup)!, Visit(node.Continuation).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(QueryBody)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Clauses)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SelectOrGroup)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Continuation).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitFromClause(FromClauseSyntax node)
    {
        // FromClause(Visit(node.FromKeyword)!, Visit(node.Type).OrNullLiteralExpression(), Visit(node.Identifier)!, Visit(node.InKeyword)!, Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(FromClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.FromKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.InKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitLetClause(LetClauseSyntax node)
    {
        // LetClause(Visit(node.LetKeyword)!, Visit(node.Identifier)!, Visit(node.EqualsToken)!, Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(LetClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LetKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EqualsToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitJoinClause(JoinClauseSyntax node)
    {
        // JoinClause(Visit(node.JoinKeyword)!, Visit(node.Type).OrNullLiteralExpression(), Visit(node.Identifier)!, Visit(node.InKeyword)!, Visit(node.InExpression)!, Visit(node.OnKeyword)!, Visit(node.LeftExpression)!, Visit(node.EqualsKeyword)!, Visit(node.RightExpression)!, Visit(node.Into).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(JoinClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.JoinKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.InKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.InExpression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OnKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LeftExpression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EqualsKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.RightExpression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Into).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitJoinIntoClause(JoinIntoClauseSyntax node)
    {
        // JoinIntoClause(Visit(node.IntoKeyword)!, Visit(node.Identifier)!)
        return InvocationExpression(
                   IdentifierName(nameof(JoinIntoClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.IntoKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitWhereClause(WhereClauseSyntax node)
    {
        // WhereClause(Visit(node.WhereKeyword)!, Visit(node.Condition)!)
        return InvocationExpression(
                   IdentifierName(nameof(WhereClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WhereKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Condition)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitOrderByClause(OrderByClauseSyntax node)
    {
        // OrderByClause(Visit(node.OrderByKeyword)!, Visit(node.Orderings)!)
        return InvocationExpression(
                   IdentifierName(nameof(OrderByClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OrderByKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Orderings)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitOrdering(OrderingSyntax node)
    {
        // Ordering(Visit(node.Kind())!, Visit(node.Expression)!, Visit(node.AscendingOrDescendingKeyword)!)
        return InvocationExpression(
                   IdentifierName(nameof(Ordering)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AscendingOrDescendingKeyword)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitSelectClause(SelectClauseSyntax node)
    {
        // SelectClause(Visit(node.SelectKeyword)!, Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(SelectClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SelectKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitGroupClause(GroupClauseSyntax node)
    {
        // GroupClause(Visit(node.GroupKeyword)!, Visit(node.GroupExpression)!, Visit(node.ByKeyword)!, Visit(node.ByExpression)!)
        return InvocationExpression(
                   IdentifierName(nameof(GroupClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.GroupKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.GroupExpression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ByKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ByExpression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitQueryContinuation(QueryContinuationSyntax node)
    {
        // QueryContinuation(Visit(node.IntoKeyword)!, Visit(node.Identifier)!, Visit(node.Body)!)
        return InvocationExpression(
                   IdentifierName(nameof(QueryContinuation)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.IntoKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Body)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitOmittedArraySizeExpression(OmittedArraySizeExpressionSyntax node)
    {
        // OmittedArraySizeExpression(Visit(node.OmittedArraySizeExpressionToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(OmittedArraySizeExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OmittedArraySizeExpressionToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
    {
        // InterpolatedStringExpression(Visit(node.StringStartToken)!, Visit(node.Contents)!, Visit(node.StringEndToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(InterpolatedStringExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.StringStartToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Contents)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.StringEndToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitIsPatternExpression(IsPatternExpressionSyntax node)
    {
        // IsPatternExpression(Visit(node.Expression)!, Visit(node.IsKeyword)!, Visit(node.Pattern)!)
        return InvocationExpression(
                   IdentifierName(nameof(IsPatternExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.IsKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Pattern)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitThrowExpression(ThrowExpressionSyntax node)
    {
        // ThrowExpression(Visit(node.ThrowKeyword)!, Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(ThrowExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ThrowKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitWhenClause(WhenClauseSyntax node)
    {
        // WhenClause(Visit(node.WhenKeyword)!, Visit(node.Condition)!)
        return InvocationExpression(
                   IdentifierName(nameof(WhenClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WhenKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Condition)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitDiscardPattern(DiscardPatternSyntax node)
    {
        // DiscardPattern(Visit(node.UnderscoreToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(DiscardPattern)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.UnderscoreToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitDeclarationPattern(DeclarationPatternSyntax node)
    {
        // DeclarationPattern(Visit(node.Type)!, Visit(node.Designation)!)
        return InvocationExpression(
                   IdentifierName(nameof(DeclarationPattern)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Designation)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitVarPattern(VarPatternSyntax node)
    {
        // VarPattern(Visit(node.VarKeyword)!, Visit(node.Designation)!)
        return InvocationExpression(
                   IdentifierName(nameof(VarPattern)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.VarKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Designation)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitRecursivePattern(RecursivePatternSyntax node)
    {
        // RecursivePattern(Visit(node.Type).OrNullLiteralExpression(), Visit(node.PositionalPatternClause).OrNullLiteralExpression(), Visit(node.PropertyPatternClause).OrNullLiteralExpression(), Visit(node.Designation).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(RecursivePattern)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.PositionalPatternClause).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.PropertyPatternClause).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Designation).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitPositionalPatternClause(PositionalPatternClauseSyntax node)
    {
        // PositionalPatternClause(Visit(node.OpenParenToken)!, Visit(node.Subpatterns)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(PositionalPatternClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Subpatterns)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitPropertyPatternClause(PropertyPatternClauseSyntax node)
    {
        // PropertyPatternClause(Visit(node.OpenBraceToken)!, Visit(node.Subpatterns)!, Visit(node.CloseBraceToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(PropertyPatternClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Subpatterns)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitSubpattern(SubpatternSyntax node)
    {
        // Subpattern(Visit(node.NameColon).OrNullLiteralExpression(), Visit(node.Pattern)!)
        return InvocationExpression(
                   IdentifierName(nameof(Subpattern)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NameColon).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Pattern)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitConstantPattern(ConstantPatternSyntax node)
    {
        // ConstantPattern(Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(ConstantPattern)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitParenthesizedPattern(ParenthesizedPatternSyntax node)
    {
        // ParenthesizedPattern(Visit(node.OpenParenToken)!, Visit(node.Pattern)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ParenthesizedPattern)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Pattern)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitRelationalPattern(RelationalPatternSyntax node)
    {
        // RelationalPattern(Visit(node.OperatorToken)!, Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(RelationalPattern)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitTypePattern(TypePatternSyntax node)
    {
        // TypePattern(Visit(node.Type)!)
        return InvocationExpression(
                   IdentifierName(nameof(TypePattern)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitBinaryPattern(BinaryPatternSyntax node)
    {
        // BinaryPattern(Visit(node.Kind())!, Visit(node.Left)!, Visit(node.OperatorToken)!, Visit(node.Right)!)
        return InvocationExpression(
                   IdentifierName(nameof(BinaryPattern)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Left)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Right)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitUnaryPattern(UnaryPatternSyntax node)
    {
        // UnaryPattern(Visit(node.OperatorToken)!, Visit(node.Pattern)!)
        return InvocationExpression(
                   IdentifierName(nameof(UnaryPattern)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Pattern)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitListPattern(ListPatternSyntax node)
    {
        // ListPattern(Visit(node.OpenBracketToken)!, Visit(node.Patterns)!, Visit(node.CloseBracketToken)!, Visit(node.Designation).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(ListPattern)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBracketToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Patterns)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBracketToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Designation).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitSlicePattern(SlicePatternSyntax node)
    {
        // SlicePattern(Visit(node.DotDotToken)!, Visit(node.Pattern).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(SlicePattern)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.DotDotToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Pattern).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitInterpolatedStringText(InterpolatedStringTextSyntax node)
    {
        // InterpolatedStringText(Visit(node.TextToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(InterpolatedStringText)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TextToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitInterpolation(InterpolationSyntax node)
    {
        // Interpolation(Visit(node.OpenBraceToken)!, Visit(node.Expression)!, Visit(node.AlignmentClause).OrNullLiteralExpression(), Visit(node.FormatClause).OrNullLiteralExpression(), Visit(node.CloseBraceToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(Interpolation)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AlignmentClause).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.FormatClause).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitInterpolationAlignmentClause(InterpolationAlignmentClauseSyntax node)
    {
        // InterpolationAlignmentClause(Visit(node.CommaToken)!, Visit(node.Value)!)
        return InvocationExpression(
                   IdentifierName(nameof(InterpolationAlignmentClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CommaToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Value)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitInterpolationFormatClause(InterpolationFormatClauseSyntax node)
    {
        // InterpolationFormatClause(Visit(node.ColonToken)!, Visit(node.FormatStringToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(InterpolationFormatClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.FormatStringToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitGlobalStatement(GlobalStatementSyntax node)
    {
        // GlobalStatement(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.Statement)!)
        return InvocationExpression(
                   IdentifierName(nameof(GlobalStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statement)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitBlock(BlockSyntax node)
    {
        // Block(Visit(node.AttributeLists)!, Visit(node.OpenBraceToken)!, Visit(node.Statements)!, Visit(node.CloseBraceToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(Block)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statements)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        // LocalFunctionStatement(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.ReturnType)!, Visit(node.Identifier)!, Visit(node.TypeParameterList).OrNullLiteralExpression(), Visit(node.ParameterList)!, Visit(node.ConstraintClauses)!, Visit(node.Body).OrNullLiteralExpression(), Visit(node.ExpressionBody).OrNullLiteralExpression(), Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(LocalFunctionStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ReturnType)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TypeParameterList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ConstraintClauses)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Body).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExpressionBody).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        // LocalDeclarationStatement(Visit(node.AttributeLists)!, Visit(node.AwaitKeyword)!, Visit(node.UsingKeyword)!, Visit(node.Modifiers)!, Visit(node.Declaration)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(LocalDeclarationStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AwaitKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.UsingKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Declaration)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        // VariableDeclaration(Visit(node.Type)!, Visit(node.Variables)!)
        return InvocationExpression(
                   IdentifierName(nameof(VariableDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Variables)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        // VariableDeclarator(Visit(node.Identifier).OrNullLiteralExpression(), Visit(node.ArgumentList).OrNullLiteralExpression(), Visit(node.Initializer).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(VariableDeclarator)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ArgumentList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Initializer).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitEqualsValueClause(EqualsValueClauseSyntax node)
    {
        // EqualsValueClause(Visit(node.EqualsToken)!, Visit(node.Value)!)
        return InvocationExpression(
                   IdentifierName(nameof(EqualsValueClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EqualsToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Value)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
    {
        // SingleVariableDesignation(Visit(node.Identifier)!)
        return InvocationExpression(
                   IdentifierName(nameof(SingleVariableDesignation)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitDiscardDesignation(DiscardDesignationSyntax node)
    {
        // DiscardDesignation(Visit(node.UnderscoreToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(DiscardDesignation)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.UnderscoreToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitParenthesizedVariableDesignation(ParenthesizedVariableDesignationSyntax node)
    {
        // ParenthesizedVariableDesignation(Visit(node.OpenParenToken)!, Visit(node.Variables)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ParenthesizedVariableDesignation)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Variables)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        // ExpressionStatement(Visit(node.AttributeLists)!, Visit(node.Expression)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ExpressionStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitEmptyStatement(EmptyStatementSyntax node)
    {
        // EmptyStatement(Visit(node.AttributeLists)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(EmptyStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitLabeledStatement(LabeledStatementSyntax node)
    {
        // LabeledStatement(Visit(node.AttributeLists)!, Visit(node.Identifier)!, Visit(node.ColonToken)!, Visit(node.Statement)!)
        return InvocationExpression(
                   IdentifierName(nameof(LabeledStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statement)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitGotoStatement(GotoStatementSyntax node)
    {
        // GotoStatement(Visit(node.Kind())!, Visit(node.AttributeLists)!, Visit(node.GotoKeyword)!, Visit(node.CaseOrDefaultKeyword)!, Visit(node.Expression).OrNullLiteralExpression(), Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(GotoStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.GotoKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CaseOrDefaultKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitBreakStatement(BreakStatementSyntax node)
    {
        // BreakStatement(Visit(node.AttributeLists)!, Visit(node.BreakKeyword)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(BreakStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.BreakKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitContinueStatement(ContinueStatementSyntax node)
    {
        // ContinueStatement(Visit(node.AttributeLists)!, Visit(node.ContinueKeyword)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ContinueStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ContinueKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitReturnStatement(ReturnStatementSyntax node)
    {
        // ReturnStatement(Visit(node.AttributeLists)!, Visit(node.ReturnKeyword)!, Visit(node.Expression).OrNullLiteralExpression(), Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ReturnStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ReturnKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitThrowStatement(ThrowStatementSyntax node)
    {
        // ThrowStatement(Visit(node.AttributeLists)!, Visit(node.ThrowKeyword)!, Visit(node.Expression).OrNullLiteralExpression(), Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ThrowStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ThrowKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitYieldStatement(YieldStatementSyntax node)
    {
        // YieldStatement(Visit(node.Kind())!, Visit(node.AttributeLists)!, Visit(node.YieldKeyword)!, Visit(node.ReturnOrBreakKeyword)!, Visit(node.Expression).OrNullLiteralExpression(), Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(YieldStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.YieldKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ReturnOrBreakKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitWhileStatement(WhileStatementSyntax node)
    {
        // WhileStatement(Visit(node.AttributeLists)!, Visit(node.WhileKeyword)!, Visit(node.OpenParenToken)!, Visit(node.Condition)!, Visit(node.CloseParenToken)!, Visit(node.Statement)!)
        return InvocationExpression(
                   IdentifierName(nameof(WhileStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WhileKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Condition)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statement)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitDoStatement(DoStatementSyntax node)
    {
        // DoStatement(Visit(node.AttributeLists)!, Visit(node.DoKeyword)!, Visit(node.Statement)!, Visit(node.WhileKeyword)!, Visit(node.OpenParenToken)!, Visit(node.Condition)!, Visit(node.CloseParenToken)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(DoStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.DoKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statement)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WhileKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Condition)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitForStatement(ForStatementSyntax node)
    {
        // ForStatement(Visit(node.AttributeLists)!, Visit(node.ForKeyword)!, Visit(node.OpenParenToken)!, Visit(node.Declaration).OrNullLiteralExpression(), Visit(node.Initializers)!, Visit(node.FirstSemicolonToken)!, Visit(node.Condition).OrNullLiteralExpression(), Visit(node.SecondSemicolonToken)!, Visit(node.Incrementors)!, Visit(node.CloseParenToken)!, Visit(node.Statement)!)
        return InvocationExpression(
                   IdentifierName(nameof(ForStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ForKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Declaration).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Initializers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.FirstSemicolonToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Condition).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SecondSemicolonToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Incrementors)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statement)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitForEachStatement(ForEachStatementSyntax node)
    {
        // ForEachStatement(Visit(node.AttributeLists)!, Visit(node.AwaitKeyword)!, Visit(node.ForEachKeyword)!, Visit(node.OpenParenToken)!, Visit(node.Type)!, Visit(node.Identifier)!, Visit(node.InKeyword)!, Visit(node.Expression)!, Visit(node.CloseParenToken)!, Visit(node.Statement)!)
        return InvocationExpression(
                   IdentifierName(nameof(ForEachStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AwaitKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ForEachKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.InKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statement)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        // ForEachVariableStatement(Visit(node.AttributeLists)!, Visit(node.AwaitKeyword)!, Visit(node.ForEachKeyword)!, Visit(node.OpenParenToken)!, Visit(node.Variable)!, Visit(node.InKeyword)!, Visit(node.Expression)!, Visit(node.CloseParenToken)!, Visit(node.Statement)!)
        return InvocationExpression(
                   IdentifierName(nameof(ForEachVariableStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AwaitKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ForEachKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Variable)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.InKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statement)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitUsingStatement(UsingStatementSyntax node)
    {
        // UsingStatement(Visit(node.AttributeLists)!, Visit(node.AwaitKeyword)!, Visit(node.UsingKeyword)!, Visit(node.OpenParenToken)!, Visit(node.Declaration).OrNullLiteralExpression(), Visit(node.Expression).OrNullLiteralExpression(), Visit(node.CloseParenToken)!, Visit(node.Statement)!)
        return InvocationExpression(
                   IdentifierName(nameof(UsingStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AwaitKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.UsingKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Declaration).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statement)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitFixedStatement(FixedStatementSyntax node)
    {
        // FixedStatement(Visit(node.AttributeLists)!, Visit(node.FixedKeyword)!, Visit(node.OpenParenToken)!, Visit(node.Declaration)!, Visit(node.CloseParenToken)!, Visit(node.Statement)!)
        return InvocationExpression(
                   IdentifierName(nameof(FixedStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.FixedKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Declaration)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statement)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitCheckedStatement(CheckedStatementSyntax node)
    {
        // CheckedStatement(Visit(node.Kind())!, Visit(node.AttributeLists)!, Visit(node.Keyword)!, Visit(node.Block)!)
        return InvocationExpression(
                   IdentifierName(nameof(CheckedStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Block)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitUnsafeStatement(UnsafeStatementSyntax node)
    {
        // UnsafeStatement(Visit(node.AttributeLists)!, Visit(node.UnsafeKeyword)!, Visit(node.Block)!)
        return InvocationExpression(
                   IdentifierName(nameof(UnsafeStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.UnsafeKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Block)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitLockStatement(LockStatementSyntax node)
    {
        // LockStatement(Visit(node.AttributeLists)!, Visit(node.LockKeyword)!, Visit(node.OpenParenToken)!, Visit(node.Expression)!, Visit(node.CloseParenToken)!, Visit(node.Statement)!)
        return InvocationExpression(
                   IdentifierName(nameof(LockStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LockKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statement)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitIfStatement(IfStatementSyntax node)
    {
        // IfStatement(Visit(node.AttributeLists)!, Visit(node.IfKeyword)!, Visit(node.OpenParenToken)!, Visit(node.Condition)!, Visit(node.CloseParenToken)!, Visit(node.Statement)!, Visit(node.Else).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(IfStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.IfKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Condition)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statement)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Else).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitElseClause(ElseClauseSyntax node)
    {
        // ElseClause(Visit(node.ElseKeyword)!, Visit(node.Statement)!)
        return InvocationExpression(
                   IdentifierName(nameof(ElseClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ElseKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statement)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitSwitchStatement(SwitchStatementSyntax node)
    {
        // SwitchStatement(Visit(node.AttributeLists)!, Visit(node.SwitchKeyword)!, Visit(node.OpenParenToken)!, Visit(node.Expression)!, Visit(node.CloseParenToken)!, Visit(node.OpenBraceToken)!, Visit(node.Sections)!, Visit(node.CloseBraceToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(SwitchStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SwitchKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Sections)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitSwitchSection(SwitchSectionSyntax node)
    {
        // SwitchSection(Visit(node.Labels)!, Visit(node.Statements)!)
        return InvocationExpression(
                   IdentifierName(nameof(SwitchSection)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Labels)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Statements)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitCasePatternSwitchLabel(CasePatternSwitchLabelSyntax node)
    {
        // CasePatternSwitchLabel(Visit(node.Keyword)!, Visit(node.Pattern)!, Visit(node.WhenClause).OrNullLiteralExpression(), Visit(node.ColonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(CasePatternSwitchLabel)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Pattern)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WhenClause).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
    {
        // CaseSwitchLabel(Visit(node.Keyword)!, Visit(node.Value)!, Visit(node.ColonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(CaseSwitchLabel)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Value)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitDefaultSwitchLabel(DefaultSwitchLabelSyntax node)
    {
        // DefaultSwitchLabel(Visit(node.Keyword)!, Visit(node.ColonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(DefaultSwitchLabel)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        // SwitchExpression(Visit(node.GoverningExpression)!, Visit(node.SwitchKeyword)!, Visit(node.OpenBraceToken)!, Visit(node.Arms)!, Visit(node.CloseBraceToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(SwitchExpression)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.GoverningExpression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SwitchKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Arms)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
    {
        // SwitchExpressionArm(Visit(node.Pattern)!, Visit(node.WhenClause).OrNullLiteralExpression(), Visit(node.EqualsGreaterThanToken)!, Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(SwitchExpressionArm)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Pattern)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WhenClause).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EqualsGreaterThanToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitTryStatement(TryStatementSyntax node)
    {
        // TryStatement(Visit(node.AttributeLists)!, Visit(node.TryKeyword)!, Visit(node.Block)!, Visit(node.Catches)!, Visit(node.Finally).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(TryStatement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TryKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Block)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Catches)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Finally).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitCatchClause(CatchClauseSyntax node)
    {
        // CatchClause(Visit(node.CatchKeyword)!, Visit(node.Declaration).OrNullLiteralExpression(), Visit(node.Filter).OrNullLiteralExpression(), Visit(node.Block)!)
        return InvocationExpression(
                   IdentifierName(nameof(CatchClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CatchKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Declaration).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Filter).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Block)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitCatchDeclaration(CatchDeclarationSyntax node)
    {
        // CatchDeclaration(Visit(node.OpenParenToken)!, Visit(node.Type)!, Visit(node.Identifier)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(CatchDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitCatchFilterClause(CatchFilterClauseSyntax node)
    {
        // CatchFilterClause(Visit(node.WhenKeyword)!, Visit(node.OpenParenToken)!, Visit(node.FilterExpression)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(CatchFilterClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WhenKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.FilterExpression)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitFinallyClause(FinallyClauseSyntax node)
    {
        // FinallyClause(Visit(node.FinallyKeyword)!, Visit(node.Block)!)
        return InvocationExpression(
                   IdentifierName(nameof(FinallyClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.FinallyKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Block)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitCompilationUnit(CompilationUnitSyntax node)
    {
        // CompilationUnit(Visit(node.Externs)!, Visit(node.Usings)!, Visit(node.AttributeLists)!, Visit(node.Members)!, Visit(node.EndOfFileToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(CompilationUnit)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Externs)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Usings)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Members)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfFileToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitExternAliasDirective(ExternAliasDirectiveSyntax node)
    {
        // ExternAliasDirective(Visit(node.ExternKeyword)!, Visit(node.AliasKeyword)!, Visit(node.Identifier)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ExternAliasDirective)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExternKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AliasKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        // UsingDirective(Visit(node.GlobalKeyword)!, Visit(node.UsingKeyword)!, Visit(node.StaticKeyword)!, Visit(node.UnsafeKeyword)!, Visit(node.Alias).OrNullLiteralExpression(), Visit(node.NamespaceOrType)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(UsingDirective)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.GlobalKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.UsingKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.StaticKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.UnsafeKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Alias).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NamespaceOrType)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        // NamespaceDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.NamespaceKeyword)!, Visit(node.Name)!, Visit(node.OpenBraceToken)!, Visit(node.Externs)!, Visit(node.Usings)!, Visit(node.Members)!, Visit(node.CloseBraceToken)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(NamespaceDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NamespaceKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Externs)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Usings)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Members)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
    {
        // FileScopedNamespaceDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.NamespaceKeyword)!, Visit(node.Name)!, Visit(node.SemicolonToken)!, Visit(node.Externs)!, Visit(node.Usings)!, Visit(node.Members)!)
        return InvocationExpression(
                   IdentifierName(nameof(FileScopedNamespaceDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NamespaceKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Externs)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Usings)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Members)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAttributeList(AttributeListSyntax node)
    {
        // AttributeList(Visit(node.OpenBracketToken)!, Visit(node.Target).OrNullLiteralExpression(), Visit(node.Attributes)!, Visit(node.CloseBracketToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(AttributeList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBracketToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Target).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Attributes)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBracketToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAttributeTargetSpecifier(AttributeTargetSpecifierSyntax node)
    {
        // AttributeTargetSpecifier(Visit(node.Identifier)!, Visit(node.ColonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(AttributeTargetSpecifier)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAttribute(AttributeSyntax node)
    {
        // Attribute(Visit(node.Name)!, Visit(node.ArgumentList).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(Attribute)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ArgumentList).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAttributeArgumentList(AttributeArgumentListSyntax node)
    {
        // AttributeArgumentList(Visit(node.OpenParenToken)!, Visit(node.Arguments)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(AttributeArgumentList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Arguments)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAttributeArgument(AttributeArgumentSyntax node)
    {
        // AttributeArgument(Visit(node.NameEquals).OrNullLiteralExpression(), Visit(node.NameColon).OrNullLiteralExpression(), Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(AttributeArgument)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NameEquals).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NameColon).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitNameEquals(NameEqualsSyntax node)
    {
        // NameEquals(Visit(node.Name)!, Visit(node.EqualsToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(NameEquals)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EqualsToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitTypeParameterList(TypeParameterListSyntax node)
    {
        // TypeParameterList(Visit(node.LessThanToken)!, Visit(node.Parameters)!, Visit(node.GreaterThanToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(TypeParameterList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LessThanToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Parameters)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.GreaterThanToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitTypeParameter(TypeParameterSyntax node)
    {
        // TypeParameter(Visit(node.AttributeLists)!, Visit(node.VarianceKeyword)!, Visit(node.Identifier)!)
        return InvocationExpression(
                   IdentifierName(nameof(TypeParameter)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.VarianceKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        // ClassDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers).OrNullLiteralExpression(), Visit(node.Keyword).OrNullLiteralExpression(), Visit(node.Identifier).OrNullLiteralExpression(), Visit(node.TypeParameterList).OrNullLiteralExpression(), Visit(node.ParameterList).OrNullLiteralExpression(), Visit(node.BaseList).OrNullLiteralExpression(), Visit(node.ConstraintClauses)!, Visit(node.OpenBraceToken).OrNullLiteralExpression(), Visit(node.Members)!, Visit(node.CloseBraceToken).OrNullLiteralExpression(), Visit(node.SemicolonToken).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(ClassDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TypeParameterList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.BaseList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ConstraintClauses)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Members)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitStructDeclaration(StructDeclarationSyntax node)
    {
        // StructDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers).OrNullLiteralExpression(), Visit(node.Keyword).OrNullLiteralExpression(), Visit(node.Identifier).OrNullLiteralExpression(), Visit(node.TypeParameterList).OrNullLiteralExpression(), Visit(node.ParameterList).OrNullLiteralExpression(), Visit(node.BaseList).OrNullLiteralExpression(), Visit(node.ConstraintClauses)!, Visit(node.OpenBraceToken).OrNullLiteralExpression(), Visit(node.Members)!, Visit(node.CloseBraceToken).OrNullLiteralExpression(), Visit(node.SemicolonToken).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(StructDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TypeParameterList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.BaseList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ConstraintClauses)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Members)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        // InterfaceDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers).OrNullLiteralExpression(), Visit(node.Keyword).OrNullLiteralExpression(), Visit(node.Identifier).OrNullLiteralExpression(), Visit(node.TypeParameterList).OrNullLiteralExpression(), Visit(node.ParameterList).OrNullLiteralExpression(), Visit(node.BaseList).OrNullLiteralExpression(), Visit(node.ConstraintClauses)!, Visit(node.OpenBraceToken).OrNullLiteralExpression(), Visit(node.Members)!, Visit(node.CloseBraceToken).OrNullLiteralExpression(), Visit(node.SemicolonToken).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(InterfaceDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TypeParameterList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.BaseList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ConstraintClauses)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Members)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        // RecordDeclaration(Visit(node.Kind()).OrNullLiteralExpression(), Visit(node.AttributeLists)!, Visit(node.Modifiers).OrNullLiteralExpression(), Visit(node.Keyword).OrNullLiteralExpression(), Visit(node.ClassOrStructKeyword).OrNullLiteralExpression(), Visit(node.Identifier).OrNullLiteralExpression(), Visit(node.TypeParameterList).OrNullLiteralExpression(), Visit(node.ParameterList).OrNullLiteralExpression(), Visit(node.BaseList).OrNullLiteralExpression(), Visit(node.ConstraintClauses)!, Visit(node.OpenBraceToken).OrNullLiteralExpression(), Visit(node.Members)!, Visit(node.CloseBraceToken).OrNullLiteralExpression(), Visit(node.SemicolonToken).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(RecordDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind()).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ClassOrStructKeyword).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TypeParameterList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.BaseList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ConstraintClauses)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Members)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        // EnumDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.EnumKeyword)!, Visit(node.Identifier)!, Visit(node.BaseList).OrNullLiteralExpression(), Visit(node.OpenBraceToken)!, Visit(node.Members)!, Visit(node.CloseBraceToken)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(EnumDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EnumKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.BaseList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Members)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitDelegateDeclaration(DelegateDeclarationSyntax node)
    {
        // DelegateDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.DelegateKeyword)!, Visit(node.ReturnType)!, Visit(node.Identifier)!, Visit(node.TypeParameterList).OrNullLiteralExpression(), Visit(node.ParameterList)!, Visit(node.ConstraintClauses)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(DelegateDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.DelegateKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ReturnType)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TypeParameterList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ConstraintClauses)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitEnumMemberDeclaration(EnumMemberDeclarationSyntax node)
    {
        // EnumMemberDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.Identifier)!, Visit(node.EqualsValue).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(EnumMemberDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EqualsValue).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitBaseList(BaseListSyntax node)
    {
        // BaseList(Visit(node.ColonToken)!, Visit(node.Types)!)
        return InvocationExpression(
                   IdentifierName(nameof(BaseList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Types)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitSimpleBaseType(SimpleBaseTypeSyntax node)
    {
        // SimpleBaseType(Visit(node.Type)!)
        return InvocationExpression(
                   IdentifierName(nameof(SimpleBaseType)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitPrimaryConstructorBaseType(PrimaryConstructorBaseTypeSyntax node)
    {
        // PrimaryConstructorBaseType(Visit(node.Type)!, Visit(node.ArgumentList)!)
        return InvocationExpression(
                   IdentifierName(nameof(PrimaryConstructorBaseType)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ArgumentList)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitTypeParameterConstraintClause(TypeParameterConstraintClauseSyntax node)
    {
        // TypeParameterConstraintClause(Visit(node.WhereKeyword)!, Visit(node.Name)!, Visit(node.ColonToken)!, Visit(node.Constraints)!)
        return InvocationExpression(
                   IdentifierName(nameof(TypeParameterConstraintClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WhereKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Constraints)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitConstructorConstraint(ConstructorConstraintSyntax node)
    {
        // ConstructorConstraint(Visit(node.NewKeyword)!, Visit(node.OpenParenToken)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ConstructorConstraint)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NewKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitClassOrStructConstraint(ClassOrStructConstraintSyntax node)
    {
        // ClassOrStructConstraint(Visit(node.Kind())!, Visit(node.ClassOrStructKeyword)!, Visit(node.QuestionToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ClassOrStructConstraint)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ClassOrStructKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.QuestionToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitTypeConstraint(TypeConstraintSyntax node)
    {
        // TypeConstraint(Visit(node.Type)!)
        return InvocationExpression(
                   IdentifierName(nameof(TypeConstraint)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitDefaultConstraint(DefaultConstraintSyntax node)
    {
        // DefaultConstraint(Visit(node.DefaultKeyword)!)
        return InvocationExpression(
                   IdentifierName(nameof(DefaultConstraint)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.DefaultKeyword)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        // FieldDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.Declaration)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(FieldDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Declaration)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
    {
        // EventFieldDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.EventKeyword)!, Visit(node.Declaration)!, Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(EventFieldDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EventKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Declaration)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitExplicitInterfaceSpecifier(ExplicitInterfaceSpecifierSyntax node)
    {
        // ExplicitInterfaceSpecifier(Visit(node.Name)!, Visit(node.DotToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ExplicitInterfaceSpecifier)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.DotToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        // MethodDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers).OrNullLiteralExpression(), Visit(node.ReturnType)!, Visit(node.ExplicitInterfaceSpecifier).OrNullLiteralExpression(), Visit(node.Identifier).OrNullLiteralExpression(), Visit(node.TypeParameterList).OrNullLiteralExpression(), Visit(node.ParameterList)!, Visit(node.ConstraintClauses)!, Visit(node.Body).OrNullLiteralExpression(), Visit(node.ExpressionBody).OrNullLiteralExpression(), Visit(node.SemicolonToken).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(MethodDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ReturnType)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExplicitInterfaceSpecifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TypeParameterList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ConstraintClauses)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Body).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExpressionBody).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        // OperatorDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.ReturnType)!, Visit(node.ExplicitInterfaceSpecifier).OrNullLiteralExpression(), Visit(node.OperatorKeyword)!, Visit(node.CheckedKeyword)!, Visit(node.OperatorToken)!, Visit(node.ParameterList)!, Visit(node.Body).OrNullLiteralExpression(), Visit(node.ExpressionBody).OrNullLiteralExpression(), Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(OperatorDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ReturnType)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExplicitInterfaceSpecifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CheckedKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Body).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExpressionBody).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        // ConversionOperatorDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.ImplicitOrExplicitKeyword)!, Visit(node.ExplicitInterfaceSpecifier).OrNullLiteralExpression(), Visit(node.OperatorKeyword)!, Visit(node.CheckedKeyword)!, Visit(node.Type)!, Visit(node.ParameterList)!, Visit(node.Body).OrNullLiteralExpression(), Visit(node.ExpressionBody).OrNullLiteralExpression(), Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ConversionOperatorDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ImplicitOrExplicitKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExplicitInterfaceSpecifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CheckedKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Body).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExpressionBody).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        // ConstructorDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers).OrNullLiteralExpression(), Visit(node.Identifier).OrNullLiteralExpression(), Visit(node.ParameterList)!, Visit(node.Initializer).OrNullLiteralExpression(), Visit(node.Body).OrNullLiteralExpression(), Visit(node.ExpressionBody).OrNullLiteralExpression(), Visit(node.SemicolonToken).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(ConstructorDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Initializer).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Body).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExpressionBody).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitConstructorInitializer(ConstructorInitializerSyntax node)
    {
        // ConstructorInitializer(Visit(node.Kind())!, Visit(node.ColonToken)!, Visit(node.ThisOrBaseKeyword)!, Visit(node.ArgumentList)!)
        return InvocationExpression(
                   IdentifierName(nameof(ConstructorInitializer)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ThisOrBaseKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ArgumentList)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitDestructorDeclaration(DestructorDeclarationSyntax node)
    {
        // DestructorDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.TildeToken)!, Visit(node.Identifier)!, Visit(node.ParameterList)!, Visit(node.Body).OrNullLiteralExpression(), Visit(node.ExpressionBody).OrNullLiteralExpression(), Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(DestructorDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TildeToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Body).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExpressionBody).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        // PropertyDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers).OrNullLiteralExpression(), Visit(node.Type)!, Visit(node.ExplicitInterfaceSpecifier).OrNullLiteralExpression(), Visit(node.Identifier).OrNullLiteralExpression(), Visit(node.AccessorList).OrNullLiteralExpression(), Visit(node.ExpressionBody).OrNullLiteralExpression(), Visit(node.Initializer).OrNullLiteralExpression(), Visit(node.SemicolonToken).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(PropertyDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExplicitInterfaceSpecifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AccessorList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExpressionBody).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Initializer).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
    {
        // ArrowExpressionClause(Visit(node.ArrowToken)!, Visit(node.Expression)!)
        return InvocationExpression(
                   IdentifierName(nameof(ArrowExpressionClause)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ArrowToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Expression)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitEventDeclaration(EventDeclarationSyntax node)
    {
        // EventDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.EventKeyword)!, Visit(node.Type)!, Visit(node.ExplicitInterfaceSpecifier).OrNullLiteralExpression(), Visit(node.Identifier)!, Visit(node.AccessorList).OrNullLiteralExpression(), Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(EventDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EventKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExplicitInterfaceSpecifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AccessorList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitIndexerDeclaration(IndexerDeclarationSyntax node)
    {
        // IndexerDeclaration(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.Type)!, Visit(node.ExplicitInterfaceSpecifier).OrNullLiteralExpression(), Visit(node.ThisKeyword)!, Visit(node.ParameterList)!, Visit(node.AccessorList).OrNullLiteralExpression(), Visit(node.ExpressionBody).OrNullLiteralExpression(), Visit(node.SemicolonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(IndexerDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExplicitInterfaceSpecifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ThisKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ParameterList)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AccessorList).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExpressionBody).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAccessorList(AccessorListSyntax node)
    {
        // AccessorList(Visit(node.OpenBraceToken)!, Visit(node.Accessors)!, Visit(node.CloseBraceToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(AccessorList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBraceToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Accessors)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBraceToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitAccessorDeclaration(AccessorDeclarationSyntax node)
    {
        // AccessorDeclaration(Visit(node.Kind()).OrNullLiteralExpression(), Visit(node.AttributeLists)!, Visit(node.Modifiers).OrNullLiteralExpression(), Visit(node.Keyword).OrNullLiteralExpression(), Visit(node.Body).OrNullLiteralExpression(), Visit(node.ExpressionBody).OrNullLiteralExpression(), Visit(node.SemicolonToken).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(AccessorDeclaration)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind()).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Keyword).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Body).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExpressionBody).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SemicolonToken).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitParameterList(ParameterListSyntax node)
    {
        // ParameterList(Visit(node.OpenParenToken)!, Visit(node.Parameters)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(ParameterList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Parameters)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitBracketedParameterList(BracketedParameterListSyntax node)
    {
        // BracketedParameterList(Visit(node.OpenBracketToken)!, Visit(node.Parameters)!, Visit(node.CloseBracketToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(BracketedParameterList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBracketToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Parameters)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBracketToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitParameter(ParameterSyntax node)
    {
        // Parameter(Visit(node.AttributeLists)!, Visit(node.Modifiers).OrNullLiteralExpression(), Visit(node.Type).OrNullLiteralExpression(), Visit(node.Identifier).OrNullLiteralExpression(), Visit(node.Default).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(Parameter)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Default).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitFunctionPointerParameter(FunctionPointerParameterSyntax node)
    {
        // FunctionPointerParameter(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.Type)!)
        return InvocationExpression(
                   IdentifierName(nameof(FunctionPointerParameter)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitIncompleteMember(IncompleteMemberSyntax node)
    {
        // IncompleteMember(Visit(node.AttributeLists)!, Visit(node.Modifiers)!, Visit(node.Type).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(IncompleteMember)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.AttributeLists)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Modifiers)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitSkippedTokensTrivia(SkippedTokensTriviaSyntax node)
    {
        // SkippedTokensTrivia(Visit(node.Tokens)!)
        return InvocationExpression(
                   IdentifierName(nameof(SkippedTokensTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Tokens)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitDocumentationCommentTrivia(DocumentationCommentTriviaSyntax node)
    {
        // DocumentationCommentTrivia(Visit(node.Kind())!, Visit(node.Content)!, Visit(node.EndOfComment)!)
        return InvocationExpression(
                   IdentifierName(nameof(DocumentationCommentTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Kind())!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Content)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfComment)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitTypeCref(TypeCrefSyntax node)
    {
        // TypeCref(Visit(node.Type)!)
        return InvocationExpression(
                   IdentifierName(nameof(TypeCref)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitQualifiedCref(QualifiedCrefSyntax node)
    {
        // QualifiedCref(Visit(node.Container)!, Visit(node.DotToken)!, Visit(node.Member)!)
        return InvocationExpression(
                   IdentifierName(nameof(QualifiedCref)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Container)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.DotToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Member)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitNameMemberCref(NameMemberCrefSyntax node)
    {
        // NameMemberCref(Visit(node.Name)!, Visit(node.Parameters).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(NameMemberCref)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Parameters).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitIndexerMemberCref(IndexerMemberCrefSyntax node)
    {
        // IndexerMemberCref(Visit(node.ThisKeyword)!, Visit(node.Parameters).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(IndexerMemberCref)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ThisKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Parameters).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitOperatorMemberCref(OperatorMemberCrefSyntax node)
    {
        // OperatorMemberCref(Visit(node.OperatorKeyword)!, Visit(node.CheckedKeyword)!, Visit(node.OperatorToken)!, Visit(node.Parameters).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(OperatorMemberCref)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CheckedKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Parameters).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitConversionOperatorMemberCref(ConversionOperatorMemberCrefSyntax node)
    {
        // ConversionOperatorMemberCref(Visit(node.ImplicitOrExplicitKeyword)!, Visit(node.OperatorKeyword)!, Visit(node.CheckedKeyword)!, Visit(node.Type)!, Visit(node.Parameters).OrNullLiteralExpression())
        return InvocationExpression(
                   IdentifierName(nameof(ConversionOperatorMemberCref)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ImplicitOrExplicitKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OperatorKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CheckedKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Parameters).OrNullLiteralExpression()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitCrefParameterList(CrefParameterListSyntax node)
    {
        // CrefParameterList(Visit(node.OpenParenToken)!, Visit(node.Parameters)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(CrefParameterList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Parameters)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitCrefBracketedParameterList(CrefBracketedParameterListSyntax node)
    {
        // CrefBracketedParameterList(Visit(node.OpenBracketToken)!, Visit(node.Parameters)!, Visit(node.CloseBracketToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(CrefBracketedParameterList)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenBracketToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Parameters)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseBracketToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitCrefParameter(CrefParameterSyntax node)
    {
        // CrefParameter(Visit(node.RefKindKeyword)!, Visit(node.Type)!)
        return InvocationExpression(
                   IdentifierName(nameof(CrefParameter)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.RefKindKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Type)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitXmlElement(XmlElementSyntax node)
    {
        // XmlElement(Visit(node.StartTag)!, Visit(node.Content)!, Visit(node.EndTag)!)
        return InvocationExpression(
                   IdentifierName(nameof(XmlElement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.StartTag)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Content)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndTag)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitXmlElementStartTag(XmlElementStartTagSyntax node)
    {
        // XmlElementStartTag(Visit(node.LessThanToken)!, Visit(node.Name)!, Visit(node.Attributes)!, Visit(node.GreaterThanToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(XmlElementStartTag)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LessThanToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Attributes)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.GreaterThanToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitXmlElementEndTag(XmlElementEndTagSyntax node)
    {
        // XmlElementEndTag(Visit(node.LessThanSlashToken)!, Visit(node.Name)!, Visit(node.GreaterThanToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(XmlElementEndTag)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LessThanSlashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.GreaterThanToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitXmlEmptyElement(XmlEmptyElementSyntax node)
    {
        // XmlEmptyElement(Visit(node.LessThanToken)!, Visit(node.Name)!, Visit(node.Attributes)!, Visit(node.SlashGreaterThanToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(XmlEmptyElement)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LessThanToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Attributes)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SlashGreaterThanToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitXmlName(XmlNameSyntax node)
    {
        // XmlName(Visit(node.Prefix).OrNullLiteralExpression(), Visit(node.LocalName)!)
        return InvocationExpression(
                   IdentifierName(nameof(XmlName)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Prefix).OrNullLiteralExpression()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LocalName)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitXmlPrefix(XmlPrefixSyntax node)
    {
        // XmlPrefix(Visit(node.Prefix)!, Visit(node.ColonToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(XmlPrefix)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Prefix)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ColonToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitXmlTextAttribute(XmlTextAttributeSyntax node)
    {
        // XmlTextAttribute(Visit(node.Name)!, Visit(node.EqualsToken)!, Visit(node.StartQuoteToken)!, Visit(node.TextTokens)!, Visit(node.EndQuoteToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(XmlTextAttribute)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EqualsToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.StartQuoteToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TextTokens)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndQuoteToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitXmlCrefAttribute(XmlCrefAttributeSyntax node)
    {
        // XmlCrefAttribute(Visit(node.Name)!, Visit(node.EqualsToken)!, Visit(node.StartQuoteToken)!, Visit(node.Cref)!, Visit(node.EndQuoteToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(XmlCrefAttribute)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EqualsToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.StartQuoteToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Cref)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndQuoteToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitXmlNameAttribute(XmlNameAttributeSyntax node)
    {
        // XmlNameAttribute(Visit(node.Name)!, Visit(node.EqualsToken)!, Visit(node.StartQuoteToken)!, Visit(node.Identifier)!, Visit(node.EndQuoteToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(XmlNameAttribute)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EqualsToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.StartQuoteToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndQuoteToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitXmlText(XmlTextSyntax node)
    {
        // XmlText(Visit(node.TextTokens)!)
        return InvocationExpression(
                   IdentifierName(nameof(XmlText)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TextTokens)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitXmlCDataSection(XmlCDataSectionSyntax node)
    {
        // XmlCDataSection(Visit(node.StartCDataToken)!, Visit(node.TextTokens)!, Visit(node.EndCDataToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(XmlCDataSection)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.StartCDataToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TextTokens)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndCDataToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitXmlProcessingInstruction(XmlProcessingInstructionSyntax node)
    {
        // XmlProcessingInstruction(Visit(node.StartProcessingInstructionToken)!, Visit(node.Name)!, Visit(node.TextTokens)!, Visit(node.EndProcessingInstructionToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(XmlProcessingInstruction)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.StartProcessingInstructionToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TextTokens)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndProcessingInstructionToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitXmlComment(XmlCommentSyntax node)
    {
        // XmlComment(Visit(node.LessThanExclamationMinusMinusToken)!, Visit(node.TextTokens)!, Visit(node.MinusMinusGreaterThanToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(XmlComment)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LessThanExclamationMinusMinusToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TextTokens)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.MinusMinusGreaterThanToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitIfDirectiveTrivia(IfDirectiveTriviaSyntax node)
    {
        // IfDirectiveTrivia(Visit(node.HashToken)!, Visit(node.IfKeyword)!, Visit(node.Condition)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax(), node.BranchTaken.ToSyntax(), node.ConditionValue.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(IfDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.IfKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Condition)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.BranchTaken.ToSyntax()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.ConditionValue.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitElifDirectiveTrivia(ElifDirectiveTriviaSyntax node)
    {
        // ElifDirectiveTrivia(Visit(node.HashToken)!, Visit(node.ElifKeyword)!, Visit(node.Condition)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax(), node.BranchTaken.ToSyntax(), node.ConditionValue.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(ElifDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ElifKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Condition)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.BranchTaken.ToSyntax()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.ConditionValue.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitElseDirectiveTrivia(ElseDirectiveTriviaSyntax node)
    {
        // ElseDirectiveTrivia(Visit(node.HashToken)!, Visit(node.ElseKeyword)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax(), node.BranchTaken.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(ElseDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ElseKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.BranchTaken.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitEndIfDirectiveTrivia(EndIfDirectiveTriviaSyntax node)
    {
        // EndIfDirectiveTrivia(Visit(node.HashToken)!, Visit(node.EndIfKeyword)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(EndIfDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndIfKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitRegionDirectiveTrivia(RegionDirectiveTriviaSyntax node)
    {
        // RegionDirectiveTrivia(Visit(node.HashToken)!, Visit(node.RegionKeyword)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(RegionDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.RegionKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitEndRegionDirectiveTrivia(EndRegionDirectiveTriviaSyntax node)
    {
        // EndRegionDirectiveTrivia(Visit(node.HashToken)!, Visit(node.EndRegionKeyword)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(EndRegionDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndRegionKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitErrorDirectiveTrivia(ErrorDirectiveTriviaSyntax node)
    {
        // ErrorDirectiveTrivia(Visit(node.HashToken)!, Visit(node.ErrorKeyword)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(ErrorDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ErrorKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitWarningDirectiveTrivia(WarningDirectiveTriviaSyntax node)
    {
        // WarningDirectiveTrivia(Visit(node.HashToken)!, Visit(node.WarningKeyword)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(WarningDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WarningKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitBadDirectiveTrivia(BadDirectiveTriviaSyntax node)
    {
        // BadDirectiveTrivia(Visit(node.HashToken)!, Visit(node.Identifier)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(BadDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Identifier)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitDefineDirectiveTrivia(DefineDirectiveTriviaSyntax node)
    {
        // DefineDirectiveTrivia(Visit(node.HashToken)!, Visit(node.DefineKeyword)!, Visit(node.Name)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(DefineDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.DefineKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitUndefDirectiveTrivia(UndefDirectiveTriviaSyntax node)
    {
        // UndefDirectiveTrivia(Visit(node.HashToken)!, Visit(node.UndefKeyword)!, Visit(node.Name)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(UndefDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.UndefKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Name)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitLineDirectiveTrivia(LineDirectiveTriviaSyntax node)
    {
        // LineDirectiveTrivia(Visit(node.HashToken)!, Visit(node.LineKeyword)!, Visit(node.Line)!, Visit(node.File)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(LineDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LineKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Line)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.File)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitLineDirectivePosition(LineDirectivePositionSyntax node)
    {
        // LineDirectivePosition(Visit(node.OpenParenToken)!, Visit(node.Line)!, Visit(node.CommaToken)!, Visit(node.Character)!, Visit(node.CloseParenToken)!)
        return InvocationExpression(
                   IdentifierName(nameof(LineDirectivePosition)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.OpenParenToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Line)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CommaToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Character)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CloseParenToken)!) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitLineSpanDirectiveTrivia(LineSpanDirectiveTriviaSyntax node)
    {
        // LineSpanDirectiveTrivia(Visit(node.HashToken)!, Visit(node.LineKeyword)!, Visit(node.Start)!, Visit(node.MinusToken)!, Visit(node.End)!, Visit(node.CharacterOffset)!, Visit(node.File)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(LineSpanDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LineKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Start)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.MinusToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.End)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.CharacterOffset)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.File)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitPragmaWarningDirectiveTrivia(PragmaWarningDirectiveTriviaSyntax node)
    {
        // PragmaWarningDirectiveTrivia(Visit(node.HashToken)!, Visit(node.PragmaKeyword)!, Visit(node.WarningKeyword)!, Visit(node.DisableOrRestoreKeyword)!, Visit(node.ErrorCodes)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(PragmaWarningDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.PragmaKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.WarningKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.DisableOrRestoreKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ErrorCodes)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitPragmaChecksumDirectiveTrivia(PragmaChecksumDirectiveTriviaSyntax node)
    {
        // PragmaChecksumDirectiveTrivia(Visit(node.HashToken)!, Visit(node.PragmaKeyword)!, Visit(node.ChecksumKeyword)!, Visit(node.File)!, Visit(node.Guid)!, Visit(node.Bytes)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(PragmaChecksumDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.PragmaKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ChecksumKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.File)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Guid)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.Bytes)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitReferenceDirectiveTrivia(ReferenceDirectiveTriviaSyntax node)
    {
        // ReferenceDirectiveTrivia(Visit(node.HashToken)!, Visit(node.ReferenceKeyword)!, Visit(node.File)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(ReferenceDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ReferenceKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.File)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitLoadDirectiveTrivia(LoadDirectiveTriviaSyntax node)
    {
        // LoadDirectiveTrivia(Visit(node.HashToken)!, Visit(node.LoadKeyword)!, Visit(node.File)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(LoadDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.LoadKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.File)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitShebangDirectiveTrivia(ShebangDirectiveTriviaSyntax node)
    {
        // ShebangDirectiveTrivia(Visit(node.HashToken)!, Visit(node.ExclamationToken)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(ShebangDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.ExclamationToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }

    public override ExpressionSyntax? VisitNullableDirectiveTrivia(NullableDirectiveTriviaSyntax node)
    {
        // NullableDirectiveTrivia(Visit(node.HashToken)!, Visit(node.NullableKeyword)!, Visit(node.SettingToken)!, Visit(node.TargetToken)!, Visit(node.EndOfDirectiveToken)!, node.IsActive.ToSyntax())
        return InvocationExpression(
                   IdentifierName(nameof(NullableDirectiveTrivia)), 
                   ArgumentList(
                       Token(OpenParenToken), 
                       SeparatedList<ArgumentSyntax>(
                           new SyntaxNodeOrToken[] { 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.HashToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.NullableKeyword)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.SettingToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.TargetToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   Visit(node.EndOfDirectiveToken)!), 
                               Token(CommaToken), 
                               Argument(
                                   null, 
                                   Token(None), 
                                   node.IsActive.ToSyntax()) }), 
                       Token(CloseParenToken)));
    }
}