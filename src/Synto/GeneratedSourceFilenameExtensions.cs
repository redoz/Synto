using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Synto;

public static class GeneratedSourceFilenameExtensions
{
    private static readonly SymbolDisplayFormat SymbolDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.OmittedAsContaining,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        propertyStyle: SymbolDisplayPropertyStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        memberOptions:
        SymbolDisplayMemberOptions.IncludeContainingType |
        SymbolDisplayMemberOptions.IncludeExplicitInterface,
        parameterOptions:
        SymbolDisplayParameterOptions.None,
        miscellaneousOptions:
        SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
    );
    
    public static string ToGeneratedFilename(this ISymbol symbol)
    {
        if (symbol is null) throw new ArgumentNullException(nameof(symbol));

        return symbol.ToDisplayString(SymbolDisplayFormat) + ".g.cs";
    }
}