using System.Runtime.CompilerServices;

namespace Synto.Diagnostics.Test;

public static class ModuleInitializer
{

    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();


        VerifyDiffPlex.Initialize();
    }
}