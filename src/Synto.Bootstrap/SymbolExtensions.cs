using Microsoft.CodeAnalysis;

namespace Synto.Bootstrap;

internal static class SymbolExtensions
{
    public static bool IsPrimitive(this ITypeSymbol type)
    {
        return type.SpecialType switch
        {
            SpecialType.System_Boolean
                or SpecialType.System_SByte
                or SpecialType.System_Int16
                or SpecialType.System_Int32
                or SpecialType.System_Int64
                or SpecialType.System_Byte
                or SpecialType.System_UInt16
                or SpecialType.System_UInt32
                or SpecialType.System_UInt64
                or SpecialType.System_Single
                or SpecialType.System_Double
                or SpecialType.System_Char
                or SpecialType.System_String
                or SpecialType.System_Object => true,
            _ => false,
        };
    }
}