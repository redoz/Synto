using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Synto.Test.Templating;

/// <summary>
/// Direct unit tests (via IVT) for the <c>SY1035</c> "Live template code" Info descriptor and its
/// <c>TemplateDiagnostics.LiveFragment</c> factory (plan Task 1). SY1035 is the first
/// Info-severity Synto diagnostic and lives in the new <c>Synto.Staging</c> category. No tap, no behavior change —
/// this only pins the descriptor shape and the location round-trip.
/// </summary>
public class LiveFragmentDiagnosticTest
{
    private static Location SomeLocation()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { int F; }");
        var field = tree.GetRoot().DescendantNodes().First(n => n is Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax);
        return field.GetLocation();
    }

    [Fact]
    public void Descriptor_HasSY1035InfoStagingShape()
    {
        var info = TemplateDiagnostics.LiveFragment(SomeLocation());
        var descriptor = info.Descriptor;

        Assert.Equal("SY1035", descriptor.Id);
        Assert.Equal(DiagnosticSeverity.Info, descriptor.DefaultSeverity);
        Assert.Equal("Synto.Staging", descriptor.Category);
        Assert.True(descriptor.IsEnabledByDefault);
        Assert.Empty(descriptor.CustomTags);
    }

    [Fact]
    public void LiveFragment_LocationRoundTrips()
    {
        var location = SomeLocation();
        var info = TemplateDiagnostics.LiveFragment(location);

        var diag = info.ToDiagnostic();
        Assert.Equal("SY1035", diag.Id);
        Assert.Equal(location.SourceSpan, diag.Location.SourceSpan);
    }
}
