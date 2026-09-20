using System.Runtime.CompilerServices;

using FilesMate.App.Controls.Glass;
using FilesMate.App.Models;

using Microsoft.UI.Xaml;

namespace FilesMate.App.Animations;

public sealed class GlassSceneController : IDisposable
{
    private static readonly ConditionalWeakTable<FrameworkElement, GlassSceneController> Scenes = new();

    private readonly FrameworkElement _root;
    private readonly HashSet<LiquidGlassSurface> _surfaces = [];
    private bool _disposed;

    public GlassSceneController(
        Window window,
        FrameworkElement root,
        Func<bool>? animationsEnabled = null,
        bool? remoteSession = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        _root = root ?? throw new ArgumentNullException(nameof(root));
        State = GlassSceneState.Resolve(
            GlassEffectMode.Balanced,
            animationsEnabled?.Invoke() ?? true,
            windowActive: true,
            remoteSession ?? false);

        Scenes.Remove(root);
        Scenes.Add(root, this);
    }

    public GlassSceneState State { get; private set; }

    public void Apply(AppearanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        State = GlassSceneState.Resolve(settings);

        foreach (var surface in _surfaces.ToArray())
        {
            surface.ApplySceneState(State);
        }
    }

    internal static bool TryGet(XamlRoot? xamlRoot, out GlassSceneController? controller)
    {
        if (xamlRoot?.Content is FrameworkElement root && Scenes.TryGetValue(root, out var scene))
        {
            controller = scene;
            return true;
        }

        controller = null;
        return false;
    }

    internal void RegisterSurface(LiquidGlassSurface surface)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);
        _surfaces.Add(surface);
        surface.AttachScene(this, State);
    }

    internal void UnregisterSurface(LiquidGlassSurface surface)
    {
        if (_surfaces.Remove(surface))
        {
            surface.DetachScene(this);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Scenes.Remove(_root);
        foreach (var surface in _surfaces.ToArray())
        {
            surface.DetachScene(this);
        }

        _surfaces.Clear();
    }
}
