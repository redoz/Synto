using System.Runtime.CompilerServices;

namespace Synto.Test;
public static class ModuleInitializer
{
    #region enable

    [ModuleInitializer]
    public static void Init()
    {
        VerifySourceGenerators.Enable();

        #endregion

        VerifyDiffPlex.Initialize();
    }
}