using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using Windows.Security.Credentials;

namespace BDFR.PersianCalendar.Desktop;

internal sealed record QueuedCollaborationCommand(
    Guid CommandId, string GroupId, string Kind, JsonElement Payload, DateTimeOffset CreatedAt);

/// <summary>Durable local queue. Commands are replayed with stable IDs; server receipts make retries idempotent.</summary>
internal sealed class CollaborationOfflineQueue
{
    private static readonly object Gate = new();
    private static readonly string QueuePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Zurvan", "Desk", "collaboration-queue.bin");

    internal IReadOnlyList<QueuedCollaborationCommand> ReadAll()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(QueuePath)) return Array.Empty<QueuedCollaborationCommand>();
                var encrypted = File.ReadAllBytes(QueuePath);
                var clear = Decrypt(encrypted);
                return JsonSerializer.Deserialize<List<QueuedCollaborationCommand>>(clear)
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

    private static byte[] QueueEncryptionKey()
    {
        const string resource = "Zurvan.Desk.Collaboration.Queue.v1";
        var vault = new PasswordVault();
        try
        {
            var credential = vault.FindAllByResource(resource).FirstOrDefault();
            if (credential is not null)
            {
                credential.RetrievePassword();
                return Convert.FromBase64String(credential.Password);
            }
        }
        catch { }
        var key = RandomNumberGenerator.GetBytes(32);
        vault.Add(new PasswordCredential(resource, "current-user", Convert.ToBase64String(key)));
        return key;
    }

    private static byte[] Encrypt(byte[] clear)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[clear.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(QueueEncryptionKey(), 16);
        aes.Encrypt(nonce, clear, cipher, tag);
        return nonce.Concat(cipher).Concat(tag).ToArray();
    }

    private static byte[] Decrypt(byte[] encrypted)
    {
        if (encrypted.Length < 28) throw new CryptographicException("صف آفلاین رمزگذاری‌شده نامعتبر است.");
        var nonce = encrypted.AsSpan(0, 12);
        var cipher = encrypted.AsSpan(12, encrypted.Length - 28);
        var tag = encrypted.AsSpan(encrypted.Length - 16, 16);
        var clear = new byte[cipher.Length];
        using var aes = new AesGcm(QueueEncryptionKey(), 16);
        aes.Decrypt(nonce, cipher, tag, clear);
        return clear;
    }

    private static void Save(IReadOnlyList<QueuedCollaborationCommand> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(QueuePath)!);
        var temp = QueuePath + ".tmp";
        var clear = JsonSerializer.SerializeToUtf8Bytes(rows);
        var encrypted = Encrypt(clear);
        File.WriteAllBytes(temp, encrypted);
        File.Move(temp, QueuePath, true);
    }
}
