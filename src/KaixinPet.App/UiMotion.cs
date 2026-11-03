using System.Windows;

namespace KaixinPet.App;

internal static class UiMotion
{
    public static bool ReduceMotion { get; set; }

    public static bool AnimationsEnabled =>
        !ReduceMotion && SystemParameters.ClientAreaAnimation;
}
