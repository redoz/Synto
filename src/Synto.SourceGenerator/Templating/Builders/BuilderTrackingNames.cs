namespace Synto.Generators.Templating;

/// <summary>
/// Names attached to the <see cref="SyntaxBuilderFacadeGenerator"/>'s incremental pipeline stages via
/// <c>WithTrackingName</c> so tests can assert the generator caches correctly (every tracked step reason is
/// <c>Cached</c>/<c>Unchanged</c> on an unrelated edit). These are test-observability hooks, not a consumer
/// contract — mirroring the <see cref="TemplateTrackingNames"/>/<c>MatchTrackingNames</c> two-step convention.
/// </summary>
internal static class BuilderTrackingNames
{
    public const string Transform = "Synto.Builders.Transform";
    public const string Result = "Synto.Builders.Result";
}
