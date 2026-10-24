using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Book.Formats.Pdf;

namespace MYBOOK.RegressionTests;

internal static class PdfSecurityRevision6Regression
{
    [ModuleInitializer]
    public static void Run()
    {
        var fileKey =
            Enumerable
                .Range(0, 32)
                .Select(value => checked((byte)(0x80 + value)))
                .ToArray();
        var validationSalt =
            new byte[]
            {
                0x31, 0x32, 0x33, 0x34,
                0x35, 0x36, 0x37, 0x38
            };
        var keySalt =
            new byte[]
            {
                0x41, 0x42, 0x43, 0x44,
                0x45, 0x46, 0x47, 0x48
            };

        var user = new byte[48];
        HardenedHash(
                ReadOnlySpan<byte>.Empty,
                validationSalt,
                ReadOnlySpan<byte>.Empty)
            .CopyTo(user, 0);
        validationSalt.CopyTo(user, 32);
        keySalt.CopyTo(user, 40);

        var userKey =
            HardenedHash(
                ReadOnlySpan<byte>.Empty,
                keySalt,
                ReadOnlySpan<byte>.Empty);
        var encryptedFileKey =
            EncryptAesCbcNoPadding(
                fileKey,
                userKey);

        const int permissions = -4;
        var permissionsBlock = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(
            permissionsBlock.AsSpan(0, 4),
            permissions);
        permissionsBlock[4] = 0xFF;
        permissionsBlock[5] = 0xFF;
        permissionsBlock[6] = 0xFF;
        permissionsBlock[7] = 0xFF;
        permissionsBlock[8] = (byte)'T';
        permissionsBlock[9] = (byte)'a';
        permissionsBlock[10] = (byte)'d';
        permissionsBlock[11] = (byte)'b';
        permissionsBlock[12] = 5;
        permissionsBlock[13] = 6;
        permissionsBlock[14] = 7;
        permissionsBlock[15] = 8;

        var encryptedPermissions =
            EncryptAesEcbNoPadding(
                permissionsBlock,
                fileKey);

        var cryptFilter =
            new PdfDictionary(
                new Dictionary<string, PdfObject>(
                    StringComparer.Ordinal)
                {
                    ["Type"] =
                        new PdfName("CryptFilter"),
                    ["CFM"] =
                        new PdfName("AESV3"),
                    ["AuthEvent"] =
                        new PdfName("DocOpen")
                });
        var encryption =
            new PdfDictionary(
                new Dictionary<string, PdfObject>(
                    StringComparer.Ordinal)
                {
                    ["Filter"] = new PdfName("Standard"),
                    ["V"] = new PdfNumber(5),
                    ["R"] = new PdfNumber(6),
                    ["Length"] = new PdfNumber(256),
                    ["O"] = new PdfString(new byte[48]),
                    ["U"] = new PdfString(user),
                    ["OE"] = new PdfString(new byte[32]),
                    ["UE"] = new PdfString(encryptedFileKey),
                    ["P"] = new PdfNumber(permissions),
                    ["EncryptMetadata"] = new PdfBoolean(true),
                    ["Perms"] = new PdfString(encryptedPermissions),
                    ["CF"] =
                        new PdfDictionary(
                            new Dictionary<string, PdfObject>(
                                StringComparer.Ordinal)
                            {
                                ["StdCF"] = cryptFilter
                            }),
                    ["StrF"] = new PdfName("StdCF"),
                    ["StmF"] = new PdfName("StdCF")
                });
        var trailer =
            new PdfDictionary(
                new Dictionary<string, PdfObject>(
                    StringComparer.Ordinal));

        var handler =
            PdfStandardSecurityHandler.CreateForEmptyPassword(
                encryption,
                trailer);

        var plain =
            Encoding.ASCII.GetBytes(
                "AESV3 revision 6 marker");
        var encrypted =
            EncryptAesV3Object(
                plain,
                fileKey,
                0x50);
        var decrypted =
            handler.DecryptObject(
                new PdfString(encrypted),
                objectNumber: 321,
                generation: 4) as PdfString
            ?? throw new InvalidOperationException(
                "PDF revision 6 regression не вернул PdfString.");

        if (!decrypted.Data.SequenceEqual(plain))
        {
            throw new InvalidOperationException(
                "PDF revision 6 AESV3 regression не расшифровал данные.");
        }

        Console.WriteLine(
            "PASS PDF revision 6 security");
    }

    private static byte[] HardenedHash(
        ReadOnlySpan<byte> password,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> userKey)
    {
        var initial =
            new byte[
                password.Length +
                salt.Length +
                userKey.Length];
        password.CopyTo(initial);
        salt.CopyTo(initial.AsSpan(password.Length));
        userKey.CopyTo(
            initial.AsSpan(password.Length + salt.Length));

        var hash = SHA256.HashData(initial);
        var round = 0;

        while (true)
        {
            var unit =
                new byte[
                    password.Length +
                    hash.Length +
                    userKey.Length];
            password.CopyTo(unit);
            hash.CopyTo(unit, password.Length);
            userKey.CopyTo(
                unit.AsSpan(password.Length + hash.Length));

            var repeated =
                new byte[unit.Length * 64];
            for (var index = 0; index < 64; index++)
            {
                unit.CopyTo(
                    repeated,
                    index * unit.Length);
            }

            var encrypted =
                EncryptAesCbcNoPadding(
                    repeated,
                    hash[..16],
                    hash.AsSpan(16, 16).ToArray());

            var selector = 0;
            for (var index = 0; index < 16; index++)
            {
                selector += encrypted[index];
            }

            hash =
                (selector % 3) switch
                {
                    0 => SHA256.HashData(encrypted),
                    1 => SHA384.HashData(encrypted),
                    _ => SHA512.HashData(encrypted)
                };

            round++;

            if (round >= 64 &&
                encrypted[^1] <= round - 32)
            {
                return hash[..32];
            }
        }
    }

    private static byte[] EncryptAesV3Object(
        byte[] data,
        byte[] key,
        byte ivSeed)
    {
        var iv =
            Enumerable
                .Range(0, 16)
                .Select(value => checked((byte)(ivSeed + value)))
                .ToArray();

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var payload =
            encryptor.TransformFinalBlock(
                data,
                0,
                data.Length);

        return
        [
            .. iv,
            .. payload
        ];
    }

    private static byte[] EncryptAesCbcNoPadding(
        byte[] data,
        byte[] key)
    {
        return EncryptAesCbcNoPadding(
            data,
            key,
            new byte[16]);
    }

    private static byte[] EncryptAesCbcNoPadding(
        byte[] data,
        byte[] key,
        byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(
            data,
            0,
            data.Length);
    }

    private static byte[] EncryptAesEcbNoPadding(
        byte[] data,
        byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(
            data,
            0,
            data.Length);
    }
}
