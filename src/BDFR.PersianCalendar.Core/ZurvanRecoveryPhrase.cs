using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace BDFR.PersianCalendar.Core;

public sealed record ZurvanCloudRecoveryEnvelope(
    int ProtocolVersion,
    string VaultId,
    int WordCount,
    string PhraseFingerprint,
    string NonceBase64,
    string WrappedVaultKeyBase64,
    string TagBase64);

public static class ZurvanRecoveryPhrase
{
    public const int DefaultWordCount = 24;

    private static readonly int[] SupportedWordCounts =
        [12, 15, 18, 21, 24];

    private static readonly Lazy<string[]> WordList =
        new(LoadWordList, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<IReadOnlyDictionary<string, int>> WordIndex =
        new(
            () => WordList.Value
                .Select((word, index) => (word, index))
                .ToDictionary(
                    x => x.word,
                    x => x.index,
                    StringComparer.Ordinal),
            LazyThreadSafetyMode.ExecutionAndPublication);

    public static string Generate(int wordCount = DefaultWordCount)
    {
        ValidateWordCount(wordCount);

        var entropyBits = wordCount * 11 * 32 / 33;
        var entropy = RandomNumberGenerator.GetBytes(entropyBits / 8);

        try
        {
            return EncodeEntropy(entropy);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    public static string EncodeEntropy(ReadOnlySpan<byte> entropy)
    {
        var entropyBits = entropy.Length * 8;
        if (entropyBits is not (128 or 160 or 192 or 224 or 256))
        {
            throw new ArgumentException(
                "BIP39 entropy must be 128, 160, 192, 224, or 256 bits.",
                nameof(entropy));
        }

        var checksumBits = entropyBits / 32;
        var totalBits = entropyBits + checksumBits;
        var wordCount = totalBits / 11;

        var checksum = SHA256.HashData(entropy);
        var words = new string[wordCount];

        for (var word = 0; word < wordCount; word++)
        {
            var index = 0;

            for (var bit = 0; bit < 11; bit++)
            {
                var globalBit = word * 11 + bit;
                var value = globalBit < entropyBits
                    ? ReadBit(entropy, globalBit)
                    : ReadBit(
                        checksum,
                        globalBit - entropyBits);

                index = (index << 1) | value;
            }

            words[word] = WordList.Value[index];
        }

        return string.Join(' ', words);
    }

    public static byte[] DecodeEntropy(string phrase)
    {
        var words = NormalizePhrase(phrase)
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        ValidateWordCount(words.Length);

        var totalBits = words.Length * 11;
        var entropyBits = totalBits * 32 / 33;
        var checksumBits = entropyBits / 32;
        var entropy = new byte[entropyBits / 8];
        var checksumValue = 0;

        for (var word = 0; word < words.Length; word++)
        {
            if (!WordIndex.Value.TryGetValue(words[word], out var index))
            {
                CryptographicOperations.ZeroMemory(entropy);
                throw new FormatException(
                    $"Unknown recovery word at position {word + 1}.");
            }

            for (var bit = 0; bit < 11; bit++)
            {
                var value = (index >> (10 - bit)) & 1;
                var globalBit = word * 11 + bit;

                if (globalBit < entropyBits)
                {
                    WriteBit(entropy, globalBit, value);
                }
                else
                {
                    checksumValue =
                        (checksumValue << 1) | value;
                }
            }
        }

        var hash = SHA256.HashData(entropy);
        var expectedChecksum = 0;

        for (var bit = 0; bit < checksumBits; bit++)
        {
            expectedChecksum =
                (expectedChecksum << 1) |
                ReadBit(hash, bit);
        }

        if (checksumValue != expectedChecksum)
        {
            CryptographicOperations.ZeroMemory(entropy);
            throw new FormatException(
                "Recovery phrase checksum is invalid.");
        }

        return entropy;
    }

    public static bool TryDecodeEntropy(
        string phrase,
        out byte[] entropy)
    {
        try
        {
            entropy = DecodeEntropy(phrase);
            return true;
        }
        catch
        {
            entropy = [];
            return false;
        }
    }

    public static string GetFingerprint(string phrase)
    {
        var entropy = DecodeEntropy(phrase);

        try
        {
            var hash = SHA256.HashData(entropy);
            return Convert
                .ToHexString(hash.AsSpan(0, 8))
                .ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    public static ZurvanCloudRecoveryEnvelope WrapVaultKey(
        string phrase,
        string vaultId,
        ReadOnlySpan<byte> vaultKey)
    {
        if (vaultKey.Length != 32)
        {
            throw new ArgumentException(
                "Vault key must be exactly 32 bytes.",
                nameof(vaultKey));
        }

        var words = NormalizePhrase(phrase)
            .Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        ValidateWordCount(words.Length);

        var recoveryKey = DeriveRecoveryKey(
            phrase,
            vaultId);

        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var wrapped = new byte[vaultKey.Length];
            var tag = new byte[16];
            var aad = BuildRecoveryAad(
                ZurvanCloudProtocol.CurrentVersion,
                vaultId,
                words.Length);

            using var aes = new AesGcm(
                recoveryKey,
                tag.Length);

            aes.Encrypt(
                nonce,
                vaultKey,
                wrapped,
                tag,
                aad);

            return new ZurvanCloudRecoveryEnvelope(
                ZurvanCloudProtocol.CurrentVersion,
                vaultId,
                words.Length,
                GetFingerprint(phrase),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(wrapped),
                Convert.ToBase64String(tag));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(recoveryKey);
        }
    }

    public static byte[] UnwrapVaultKey(
        string phrase,
        ZurvanCloudRecoveryEnvelope envelope)
    {
        if (envelope.ProtocolVersion !=
            ZurvanCloudProtocol.CurrentVersion)
        {
            throw new NotSupportedException(
                $"Unsupported Zurvan recovery protocol version: {envelope.ProtocolVersion}");
        }

        var normalized = NormalizePhrase(phrase);
        var words = normalized.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        if (words.Length != envelope.WordCount)
        {
            throw new FormatException(
                $"Recovery phrase must contain {envelope.WordCount} words.");
        }

        var fingerprint = GetFingerprint(normalized);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(fingerprint),
                Encoding.ASCII.GetBytes(
                    envelope.PhraseFingerprint)))
        {
            throw new CryptographicException(
                "Recovery phrase does not match this vault.");
        }

        var recoveryKey = DeriveRecoveryKey(
            normalized,
            envelope.VaultId);

        try
        {
            var nonce = Convert.FromBase64String(
                envelope.NonceBase64);
            var wrapped = Convert.FromBase64String(
                envelope.WrappedVaultKeyBase64);
            var tag = Convert.FromBase64String(
                envelope.TagBase64);
            var vaultKey = new byte[wrapped.Length];
            var aad = BuildRecoveryAad(
                envelope.ProtocolVersion,
                envelope.VaultId,
                envelope.WordCount);

            using var aes = new AesGcm(
                recoveryKey,
                tag.Length);

            aes.Decrypt(
                nonce,
                wrapped,
                tag,
                vaultKey,
                aad);

            if (vaultKey.Length != 32)
            {
                CryptographicOperations.ZeroMemory(vaultKey);
                throw new CryptographicException(
                    "Recovered vault key has an invalid length.");
            }

            return vaultKey;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(recoveryKey);
        }
    }

    private static byte[] DeriveRecoveryKey(
        string phrase,
        string vaultId)
    {
        var entropy = DecodeEntropy(phrase);

        try
        {
            using var hmac = new HMACSHA256(entropy);
            return hmac.ComputeHash(
                Encoding.UTF8.GetBytes(
                    $"zurvan-recovery-v1|{vaultId}"));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    private static byte[] BuildRecoveryAad(
        int protocolVersion,
        string vaultId,
        int wordCount)
        => Encoding.UTF8.GetBytes(
            $"zurvan-recovery-v1|{protocolVersion}|{vaultId}|{wordCount}");

    private static string NormalizePhrase(string phrase)
        => string.Join(
            ' ',
            (phrase ?? string.Empty)
                .Normalize(NormalizationForm.FormKD)
                .Trim()
                .ToLowerInvariant()
                .Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));

    private static void ValidateWordCount(int wordCount)
    {
        if (!SupportedWordCounts.Contains(wordCount))
        {
            throw new ArgumentOutOfRangeException(
                nameof(wordCount),
                "BIP39 recovery phrases must contain 12, 15, 18, 21, or 24 words.");
        }
    }

    private static int ReadBit(
        ReadOnlySpan<byte> bytes,
        int bitIndex)
    {
        var byteIndex = bitIndex / 8;
        var shift = 7 - (bitIndex % 8);
        return (bytes[byteIndex] >> shift) & 1;
    }

    private static void WriteBit(
        Span<byte> bytes,
        int bitIndex,
        int value)
    {
        if (value == 0)
            return;

        var byteIndex = bitIndex / 8;
        var shift = 7 - (bitIndex % 8);
        bytes[byteIndex] |= (byte)(1 << shift);
    }

    private static string[] LoadWordList()
    {
        var assembly = typeof(ZurvanRecoveryPhrase).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(
                name => name.EndsWith(
                    ".Resources.bip39-english.txt",
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "Embedded BIP39 English wordlist was not found.");

        using var stream = assembly.GetManifestResourceStream(
            resourceName)
            ?? throw new InvalidOperationException(
                "Embedded BIP39 English wordlist could not be opened.");

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        var words = reader
            .ReadToEnd()
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

        if (words.Length != 2048)
        {
            throw new InvalidOperationException(
                $"BIP39 English wordlist must contain 2048 words, found {words.Length}.");
        }

        return words;
    }
}
