using System.Collections.Generic;

namespace RAEngine.Quests;

/// <summary>Turns engine events into objective progress for one <see cref="Quest"/>.
/// A plain C# object (not a Node) owned by the GameSession, so it is trivially
/// testable without a renderer. Push-driven: the session relays Talked / Defeated /
/// Reached / block-change events into the On* methods, each of which advances the
/// lowest-index incomplete matching objective and refreshes the HUD.</summary>
public sealed class QuestTracker
{
    private readonly GameSession _s;
    private readonly Quest _quest;
    private readonly IReadOnlyList<Objective> _objs;
    private readonly int[] _progress;
    private readonly bool[] _done;
    private readonly HashSet<string>[] _seen; // de-dupe distinct keys (animal names, reached zones)
    private bool _completed;

    public QuestTracker(GameSession s, Quest quest)
    {
        _s = s;
        _quest = quest;
        _objs = quest.Objectives;
        int n = _objs.Count;
        _progress = new int[n];
        _done = new bool[n];
        _seen = new HashSet<string>[n];
        for (int i = 0; i < n; i++) _seen[i] = new HashSet<string>();
    }

    /// <summary>Render the initial checklist (no chime).</summary>
    public void Begin() => Render();

    // ---- push entry points (called by GameSession relays; also driven directly by tests) ----

    public void OnTalk(string npcName)
    {
        for (int i = 0; i < _objs.Count; i++)
        {
            var o = _objs[i];
            if (_done[i] || o.Kind != ObjectiveKind.Talk) continue;
            if (o.Key != null && o.Key != npcName) continue;   // a specific NPC
            if (!_seen[i].Add(npcName ?? "")) continue;        // already counted here — try a later objective
            Advance(i);
            return;                                            // one event advances one objective
        }
    }

    public void OnDefeat(string enemyTypeName)
    {
        for (int i = 0; i < _objs.Count; i++)
        {
            var o = _objs[i];
            if (_done[i] || o.Kind != ObjectiveKind.Defeat) continue;
            if (o.Key != null && o.Key != enemyTypeName) continue;
            Advance(i);
            return;
        }
    }

    public void OnReach(string triggerId)
    {
        for (int i = 0; i < _objs.Count; i++)
        {
            var o = _objs[i];
            if (_done[i] || o.Kind != ObjectiveKind.Reach) continue;
            if (o.Key != null && o.Key != triggerId) continue;
            if (!_seen[i].Add(triggerId ?? "")) continue;      // already counted here — try a later objective
            Advance(i);
            return;
        }
    }

    public void OnBlockChanged(ushort oldId, ushort newId)
    {
        for (int i = 0; i < _objs.Count; i++)
        {
            var o = _objs[i];
            if (_done[i]) continue;
            if (o.Kind != ObjectiveKind.Break && o.Kind != ObjectiveKind.Place && o.Kind != ObjectiveKind.Collect)
                continue;
            // A null/mistyped/"air" key resolves to id 0, which would alias the
            // "broke any block" signature (new==air) — skip it rather than false-complete.
            if (!Core.BlockRegistry.TryId(o.Key, out ushort target) || target == 0) continue;
            // Place: the target appears. Break/Collect (gather-by-breaking): the target is removed.
            bool match = o.Kind == ObjectiveKind.Place
                ? (newId == target && oldId != target)
                : (oldId == target && newId == 0);
            if (!match) continue;
            Advance(i);
            return;
        }
    }

    // ---- headless assertions ----
    public int Progress(int i) => (i >= 0 && i < _progress.Length) ? _progress[i] : 0;
    public bool IsDone(int i) => i >= 0 && i < _done.Length && _done[i];
    public bool AllDone
    {
        get
        {
            for (int i = 0; i < _done.Length; i++)
                if (!_done[i] && !_objs[i].Optional) return false; // optional objectives don't gate completion
            return true;
        }
    }

    // ---- internals ----
    private void Advance(int i)
    {
        var o = _objs[i];
        _progress[i]++;
        if (_progress[i] < o.Count)
        {
            Render(); // refresh the "(k/N)" counter line
            return;
        }

        _done[i] = true;
        Core.AudioManager.Play("chime"); // a warm reward the moment an objective is earned
        Render();
        o.OnComplete?.Invoke(_s);
        if (AllDone && !_completed)
        {
            _completed = true;
            _quest.OnComplete?.Invoke(_s);
            _s.NotifyQuestComplete();
        }
    }

    private void Render()
    {
        var lines = new List<(string, bool)>(_objs.Count);
        for (int i = 0; i < _objs.Count; i++)
        {
            var o = _objs[i];
            string text = o.Counted
                ? $"{o.Label} ({System.Math.Min(_progress[i], o.Count)}/{o.Count})"
                : o.Label;
            lines.Add((text, _done[i]));
        }
        _s.Hud.SetObjectivesWithState(lines);
    }
}
