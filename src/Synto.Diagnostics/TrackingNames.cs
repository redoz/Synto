namespace Synto.Diagnostics;

/// <summary>
/// Names attached to the incremental pipeline stages via <c>WithTrackingName</c> so tests can assert the
/// generator caches correctly (every tracked step reason is <c>Cached</c>/<c>Unchanged</c> on an unrelated
/// edit). These are test-observability hooks, not a consumer contract.
/// </summary>
internal static class TrackingNames
{
    public const string Transform = "Synto.Diagnostics.Transform";
    public const string Result = "Synto.Diagnostics.Result";
}
