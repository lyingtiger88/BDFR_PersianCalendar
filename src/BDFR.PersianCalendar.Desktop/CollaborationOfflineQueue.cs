using System.Text.Json;

namespace BDFR.PersianCalendar.Desktop;

internal sealed record QueuedCollaborationCommand(
    Guid CommandId, string GroupId, string Kind, JsonElement Payload, DateTimeOffset CreatedAt);

/// <summary>Durable local queue. Commands are replayed with stable IDs; server receipts make retries idempotent.</summary>
internal sealed class CollaborationOfflineQueue
{
    private static readonly object Gate = new();
    private static readonly string QueuePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Zurvan", "Desk", "collaboration-queue.json");

    internal IReadOnlyList<QueuedCollaborationCommand> ReadAll()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(QueuePath)) return Array.Empty<QueuedCollaborationCommand>();
                return JsonSerializer.Deserialize<List<QueuedCollaborationCommand>>(File.ReadAllText(QueuePath))
                    ?? new List<QueuedCollaborationCommand>();
            }
            catch { return Array.Empty<QueuedCollaborationCommand>(); }
        }
    }

    internal QueuedCollaborationCommand Enqueue(string groupId, string kind, object payload, Guid? commandId = null)
    {
        lock (Gate)
        {
            var rows = ReadAll().ToList();
            var row = new QueuedCollaborationCommand(commandId ?? Guid.NewGuid(), groupId, kind,
                JsonSerializer.SerializeToElement(payload), DateTimeOffset.UtcNow);
            if (rows.All(x => x.CommandId != row.CommandId)) rows.Add(row);
            Save(rows);
            return row;
        }
    }

    internal void Remove(Guid commandId)
    {
        lock (Gate) Save(ReadAll().Where(x => x.CommandId != commandId).ToList());
    }

    private static void Save(IReadOnlyList<QueuedCollaborationCommand> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(QueuePath)!);
        var temp = QueuePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(rows));
        File.Move(temp, QueuePath, true);
    }
}
