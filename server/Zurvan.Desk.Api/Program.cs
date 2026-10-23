using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BDFR.PersianCalendar.Core;
using BDFR.PersianCalendar.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<DeskAccountStore>();
builder.Services.AddSingleton<HttpClient>();

builder.Services.AddSingleton<IDeskBlobStore>(sp =>
{
    var env = Environment.GetEnvironmentVariables();

    var endpoint =
        Environment.GetEnvironmentVariable("ZURVAN_DESK_S3_ENDPOINT") ??
        ZurvanCloudProtocol.DefaultEndpoint;

    var region =
        Environment.GetEnvironmentVariable("ZURVAN_DESK_S3_REGION") ??
        ZurvanCloudProtocol.DefaultRegion;

    var bucket =
        Environment.GetEnvironmentVariable("ZURVAN_DESK_S3_BUCKET");

    var accessKey =
        Environment.GetEnvironmentVariable("ZURVAN_DESK_S3_ACCESS_KEY");

    var secretKey =
        Environment.GetEnvironmentVariable("ZURVAN_DESK_S3_SECRET_KEY");

    if (!string.IsNullOrWhiteSpace(bucket) &&
        !string.IsNullOrWhiteSpace(accessKey) &&
        !string.IsNullOrWhiteSpace(secretKey))
    {
        var profile = new ZurvanCloudProfile(
            ZurvanCloudProtocol.DefaultProvider,
            endpoint,
            region,
            bucket,
            "desk",
            "server",
            true);

        var objectStore = new ArvanS3ObjectStore(
            sp.GetRequiredService<HttpClient>(),
            profile,
            new ZurvanCloudCredentials(accessKey, secretKey));

        return new ArvanDeskBlobStore(objectStore);
    }

    var root =
        Environment.GetEnvironmentVariable("ZURVAN_DESK_LOCAL_BLOB_ROOT") ??
        Path.Combine(AppContext.BaseDirectory, "App_Data", "blobs");

    return new LocalDeskBlobStore(root);
});

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    service = "Zurvan Desk",
    status = "ok",
    protocol = 1
}));

app.MapPost("/api/v1/auth/register", async (
    RegisterRequest request,
    DeskAccountStore accounts) =>
{
    var identifier = NormalizeIdentifier(request.Identifier);

    if (identifier.Length < 3)
        return Results.BadRequest(new ApiError("شناسه حساب معتبر نیست."));

    if (request.Password is null || request.Password.Length < 8)
        return Results.BadRequest(new ApiError("رمز عبور باید حداقل ۸ نویسه باشد."));

    var result = await accounts.RegisterAsync(
        identifier,
        request.Password,
        request.DisplayName?.Trim());

    return result.Error is not null
        ? Results.Conflict(new ApiError(result.Error))
        : Results.Ok(new AuthResponse(
            result.Token!,
            result.User!.VaultId,
            result.User.DisplayName,
            result.User.Identifier));
});

app.MapPost("/api/v1/auth/login", async (
    LoginRequest request,
    DeskAccountStore accounts) =>
{
    var identifier = NormalizeIdentifier(request.Identifier);

    var result = await accounts.LoginAsync(
        identifier,
        request.Password ?? string.Empty);

    return result.Error is not null
        ? Results.Unauthorized()
        : Results.Ok(new AuthResponse(
            result.Token!,
            result.User!.VaultId,
            result.User.DisplayName,
            result.User.Identifier));
});

app.MapPost("/api/v1/auth/logout", async (
    HttpContext http,
    DeskAccountStore accounts) =>
{
    var token = GetBearerToken(http);
    if (token is null)
        return Results.Unauthorized();

    await accounts.LogoutAsync(token);
    return Results.NoContent();
});

app.MapGet("/api/v1/account", (
    HttpContext http,
    DeskAccountStore accounts) =>
{
    var user = ResolveUser(http, accounts);
    return user is null
        ? Results.Unauthorized()
        : Results.Ok(new
        {
            user.Id,
            user.Identifier,
            user.DisplayName,
            user.VaultId,
            user.CreatedAtUtc
        });
});

app.MapPut("/api/v1/recovery", async (
    RecoveryEnvelopeRequest request,
    HttpContext http,
    DeskAccountStore accounts) =>
{
    var user = ResolveUser(http, accounts);
    if (user is null)
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.Payload) ||
        request.Payload.Length > 64_000)
    {
        return Results.BadRequest(
            new ApiError("Recovery envelope is invalid."));
    }

    await accounts.SetRecoveryEnvelopeAsync(
        user.Id,
        request.Payload);

    return Results.NoContent();
});

app.MapGet("/api/v1/recovery", (
    HttpContext http,
    DeskAccountStore accounts) =>
{
    var user = ResolveUser(http, accounts);
    if (user is null)
        return Results.Unauthorized();

    var payload = accounts.GetRecoveryEnvelope(user.Id);
    return payload is null
        ? Results.NotFound()
        : Results.Ok(new RecoveryEnvelopeRequest(payload));
});

app.MapGet("/api/v1/files", (
    HttpContext http,
    DeskAccountStore accounts) =>
{
    var user = ResolveUser(http, accounts);
    if (user is null)
        return Results.Unauthorized();

    var files = accounts.ListFiles(user.Id)
        .OrderByDescending(x => x.ModifiedAtUtc)
        .Select(x => new DeskFileResponse(
            x.Id,
            x.EncryptedName,
            x.Size,
            x.ModifiedAtUtc,
            x.ContentType));

    return Results.Ok(files);
});

app.MapPost("/api/v1/files", async (
    HttpContext http,
    DeskAccountStore accounts,
    IDeskBlobStore blobs) =>
{
    var user = ResolveUser(http, accounts);
    if (user is null)
        return Results.Unauthorized();

    var encryptedName =
        http.Request.Headers["X-Zurvan-Encrypted-Name"].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(encryptedName))
    {
        return Results.BadRequest(
            new ApiError("Encrypted file name metadata is required."));
    }

    const long maxBytes = 100L * 1024 * 1024;

    await using var buffer = new MemoryStream();
    await http.Request.Body.CopyToAsync(buffer);

    if (buffer.Length <= 0 || buffer.Length > maxBytes)
    {
        return Results.BadRequest(
            new ApiError("File size must be between 1 byte and 100 MB."));
    }

    var fileId = $"file_{Guid.NewGuid():N}";
    var objectKey = BuildUserFileKey(user.Id, fileId);
    var contentType = string.IsNullOrWhiteSpace(http.Request.ContentType)
        ? "application/octet-stream"
        : http.Request.ContentType!;

    await blobs.PutAsync(
        objectKey,
        buffer.ToArray(),
        contentType,
        http.RequestAborted);

    var file = new DeskFileRecord
    {
        Id = fileId,
        UserId = user.Id,
        EncryptedName = encryptedName,
        Size = buffer.Length,
        ModifiedAtUtc = DateTimeOffset.UtcNow,
        ContentType = contentType,
        ObjectKey = objectKey
    };

    await accounts.UpsertFileAsync(file);

    return Results.Ok(new DeskFileResponse(
        file.Id,
        file.EncryptedName,
        file.Size,
        file.ModifiedAtUtc,
        file.ContentType));
});

app.MapGet("/api/v1/files/{id}", async (
    string id,
    HttpContext http,
    DeskAccountStore accounts,
    IDeskBlobStore blobs) =>
{
    var user = ResolveUser(http, accounts);
    if (user is null)
        return Results.Unauthorized();

    var file = accounts.GetFile(user.Id, id);
    if (file is null)
        return Results.NotFound();

    var payload = await blobs.GetAsync(
        file.ObjectKey,
        http.RequestAborted);

    if (payload is null)
        return Results.NotFound();

    return Results.File(
        payload,
        file.ContentType,
        enableRangeProcessing: false);
});

app.MapPut("/api/v1/files/{id}/metadata", async (
    string id,
    RenameFileRequest request,
    HttpContext http,
    DeskAccountStore accounts) =>
{
    var user = ResolveUser(http, accounts);
    if (user is null)
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.EncryptedName))
    {
        return Results.BadRequest(
            new ApiError("EncryptedName is required."));
    }

    var updated = await accounts.RenameFileAsync(
        user.Id,
        id,
        request.EncryptedName);

    return updated is null
        ? Results.NotFound()
        : Results.Ok(new DeskFileResponse(
            updated.Id,
            updated.EncryptedName,
            updated.Size,
            updated.ModifiedAtUtc,
            updated.ContentType));
});

app.MapDelete("/api/v1/files/{id}", async (
    string id,
    HttpContext http,
    DeskAccountStore accounts,
    IDeskBlobStore blobs) =>
{
    var user = ResolveUser(http, accounts);
    if (user is null)
        return Results.Unauthorized();

    var file = accounts.GetFile(user.Id, id);
    if (file is null)
        return Results.NotFound();

    await blobs.DeleteAsync(
        file.ObjectKey,
        http.RequestAborted);

    await accounts.DeleteFileAsync(
        user.Id,
        id);

    return Results.NoContent();
});

app.Run();

static string NormalizeIdentifier(string? value)
    => (value ?? string.Empty).Trim().ToLowerInvariant();

static string? GetBearerToken(HttpContext http)
{
    var header = http.Request.Headers.Authorization.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(header) ||
        !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    return header["Bearer ".Length..].Trim();
}

static DeskUser? ResolveUser(
    HttpContext http,
    DeskAccountStore accounts)
{
    var token = GetBearerToken(http);
    return token is null
        ? null
        : accounts.ResolveSession(token);
}

static string BuildUserFileKey(
    string userId,
    string fileId)
    => $"users/{userId}/files/{fileId}.zdf";

public sealed record RegisterRequest(
    string Identifier,
    string Password,
    string? DisplayName);

public sealed record LoginRequest(
    string Identifier,
    string Password);

public sealed record AuthResponse(
    string Token,
    string VaultId,
    string? DisplayName,
    string Identifier);

public sealed record RenameFileRequest(
    string EncryptedName);

public sealed record RecoveryEnvelopeRequest(
    string Payload);

public sealed record DeskFileResponse(
    string Id,
    string EncryptedName,
    long Size,
    DateTimeOffset ModifiedAtUtc,
    string ContentType);

public sealed record ApiError(string Error);

public sealed class DeskAccountStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private DeskDatabase _database;

    public DeskAccountStore()
    {
        var root =
            Environment.GetEnvironmentVariable("ZURVAN_DESK_ACCOUNT_DATA_ROOT") ??
            Path.Combine(AppContext.BaseDirectory, "App_Data");

        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "accounts.json");

        _database = File.Exists(_path)
            ? JsonSerializer.Deserialize<DeskDatabase>(
                  File.ReadAllText(_path))
              ?? new DeskDatabase()
            : new DeskDatabase();
    }

    public async Task<AuthStoreResult> RegisterAsync(
        string identifier,
        string password,
        string? displayName)
    {
        await _gate.WaitAsync();

        try
        {
            if (_database.Users.Any(
                    x => string.Equals(
                        x.Identifier,
                        identifier,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return new AuthStoreResult(
                    null,
                    null,
                    "این حساب قبلاً ساخته شده است.");
            }

            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = HashPassword(password, salt);

            var user = new DeskUser
            {
                Id = $"usr_{Guid.NewGuid():N}",
                Identifier = identifier,
                DisplayName = string.IsNullOrWhiteSpace(displayName)
                    ? null
                    : displayName.Trim(),
                PasswordSalt = Convert.ToBase64String(salt),
                PasswordHash = Convert.ToBase64String(hash),
                VaultId = ZurvanCloudProtocol.NewVaultId(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            _database.Users.Add(user);
            var token = CreateSessionLocked(user.Id);
            await SaveLockedAsync();

            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(hash);

            return new AuthStoreResult(user, token, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AuthStoreResult> LoginAsync(
        string identifier,
        string password)
    {
        await _gate.WaitAsync();

        try
        {
            var user = _database.Users.FirstOrDefault(
                x => string.Equals(
                    x.Identifier,
                    identifier,
                    StringComparison.OrdinalIgnoreCase));

            if (user is null ||
                !VerifyPassword(password, user))
            {
                return new AuthStoreResult(
                    null,
                    null,
                    "نام کاربری یا رمز عبور نادرست است.");
            }

            var token = CreateSessionLocked(user.Id);
            await SaveLockedAsync();

            return new AuthStoreResult(user, token, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public DeskUser? ResolveSession(string token)
    {
        var tokenHash = HashToken(token);

        var session = _database.Sessions.FirstOrDefault(
            x => string.Equals(
                     x.TokenHash,
                     tokenHash,
                     StringComparison.Ordinal) &&
                 x.ExpiresAtUtc > DateTimeOffset.UtcNow);

        if (session is null)
            return null;

        return _database.Users.FirstOrDefault(
            x => x.Id == session.UserId);
    }

    public async Task LogoutAsync(string token)
    {
        await _gate.WaitAsync();

        try
        {
            var tokenHash = HashToken(token);
            _database.Sessions.RemoveAll(
                x => string.Equals(
                    x.TokenHash,
                    tokenHash,
                    StringComparison.Ordinal));

            await SaveLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public string? GetRecoveryEnvelope(string userId)
        => _database.Users
            .FirstOrDefault(x => x.Id == userId)
            ?.RecoveryEnvelope;

    public async Task SetRecoveryEnvelopeAsync(
        string userId,
        string payload)
    {
        await _gate.WaitAsync();

        try
        {
            var user = _database.Users.FirstOrDefault(
                x => x.Id == userId)
                ?? throw new InvalidOperationException(
                    "Desk user was not found.");

            user.RecoveryEnvelope = payload;
            await SaveLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<DeskFileRecord> ListFiles(string userId)
        => _database.Files
            .Where(x => x.UserId == userId)
            .Select(CloneFile)
            .ToArray();

    public DeskFileRecord? GetFile(
        string userId,
        string id)
        => _database.Files
            .Where(x => x.UserId == userId && x.Id == id)
            .Select(CloneFile)
            .FirstOrDefault();

    public async Task UpsertFileAsync(
        DeskFileRecord file)
    {
        await _gate.WaitAsync();

        try
        {
            _database.Files.RemoveAll(
                x => x.UserId == file.UserId &&
                     x.Id == file.Id);

            _database.Files.Add(CloneFile(file));
            await SaveLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DeskFileRecord?> RenameFileAsync(
        string userId,
        string id,
        string encryptedName)
    {
        await _gate.WaitAsync();

        try
        {
            var file = _database.Files.FirstOrDefault(
                x => x.UserId == userId &&
                     x.Id == id);

            if (file is null)
                return null;

            file.EncryptedName = encryptedName;
            file.ModifiedAtUtc = DateTimeOffset.UtcNow;
            await SaveLockedAsync();
            return CloneFile(file);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteFileAsync(
        string userId,
        string id)
    {
        await _gate.WaitAsync();

        try
        {
            _database.Files.RemoveAll(
                x => x.UserId == userId &&
                     x.Id == id);

            await SaveLockedAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private string CreateSessionLocked(string userId)
    {
        _database.Sessions.RemoveAll(
            x => x.ExpiresAtUtc <= DateTimeOffset.UtcNow);

        var raw = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(raw)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        CryptographicOperations.ZeroMemory(raw);

        _database.Sessions.Add(new DeskSession
        {
            UserId = userId,
            TokenHash = HashToken(token),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30)
        });

        return token;
    }

    private async Task SaveLockedAsync()
    {
        var temp = _path + ".tmp";

        await File.WriteAllTextAsync(
            temp,
            JsonSerializer.Serialize(
                _database,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));

        File.Move(temp, _path, overwrite: true);
    }

    private static byte[] HashPassword(
        string password,
        byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            210_000,
            HashAlgorithmName.SHA256,
            32);

    private static bool VerifyPassword(
        string password,
        DeskUser user)
    {
        var salt = Convert.FromBase64String(
            user.PasswordSalt);

        var expected = Convert.FromBase64String(
            user.PasswordHash);

        var actual = HashPassword(
            password,
            salt);

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                actual,
                expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                salt);
            CryptographicOperations.ZeroMemory(
                expected);
            CryptographicOperations.ZeroMemory(
                actual);
        }
    }

    private static string HashToken(string token)
        => Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(token)))
            .ToLowerInvariant();

    private static DeskFileRecord CloneFile(
        DeskFileRecord source)
        => new()
        {
            Id = source.Id,
            UserId = source.UserId,
            EncryptedName = source.EncryptedName,
            Size = source.Size,
            ModifiedAtUtc = source.ModifiedAtUtc,
            ContentType = source.ContentType,
            ObjectKey = source.ObjectKey
        };
}

public sealed record AuthStoreResult(
    DeskUser? User,
    string? Token,
    string? Error);

public sealed class DeskDatabase
{
    public List<DeskUser> Users { get; set; } = [];
    public List<DeskSession> Sessions { get; set; } = [];
    public List<DeskFileRecord> Files { get; set; } = [];
}

public sealed class DeskUser
{
    public string Id { get; set; } = "";
    public string Identifier { get; set; } = "";
    public string? DisplayName { get; set; }
    public string PasswordSalt { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string VaultId { get; set; } = "";
    public string? RecoveryEnvelope { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class DeskSession
{
    public string UserId { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

public sealed class DeskFileRecord
{
    public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public string EncryptedName { get; set; } = "";
    public long Size { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
    public string ObjectKey { get; set; } = "";
}

public interface IDeskBlobStore
{
    Task PutAsync(
        string key,
        byte[] payload,
        string contentType,
        CancellationToken cancellationToken);

    Task<byte[]?> GetAsync(
        string key,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        string key,
        CancellationToken cancellationToken);
}

public sealed class ArvanDeskBlobStore(
    IZurvanCloudObjectStore objectStore) : IDeskBlobStore
{
    public Task PutAsync(
        string key,
        byte[] payload,
        string contentType,
        CancellationToken cancellationToken)
        => objectStore.PutAsync(
            key,
            payload,
            contentType,
            cancellationToken);

    public Task<byte[]?> GetAsync(
        string key,
        CancellationToken cancellationToken)
        => objectStore.GetAsync(
            key,
            cancellationToken);

    public Task DeleteAsync(
        string key,
        CancellationToken cancellationToken)
        => objectStore.DeleteAsync(
            key,
            cancellationToken);
}

public sealed class LocalDeskBlobStore
    : IDeskBlobStore
{
    private readonly string _root;

    public LocalDeskBlobStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async Task PutAsync(
        string key,
        byte[] payload,
        string contentType,
        CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)!);

        await File.WriteAllBytesAsync(
            path,
            payload,
            cancellationToken);
    }

    public async Task<byte[]?> GetAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        return File.Exists(path)
            ? await File.ReadAllBytesAsync(
                path,
                cancellationToken)
            : null;
    }

    public Task DeleteAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        var relative = key
            .Replace('\\', '/')
            .TrimStart('/')
            .Replace('/', Path.DirectorySeparatorChar);

        var path = Path.GetFullPath(
            Path.Combine(_root, relative));

        if (!path.StartsWith(
                _root,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Invalid blob key.");
        }

        return path;
    }
}
