using BDFR.PersianCalendar.Core;
using Windows.Security.Credentials;

namespace BDFR.PersianCalendar.Desktop;

public sealed class WindowsCloudSecretStore : IZurvanCloudSecretStore
{
    private const string ResourceName = "Zurvan.Cloud";
    private readonly PasswordVault _vault = new();

    public Task<string?> GetAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var credential = _vault.Retrieve(ResourceName, name);
            credential.RetrievePassword();
            return Task.FromResult<string?>(credential.Password);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetAsync(
        string name,
        string value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var existing = _vault.Retrieve(ResourceName, name);
            _vault.Remove(existing);
        }
        catch
        {
            // First write or credential already absent.
        }

        _vault.Add(new PasswordCredential(
            ResourceName,
            name,
            value));

        return Task.CompletedTask;
    }

    public Task RemoveAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var existing = _vault.Retrieve(ResourceName, name);
            _vault.Remove(existing);
        }
        catch
        {
            // Removing an absent secret is idempotent.
        }

        return Task.CompletedTask;
    }
}
