using System.Security.Cryptography;
using BDFR.PersianCalendar.Core;

static int Usage()
{
    Console.WriteLine("Zurvan offline activation calculator");
    Console.WriteLine("  keygen <private.pem> <public.pem>");
    Console.WriteLine("  issue <private.pem> <device-code> [edition] [expiry-ISO8601]");
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
    if (args.Length is >= 3 and <= 5 && args[0] == "issue")
    {
        var device = args[2].Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(device, @"^ZRV-[0-9A-F]{24}$"))
            throw new ArgumentException("Invalid device code.");
        var edition = args.Length >= 4 ? args[3] : "standard";
        var expiry = args.Length == 5 ? args[4] : null;
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(args[1]));
        Console.WriteLine(ZurvanLicense.Issue(new(ZurvanLicense.ProductId, device, edition, expiry), rsa));
        return 0;
    }
    if (args.Length == 4 && args[0] == "verify")
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(args[1]));
        var ok = ZurvanLicense.TryVerify(args[3], args[2], rsa, out var license);
        Console.WriteLine(ok ? $"Valid: {license!.Edition}" : "Invalid license");
        return ok ? 0 : 1;
    }
    return Usage();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
