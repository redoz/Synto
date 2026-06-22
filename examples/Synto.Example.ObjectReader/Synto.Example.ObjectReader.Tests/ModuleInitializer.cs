using System.Runtime.CompilerServices;

namespace Synto.Example.ObjectReader.Tests;

internal static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();
    }
}
