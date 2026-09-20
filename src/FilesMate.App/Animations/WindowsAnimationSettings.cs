using Windows.UI.ViewManagement;

namespace FilesMate.App.Animations;

internal sealed class WindowsAnimationSettings : IAnimationSettings
{
    private readonly UISettings _settings = new();

    public bool AnimationsEnabled => _settings.AnimationsEnabled;
}
