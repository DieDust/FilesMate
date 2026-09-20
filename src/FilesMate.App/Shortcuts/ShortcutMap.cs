namespace FilesMate.App.Shortcuts;

public sealed class ShortcutMap
{
    private readonly Dictionary<ShortcutAction, ShortcutGesture> _gestures;

    public ShortcutMap(IEnumerable<KeyValuePair<ShortcutAction, ShortcutGesture>>? values = null)
    {
        _gestures = ShortcutDefaults.Definitions.ToDictionary(item => item.Action, item => item.Gesture);
        if (values is null)
        {
            return;
        }

        foreach (var (action, gesture) in values)
        {
            if (gesture.IsValid && _gestures.ContainsKey(action))
            {
                _gestures[action] = gesture;
            }
        }

        RemoveConflictingOverrides();
    }

    public ShortcutGesture this[ShortcutAction action] => _gestures[action];

    public ShortcutAction? FindConflict(ShortcutAction action, ShortcutGesture gesture)
    {
        foreach (var (candidate, assigned) in _gestures)
        {
            if (candidate != action && assigned == gesture)
            {
                return candidate;
            }
        }

        return null;
    }

    public bool TrySet(ShortcutAction action, ShortcutGesture gesture, out ShortcutAction? conflict)
    {
        conflict = null;
        if (!gesture.IsValid || !_gestures.ContainsKey(action))
        {
            return false;
        }

        conflict = FindConflict(action, gesture);
        if (conflict is not null)
        {
            return false;
        }

        _gestures[action] = gesture;
        return true;
    }

    public void Reset()
    {
        _gestures.Clear();
        foreach (var definition in ShortcutDefaults.Definitions)
        {
            _gestures[definition.Action] = definition.Gesture;
        }
    }

    public IReadOnlyDictionary<ShortcutAction, ShortcutGesture> Snapshot() =>
        new Dictionary<ShortcutAction, ShortcutGesture>(_gestures);

    private void RemoveConflictingOverrides()
    {
        var used = new HashSet<ShortcutGesture>();
        foreach (var definition in ShortcutDefaults.Definitions)
        {
            var gesture = _gestures[definition.Action];
            if (!used.Add(gesture))
            {
                _gestures[definition.Action] = definition.Gesture;
                used.Add(definition.Gesture);
            }
        }
    }
}
