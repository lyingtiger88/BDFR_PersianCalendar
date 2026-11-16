using System.Security.Cryptography;
using BDFR.PersianCalendar.Core;

static int Usage()
{
    Console.WriteLine("Zurvan offline activation calculator");
    Console.WriteLine("  keygen <private.pem> <public.pem>");
    Console.WriteLine("  issue <private.pem> <device-code> [edition] [expiry-ISO8601] [--pahlavi-occasions]");
    Console.WriteLine("    editions: standard (default), pro, enterprise, special, pahlavi");
    Console.WriteLine("  verify <public.pem> <device-code> <activation-code>");
    Console.WriteLine("Keep private.pem OFF the repository and never ship it with clients.");
    return 2;
}

try
{
    if (args.Length == 3 && args[0] == "keygen")
    {
        if (File.Exists(args[1]) || File.Exists(args[2]))
            throw new InvalidOperationException("Refusing to overwrite key files.");
        using var rsa = RSA.Create(3072);
        File.WriteAllText(args[1], rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(args[2], rsa.ExportSubjectPublicKeyInfoPem());
        Console.WriteLine("Keys generated. Store the private key securely; distribute ONLY public key.");
        return 0;
    }
    if (args.Length is >= 3 and <= 6 && args[0] == "issue")
    {
        var grantPahlavi = args[^1] == "--pahlavi-occasions";
        var parameters = grantPahlavi ? args[..^1] : args;
        if (parameters.Length is < 3 or > 5) return Usage();
        var device = parameters[2].Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(device, @"^ZRV-[0-9A-F]{24}$"))
            throw new ArgumentException("Invalid device code.");
        var edition = (parameters.Length >= 4 ? parameters[3] : "standard").Trim().ToLowerInvariant();
        if (edition is not ("standard" or "pro" or "enterprise" or "pahlavi" or "special"))
            throw new ArgumentException("Unknown license edition.");
        var expiry = parameters.Length == 5 ? parameters[4] : null;
        if (expiry is not null && (!DateTimeOffset.TryParse(expiry, out var date) || date <= DateTimeOffset.UtcNow))
            throw new ArgumentException("Expiry must be a future ISO-8601 timestamp.");
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(args[1]));
        var features = grantPahlavi
            ? new[] { ZurvanLicense.PahlaviOccasionsFeature }
            : Array.Empty<string>();
        Console.WriteLine(ZurvanLicense.Issue(
            new(ZurvanLicense.ProductId, device, edition, expiry, features), rsa));
        return 0;
    }
    if (args.Length == 4 && args[0] == "verify")
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(args[1]));
        var ok = ZurvanLicense.TryVerify(args[3], args[2], rsa, out var license);
        Console.WriteLine(ok
            ? $"Valid: {license!.Edition}; Pahlavi occasions: {(license.Edition is "pahlavi" or "special" || (license.Features?.Contains(ZurvanLicense.PahlaviOccasionsFeature) ?? false) ? "enabled" : "disabled")}"
            : "Invalid license");
        return ok ? 0 : 1;
    }
    return Usage();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
