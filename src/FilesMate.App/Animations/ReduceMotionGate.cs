using FilesMate.App.Models;

namespace FilesMate.App.Animations;

public static class ReduceMotionGate
{
    public static bool AnimationsEnabled(bool systemEnabled, ReduceMotionKind reduce) =>
        systemEnabled && reduce != ReduceMotionKind.On;
}
