using System.Runtime.CompilerServices;

namespace Synto.Example.ObjectReader.Test;
public static class ModuleInitializer
{

    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Initialize();

        VerifyDiffPlex.Initialize();
    }
}