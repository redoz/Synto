using System.Runtime.CompilerServices;

namespace Synto.Bootstrap.Test;

internal static class ModuleInitializer
{

    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();


        VerifyDiffPlex.Initialize();
    }
}