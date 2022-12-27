using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synto.CodeAnalysis
{
    public static class AttributeSyntaxExtensions
    {
        public static object?[] GetConstructorArguments(this AttributeSyntax attribute, SemanticModel semanticModel)
        {
            if (attribute.ArgumentList?.Arguments is not {} arguments)
                return Array.Empty<object>();

            return arguments.Where(arg => arg.NameEquals is null)
                .Select(arg => semanticModel.GetConstantValue(arg.Expression).Value)
                .ToArray();
        }

        public static T1 GetConstructorArguments<T1>(this AttributeSyntax attribute, SemanticModel semanticModel)
        {
            object?[] ret = attribute.GetConstructorArguments(semanticModel);
            if (ret.Length < 1)
                throw new InvalidOperationException("AttributeSyntax only has {ret.Length} arguments specified");

            return (T1)ret[0]!;
        }

        public static (T1?, T2?) GetConstructorArguments<T1, T2>(this AttributeSyntax attribute, SemanticModel semanticModel)
        {
            object?[] ret = attribute.GetConstructorArguments(semanticModel);
            if (ret.Length < 2)
                throw new InvalidOperationException("AttributeSyntax only has {ret.Length} arguments specified");

            return ((T1) ret[0]!, (T2) ret[0]!);
        }

        public static Optional<T> GetNamedArgument<T>(this AttributeSyntax attribute, string name, SemanticModel semanticModel)
        {
            if (attribute.ArgumentList?.Arguments is not { } arguments)
                return new Optional<T>();

            var arg = arguments.SingleOrDefault(arg => arg.NameEquals is { Name: { Identifier: { Text: var propertyName} }} && propertyName == name);
            
            if (arg is null || semanticModel.GetConstantValue(arg.Expression) is var value && !value.HasValue)
                return new Optional<T>();
            
            return new Optional<T>((T)value.Value!);
        }
    }
}
