using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Synto.Diagnostics;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class DiagnosticAttribute(
    string id,
    string title,
    string messageFormat,
    string category,
    DiagnosticSeverity defaultSeverity,
    bool isEnabledByDefault,
    string? description = null,
    string? helpLinkUri = null,
    params string[] customTags)
    : Attribute
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string MessageFormat { get; } = messageFormat;
    public string Category { get; } = category;
    public DiagnosticSeverity DefaultSeverity { get; } = defaultSeverity;
    public bool IsEnabledByDefault { get; } = isEnabledByDefault;
    public string? Description { get; } = description;
    public string? HelpLinkUri { get; } = helpLinkUri;
    public IReadOnlyList<string> CustomTags { get; } = customTags;
}