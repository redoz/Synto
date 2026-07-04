namespace Synto.Generators.Templating;

/// <summary>
/// Names attached to the templating generator's incremental pipeline stages via <c>WithTrackingName</c> so
/// tests can assert the generator caches correctly (every tracked step reason is <c>Cached</c>/<c>Unchanged</c>
/// on an unrelated edit). These are test-observability hooks, not a consumer contract.
/// </summary>
internal static class TemplateTrackingNames
{
    public const string Transform = "Synto.Templating.Transform";
    public const string Result = "Synto.Templating.Result";

    // Distinct names for the second (generic [Template<T>]) ForAttributeWithMetadataName source so the
    // all-steps cacheability guards stay unambiguous across the two pipelines.
    public const string GenericTransform = "Synto.Templating.GenericTransform";
    public const string GenericResult = "Synto.Templating.GenericResult";
}
