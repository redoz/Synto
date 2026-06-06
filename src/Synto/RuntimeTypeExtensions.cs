using System;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Synto;

public static class RuntimeTypeExtensions
{
    /// <summary>
    /// Converts a runtime <see cref="Type"/> into the <see cref="TypeSyntax"/> that represents it in
    /// C# source. Unlike <c>ParseTypeName(type.FullName)</c>, this correctly handles closed generic
    /// types, arrays and nested types (whose reflection names are not valid C#).
    /// </summary>
    public static TypeSyntax ToTypeSyntax(this Type type)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));

        return SF.ParseTypeName(GetCSharpName(type));
    }

    private static string GetCSharpName(Type type)
    {
        if (type.IsArray)
        {
            int rank = type.GetArrayRank();
            string commas = rank > 1 ? new string(',', rank - 1) : string.Empty;
            return GetCSharpName(type.GetElementType()!) + "[" + commas + "]";
        }

        // by-ref and pointer types can't appear as a type argument, fall back to the element type
        if (type.IsByRef || type.IsPointer)
            return GetCSharpName(type.GetElementType()!);

        // a generic parameter (T) has no namespace or full name; render it by its bare name
        if (type.IsGenericParameter)
            return type.Name;

        // GetGenericArguments() flattens the arguments of the whole nesting chain (outermost first),
        // so we walk the chain and let each level render the slice of arguments it owns.
        Type[] genericArgs = type.IsGenericType ? type.GetGenericArguments() : Type.EmptyTypes;
        return BuildNestedName(type, genericArgs, genericArgs.Length);
    }

    private static string BuildNestedName(Type type, Type[] args, int argCount)
    {
        Type? declaring = type.DeclaringType;

        // GetGenericArguments() is cumulative, so the declaring type's arity is exactly the number of
        // arguments owned by the enclosing chain; this level owns args[outerArity..argCount].
        int outerArity = declaring is { IsGenericType: true } ? declaring.GetGenericArguments().Length : 0;

        string prefix = declaring is not null
            ? BuildNestedName(declaring, args, outerArity) + "."
            : string.IsNullOrEmpty(type.Namespace) ? string.Empty : type.Namespace + ".";

        // strip the arity marker (e.g. "List`1" -> "List")
        string name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0)
            name = name.Substring(0, tick);

        var builder = new StringBuilder(prefix);
        builder.Append(name);

        if (argCount > outerArity)
        {
            builder.Append('<');
            for (int i = outerArity; i < argCount; i++)
            {
                if (i > outerArity)
                    builder.Append(", ");
                builder.Append(GetCSharpName(args[i]));
            }

            builder.Append('>');
        }

        return builder.ToString();
    }
}
