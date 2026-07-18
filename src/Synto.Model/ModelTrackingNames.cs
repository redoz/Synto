namespace Synto.Generators.Model;

/// <summary>
/// Names attached to the <c>[SyntoModel]</c> generator's incremental pipeline stages via
/// <c>WithTrackingName</c> so tests can assert the generator caches correctly (every tracked step reason is
/// <c>Cached</c>/<c>Unchanged</c> on an unrelated edit). These are test-observability hooks, not a consumer
/// contract.
/// </summary>
internal static class ModelTrackingNames
{
    public const string Transform = "Synto.Model.Transform";
    public const string Result = "Synto.Model.Result";
}
