using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using FilesMate.App.Models;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

using Windows.UI.ViewManagement;

namespace FilesMate.App.Animations;

/// <summary>
/// Opt-in bridge for FilesMate-owned templates. Native VSM still owns state
/// selection, setters, generated transitions and theme-animation endpoints.
/// </summary>
public sealed class MotionVisualStateManager : VisualStateManager
{
    private readonly ConditionalWeakTable<FrameworkElement, RootBinding> _bindings = new();
    private readonly HashSet<RootBinding> _loaded = [];
    private UISettings? _systemSettings;
    private bool _systemSubscribed, _systemEventUnavailable;
    private long _watchGeneration;
    private int _policyQueued;
    private const int EventUnavailable = unchecked((int)0x80070490);

    private static bool AnimationsEnabled => App.Motion.Resolve(MotionDurations.Fast) > TimeSpan.Zero;

    protected override bool GoToStateCore(Control control, FrameworkElement templateRoot,
        string stateName, VisualStateGroup group, VisualState state, bool useTransitions)
    {
        if (group is null || state is null)
            return base.GoToStateCore(control, templateRoot, stateName, group, state, useTransitions);

        if (!_bindings.TryGetValue(templateRoot, out var binding))
        {
            binding = new RootBinding(this, control, templateRoot);
            _bindings.Add(templateRoot, binding);
        }
        var wasLoaded = templateRoot.IsLoaded;
        var request = binding.Record(group, state);
        binding.Synchronize(AnimationsEnabled);
        if (!binding.IsCurrent(group, request) || (wasLoaded && !templateRoot.IsLoaded)) return true;
        var changed = base.GoToStateCore(control, templateRoot, stateName, group, state, useTransitions && AnimationsEnabled);
        if (changed && !AnimationsEnabled && templateRoot.IsLoaded && binding.IsCurrent(group, request))
        {
            // A state storyboard is independent of useTransitions. Re-entering
            // the same target without transitions lets native VSM snap both it
            // and any generated transition to their final values.
            base.GoToStateCore(control, templateRoot, stateName, group, state, false);
        }
        return changed;
    }

    private void Settle(RootBinding binding, VisualStateGroup group, VisualState state) =>
        base.GoToStateCore(binding.Control, binding.Root, state.Name, group, state, false);

    private void Attach(RootBinding binding)
    {
        if (!_loaded.Add(binding)) return;
        if (_loaded.Count == 1)
        {
            _watchGeneration++;
            App.AppearanceChanged += Appearance_Changed;
            _systemSettings ??= new UISettings();
            if (!_systemSubscribed && !_systemEventUnavailable)
            {
                try
                {
                    _systemSettings.AnimationsEnabledChanged += System_AnimationsEnabledChanged;
                    _systemSubscribed = true;
                }
                catch (COMException error) when (error.HResult == EventUnavailable)
                {
                    _systemEventUnavailable = true;
                    System.Diagnostics.Trace.TraceWarning("Motion policy event unavailable: {0}", error);
                }
            }
        }
        // Read after subscribing: a setting change during load cannot be lost.
        binding.Synchronize(AnimationsEnabled);
    }

    private void Detach(RootBinding binding)
    {
        if (!_loaded.Remove(binding)) return;
        if (_loaded.Count != 0) return;
        _watchGeneration++;
        App.AppearanceChanged -= Appearance_Changed;
        if (!_systemSubscribed) return;
        try
        {
            _systemSettings!.AnimationsEnabledChanged -= System_AnimationsEnabledChanged;
        }
        catch (COMException error) when (error.HResult == EventUnavailable)
        {
            _systemEventUnavailable = true;
            System.Diagnostics.Trace.TraceWarning("Motion policy event already unavailable: {0}", error);
        }
        finally { _systemSubscribed = false; }
    }

    private void Appearance_Changed(object? sender, AppearanceSettings settings) => PolicyChanged();

    private void System_AnimationsEnabledChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args) => PolicyChanged();

    private void PolicyChanged()
    {
        var generation = Volatile.Read(ref _watchGeneration);
        void Apply()
        {
            if (generation != _watchGeneration || _loaded.Count == 0) return;
            foreach (var binding in _loaded.ToArray())
            {
                // Native state events can reenter appearance settings. Resolve
                // anew for each owner rather than applying a stale outer policy.
                if (_loaded.Contains(binding) && binding.Root.IsLoaded)
                    binding.Synchronize(AnimationsEnabled);
            }
        }
        if (DispatcherQueue.HasThreadAccess) Apply();
        else if (Interlocked.Exchange(ref _policyQueued, 1) == 0)
        {
            if (!DispatcherQueue.TryEnqueue(() =>
            {
                Interlocked.Exchange(ref _policyQueued, 0);
                // A detach/reload may have occurred while this one coalesced
                // notification waited. Only the currently loaded owners matter.
                if (generation != _watchGeneration) PolicyChanged();
                else Apply();
            })) Interlocked.Exchange(ref _policyQueued, 0);
        }
    }

    private sealed class RootBinding
    {
        private readonly MotionVisualStateManager _owner;
        private readonly Dictionary<VisualStateGroup, GroupBinding> _groups = [];
        private bool? _enabled;

        internal Control Control { get; }
        internal FrameworkElement Root { get; }

        internal RootBinding(MotionVisualStateManager owner, Control control, FrameworkElement root)
        {
            _owner = owner;
            Control = control;
            Root = root;
            // Capture unmodified template durations once, before applying policy.
            foreach (var group in GetVisualStateGroups(root)) _groups.Add(group, new GroupBinding(group));
            root.Loaded += Loaded;
            root.Unloaded += Unloaded;
            if (root.IsLoaded) owner.Attach(this);
        }

        internal long Record(VisualStateGroup group, VisualState state)
        {
            var binding = _groups[group];
            binding.Target = state;
            return ++binding.Request;
        }

        internal bool IsCurrent(VisualStateGroup group, long request) => _groups[group].Request == request;

        internal void Synchronize(bool enabled)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;
            if (!enabled) Settle();
            if (_enabled != enabled) return; // A state event can reenter appearance policy.
            foreach (var binding in _groups.Values) binding.SetDurations(enabled);
        }

        private void Settle()
        {
            foreach (var (group, binding) in _groups)
            {
                if (_enabled != false || AnimationsEnabled || !Root.IsLoaded) return;
                // Track requested states, not CurrentState: generated transitions
                // can still be between states when the setting changes.
                if (binding.Target is { } state) _owner.Settle(this, group, state);
            }
        }

        private void Loaded(object sender, RoutedEventArgs args) => _owner.Attach(this);

        private void Unloaded(object sender, RoutedEventArgs args)
        {
            // Native VSM completes its own transitions when a template leaves
            // the tree. Never call GoToState through an obsolete template root.
            _owner.Detach(this);
        }
    }

    private sealed class GroupBinding
    {
        private readonly List<(VisualTransition Transition, Duration Duration)> _transitions = [];
        private readonly List<(Timeline Timeline, Duration Duration, TimeSpan? BeginTime)> _timelines = [];
        internal VisualState? Target { get; set; }
        internal long Request { get; set; }

        internal GroupBinding(VisualStateGroup group)
        {
            foreach (var transition in group.Transitions)
            {
                _transitions.Add((transition, transition.GeneratedDuration));
                if (transition.Storyboard is { } storyboard) Capture(storyboard);
            }
            foreach (var state in group.States)
            {
                if (state.Storyboard is { } storyboard) Capture(storyboard);
            }
        }

        private void Capture(Timeline timeline)
        {
            _timelines.Add((timeline, timeline.Duration, timeline.BeginTime));
            if (timeline is Storyboard storyboard)
            {
                foreach (var child in storyboard.Children) Capture(child);
            }
        }

        internal void SetDurations(bool enabled)
        {
            foreach (var (transition, duration) in _transitions)
                transition.GeneratedDuration = enabled ? duration : new Duration(TimeSpan.Zero);
            foreach (var (timeline, duration, beginTime) in _timelines)
            {
                timeline.Duration = enabled ? duration : new Duration(TimeSpan.Zero);
                timeline.BeginTime = enabled ? beginTime : TimeSpan.Zero;
            }
        }
    }
}
