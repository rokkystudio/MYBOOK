using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Book.Formats.Pdf;

namespace MYBOOK.RegressionTests;

internal static class PdfSecurityRevision5Regression
{
    [ModuleInitializer]
    public static void Run()
    {
        var fileKey =
            Enumerable
                .Range(
                    0,
                    32)
                .Select(
                    value => checked((byte)(value + 1)))
                .ToArray();
        var validationSalt =
            new byte[]
            {
                0x10, 0x11, 0x12, 0x13,
                0x14, 0x15, 0x16, 0x17
            };
        var keySalt =
            new byte[]
            {
                0x20, 0x21, 0x22, 0x23,
                0x24, 0x25, 0x26, 0x27
            };

        var user = new byte[48];
        SHA256.HashData(
                validationSalt)
            .CopyTo(
                user,
                0);
        validationSalt.CopyTo(
            user,
            32);
        keySalt.CopyTo(
            user,
            40);

        var userKey =
            SHA256.HashData(
                keySalt);
        var encryptedFileKey =
            EncryptAesCbcNoPadding(
                fileKey,
                userKey);

        const int permissions = -4;
        var permissionsBlock =
            new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(
            permissionsBlock.AsSpan(
                0,
                4),
            permissions);
        permissionsBlock[4] = 0xFF;
        permissionsBlock[5] = 0xFF;
        permissionsBlock[6] = 0xFF;
        permissionsBlock[7] = 0xFF;
        permissionsBlock[8] = (byte)'T';
        permissionsBlock[9] = (byte)'a';
        permissionsBlock[10] = (byte)'d';
        permissionsBlock[11] = (byte)'b';
        permissionsBlock[12] = 1;
        permissionsBlock[13] = 2;
        permissionsBlock[14] = 3;
        permissionsBlock[15] = 4;

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
                        new PdfName(
                            "CryptFilter"),
                    ["CFM"] =
                        new PdfName(
                            "AESV3"),
                    ["AuthEvent"] =
                        new PdfName(
                            "DocOpen")
                });
        var encryption =
            new PdfDictionary(
                new Dictionary<string, PdfObject>(
                    StringComparer.Ordinal)
                {
                    ["Filter"] =
                        new PdfName(
                            "Standard"),
                    ["V"] =
                        new PdfNumber(
                            5),
                    ["R"] =
                        new PdfNumber(
                            5),
                    ["Length"] =
                        new PdfNumber(
                            256),
                    ["O"] =
                        new PdfString(
                            new byte[48]),
                    ["U"] =
                        new PdfString(
                            user),
                    ["OE"] =
                        new PdfString(
                            new byte[32]),
                    ["UE"] =
                        new PdfString(
                            encryptedFileKey),
                    ["P"] =
                        new PdfNumber(
                            permissions),
                    ["EncryptMetadata"] =
                        new PdfBoolean(
                            true),
                    ["Perms"] =
                        new PdfString(
                            encryptedPermissions),
                    ["CF"] =
                        new PdfDictionary(
                            new Dictionary<string, PdfObject>(
                                StringComparer.Ordinal)
                            {
                                ["StdCF"] =
                                    cryptFilter
                            }),
                    ["StrF"] =
                        new PdfName(
                            "StdCF"),
                    ["StmF"] =
                        new PdfName(
                            "StdCF")
                });
        var trailer =
            new PdfDictionary(
                new Dictionary<string, PdfObject>(
                    StringComparer.Ordinal));

        var handler =
            PdfStandardSecurityHandler.CreateForEmptyPassword(
                encryption,
                trailer);

        var plainString =
            Encoding.ASCII.GetBytes(
                "AESV3 string marker");
        var encryptedString =
            EncryptAesV3Object(
                plainString,
                fileKey,
                0x30);
        var decryptedString =
            handler.DecryptObject(
                new PdfString(
                    encryptedString),
                objectNumber: 25,
                generation: 7) as PdfString
            ?? throw new InvalidOperationException(
                "PDF AESV3 string regression не вернул PdfString.");

        if (!decryptedString.Data.SequenceEqual(
                plainString))
        {
            throw new InvalidOperationException(
                "PDF AESV3 string regression не расшифровал данные через file key.");
        }

        var plainStream =
            Encoding.ASCII.GetBytes(
                "AESV3 stream marker");
        var encryptedStream =
            EncryptAesV3Object(
                plainStream,
                fileKey,
                0x40);
        var decryptedStream =
            handler.DecryptObject(
                new PdfStream(
                    new PdfDictionary(
                        new Dictionary<string, PdfObject>(
                            StringComparer.Ordinal)),
                    encryptedStream),
                objectNumber: 99,
                generation: 2) as PdfStream
            ?? throw new InvalidOperationException(
                "PDF AESV3 stream regression не вернул PdfStream.");

        if (!decryptedStream.Data.SequenceEqual(
                plainStream))
        {
            throw new InvalidOperationException(
                "PDF AESV3 stream regression не расшифровал данные через file key.");
        }

        Console.WriteLine(
            "PASS PDF AESV3 security");
    }

    private static byte[] EncryptAesV3Object(
        byte[] data,
        byte[] key,
        byte ivSeed)
    {
        var iv =
            Enumerable
                .Range(
                    0,
                    16)
                .Select(
                    value => checked((byte)(ivSeed + value)))
                .ToArray();

        using var aes =
            Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor =
            aes.CreateEncryptor();
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
        using var aes =
            Aes.Create();
        aes.Key = key;
        aes.IV = new byte[16];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;

        using var encryptor =
            aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(
            data,
            0,
            data.Length);
    }

    private static byte[] EncryptAesEcbNoPadding(
        byte[] data,
        byte[] key)
    {
        using var aes =
            Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;

        using var encryptor =
            aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(
            data,
            0,
            data.Length);
    }
}
