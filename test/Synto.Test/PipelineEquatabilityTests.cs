using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Synto.Test;

/// <summary>
/// Unit tests for the equatable value types that flow through (or back the) templating incremental
/// pipeline. Structural equality is the load-bearing property: if these don't compare by value, Roslyn
/// can't short-circuit unchanged stages and the generator re-runs on every keystroke.
/// </summary>
public class PipelineEquatabilityTests
{
    // ----- EquatableArray<T> -----------------------------------------------------------------------

    [Fact]
    public void EquatableArrayEqualByElementNotByReference()
    {
        // distinct backing arrays, identical contents
        var a = new EquatableArray<string>(ImmutableArray.Create("one", "two", "three"));
        var b = new EquatableArray<string>(ImmutableArray.Create("one", "two", "three"));

        Assert.True(a.Equals(b));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void EquatableArrayDifferentElementsAreUnequal()
    {
        var a = new EquatableArray<string>(ImmutableArray.Create("one", "two"));
        var b = new EquatableArray<string>(ImmutableArray.Create("one", "TWO"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EquatableArrayDifferentLengthAreUnequal()
    {
        var a = new EquatableArray<string>(ImmutableArray.Create("one", "two"));
        var b = new EquatableArray<string>(ImmutableArray.Create("one"));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EquatableArrayDefaultAndEmptyAreEqual()
    {
        var fromDefault = default(EquatableArray<string>);
        var fromEmpty = new EquatableArray<string>(ImmutableArray<string>.Empty);

        Assert.Equal(fromDefault, fromEmpty);
    }

    // ----- LocationInfo ----------------------------------------------------------------------------

    [Fact]
    public void LocationInfoEqualByValue()
    {
        var a = new LocationInfo("File.cs", new TextSpan(3, 7), new LinePositionSpan(new LinePosition(0, 3), new LinePosition(0, 10)));
        var b = new LocationInfo("File.cs", new TextSpan(3, 7), new LinePositionSpan(new LinePosition(0, 3), new LinePosition(0, 10)));

        Assert.Equal(a, b);
    }

    [Fact]
    public void LocationInfoDifferentSpanAreUnequal()
    {
        var a = new LocationInfo("File.cs", new TextSpan(3, 7), new LinePositionSpan(new LinePosition(0, 3), new LinePosition(0, 10)));
        var b = new LocationInfo("File.cs", new TextSpan(4, 7), new LinePositionSpan(new LinePosition(0, 3), new LinePosition(0, 10)));

        Assert.NotEqual(a, b);
    }

    // ----- DiagnosticInfo --------------------------------------------------------------------------

    private static readonly DiagnosticDescriptor SampleDescriptor = new(
        "SY9999", "title", "message {0}", "category", DiagnosticSeverity.Error, isEnabledByDefault: true);

    [Fact]
    public void DiagnosticInfoEqualByValue()
    {
        var location = new LocationInfo("File.cs", new TextSpan(1, 2), new LinePositionSpan(new LinePosition(0, 1), new LinePosition(0, 3)));

        var a = new DiagnosticInfo(SampleDescriptor, location, new EquatableArray<string>(ImmutableArray.Create("arg")));
        var b = new DiagnosticInfo(SampleDescriptor, location, new EquatableArray<string>(ImmutableArray.Create("arg")));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DiagnosticInfoDifferentMessageArgsAreUnequal()
    {
        var a = new DiagnosticInfo(SampleDescriptor, Location: null, new EquatableArray<string>(ImmutableArray.Create("arg")));
        var b = new DiagnosticInfo(SampleDescriptor, Location: null, new EquatableArray<string>(ImmutableArray.Create("other")));

        Assert.NotEqual(a, b);
    }

    // ----- TemplateGenerationResult (the value that actually flows through the pipeline) ------------

    [Fact]
    public void TemplateGenerationResultEqualByValueDrivenByDiagnosticArray()
    {
        // distinct DiagnosticInfo arrays with identical content => the wrapping results must compare equal,
        // which is exactly what lets the pipeline cache an unchanged template.
        var diagA = new EquatableArray<DiagnosticInfo>(ImmutableArray.Create(
            new DiagnosticInfo(SampleDescriptor, Location: null, new EquatableArray<string>(ImmutableArray.Create("x")))));
        var diagB = new EquatableArray<DiagnosticInfo>(ImmutableArray.Create(
            new DiagnosticInfo(SampleDescriptor, Location: null, new EquatableArray<string>(ImmutableArray.Create("x")))));

        var a = new TemplateGenerationResult("File.g.cs", "source", diagA);
        var b = new TemplateGenerationResult("File.g.cs", "source", diagB);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void TemplateGenerationResultDifferentSourceAreUnequal()
    {
        var empty = EquatableArray<DiagnosticInfo>.Empty;

        var a = new TemplateGenerationResult("File.g.cs", "source one", empty);
        var b = new TemplateGenerationResult("File.g.cs", "source two", empty);

        Assert.NotEqual(a, b);
    }
}
