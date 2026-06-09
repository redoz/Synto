namespace Synto;

/// <summary>
/// Names attached to the templating generator's incremental pipeline stages via <c>WithTrackingName</c> so
/// tests can assert the generator caches correctly (every tracked step reason is <c>Cached</c>/<c>Unchanged</c>
/// on an unrelated edit). These are test-observability hooks, not a consumer contract.
/// </summary>
internal static class TemplateTrackingNames
{
    public const string Transform = "Synto.Templating.Transform";
    public const string Result = "Synto.Templating.Result";
}
