using ChurchSlides.Domain.Text;

namespace ChurchSlides.Spike;

/// <summary>A model and where the selection was, by plain-text offset.</summary>
public sealed record Snapshot(RichText Text, string Json, int SelectionStart, int SelectionEnd);

/// <summary>
/// The app-level undo (D-2, ARCH 8.2): the control's own undo is off, and a snapshot of the
/// model is taken on each typing pause. Undo and Redo reload a snapshot into the editor.
/// </summary>
public sealed class SnapshotUndo
{
    private const int MaxSnapshots = 100;

    /// <summary>Oldest first; the last one is the current state.</summary>
    private readonly List<Snapshot> done = [];
    private readonly Stack<Snapshot> undone = new();

    public void Reset(RichText text)
    {
        done.Clear();
        undone.Clear();
        done.Add(new Snapshot(text, ModelJson.Of(text), 0, 0));
    }

    /// <summary>Takes a snapshot if the model changed since the last one. Clears Redo then.</summary>
    public void Record(RichText text, int selectionStart, int selectionEnd)
    {
        var snapshot = new Snapshot(text, ModelJson.Of(text), selectionStart, selectionEnd);
        if (done.Count > 0 && done[^1].Json == snapshot.Json)
        {
            done[^1] = snapshot; // same text: keep the latest selection
            return;
        }
        done.Add(snapshot);
        if (done.Count > MaxSnapshots)
            done.RemoveAt(0);
        undone.Clear();
    }

    /// <summary>The snapshot to go back to, or null if there is none.</summary>
    public Snapshot? Undo()
    {
        if (done.Count < 2)
            return null;
        undone.Push(done[^1]);
        done.RemoveAt(done.Count - 1);
        return done[^1];
    }

    public Snapshot? Redo()
    {
        if (!undone.TryPop(out var snapshot))
            return null;
        done.Add(snapshot);
        return snapshot;
    }
}
