namespace MKWSatellite.Formats.Byml;

/// <summary>
/// Undoable edits to a BYML tree. Every edit sets (or removes) one dictionary key; vectors and other compound values
/// are replaced as a whole, so undo just puts the old reference back. Consecutive edits to the same key merge into
/// one step until <see cref="Seal"/> (one drag = one undo step).
/// </summary>
public sealed class BymlEditHistory
{
    private sealed class Edit(BymlDict dict, string key, bool hadOld, object? old, bool hasNew, object? value)
    {
        public BymlDict Dict { get; } = dict;
        public string Key { get; } = key;
        public bool HadOld { get; } = hadOld;
        public object? Old { get; } = old;
        public bool HasNew { get; set; } = hasNew;
        public object? New { get; set; } = value;
        public bool Sealed { get; set; }
    }

    private readonly List<Edit> _undo = [];
    private readonly List<Edit> _redo = [];
    private int _savedAt;
    private bool _savedUnreachable;

    /// <summary>True when the tree differs from the last saved (or loaded) state.</summary>
    public bool IsDirty => _savedUnreachable || _undo.Count != _savedAt;
    public int UndoCount => _undo.Count;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Set(BymlDict dict, string key, object? value) => Apply(dict, key, true, value);

    public void Remove(BymlDict dict, string key)
    {
        if (dict.ContainsKey(key)) Apply(dict, key, false, null);
    }

    private void Apply(BymlDict dict, string key, bool hasNew, object? value)
    {
        if (_undo.Count > 0 && _undo[^1] is { Sealed: false } top && ReferenceEquals(top.Dict, dict) && top.Key == key)
        {
            top.HasNew = hasNew;
            top.New = value;
            if (_savedAt == _undo.Count) _savedUnreachable = true; // the saved state was in the middle of this step
        }
        else
        {
            if (_savedAt > _undo.Count) _savedUnreachable = true; // the saved state is in the redo branch being dropped
            var hadOld = dict.TryGetValue(key, out var old);
            Seal();
            _undo.Add(new Edit(dict, key, hadOld, old, hasNew, value));
        }
        _redo.Clear();
        Put(dict, key, hasNew, value);
    }

    /// <summary>Ends merging: the next edit is a separate undo step.</summary>
    public void Seal()
    {
        if (_undo.Count > 0) _undo[^1].Sealed = true;
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var e = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        e.Sealed = true;
        Put(e.Dict, e.Key, e.HadOld, e.Old);
        _redo.Add(e);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var e = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Put(e.Dict, e.Key, e.HasNew, e.New);
        _undo.Add(e);
    }

    /// <summary>The current state is what is on disk.</summary>
    public void MarkSaved()
    {
        Seal();
        _savedAt = _undo.Count;
        _savedUnreachable = false;
    }

    private static void Put(BymlDict dict, string key, bool present, object? value)
    {
        if (present) dict[key] = value;
        else dict.Remove(key);
    }
}
