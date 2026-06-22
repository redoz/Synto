// netstandard2.0 (the required generator TFM) does not define IsExternalInit, which the `init` accessors of
// the equatable `record struct` models need. Synto injects this polyfill for CONSUMERS, but a generator
// project that authors its own equatable models must hand-roll it (friction).
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
