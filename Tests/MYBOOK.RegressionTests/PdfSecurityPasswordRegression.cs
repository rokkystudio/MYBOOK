using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Book.Formats.Pdf;

namespace MYBOOK.RegressionTests;

internal static class PdfSecurityPasswordRegression
{
    private static readonly byte[] PasswordPadding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    ];

    [ModuleInitializer]
    public static void Run()
    {
        TestRevision3Passwords();
        TestRevision5Passwords();

        Console.WriteLine(
            "PASS PDF non-empty passwords");
    }

    private static void TestRevision3Passwords()
    {
        var userPassword =
            Encoding.ASCII.GetBytes(
                "reader");
        var ownerPassword =
            Encoding.ASCII.GetBytes(
                "owner-secret");
        var id =
            Encoding.ASCII.GetBytes(
                "revision3-id");
        const int permissions = -4;
        const int keyLength = 16;

        var ownerEntry =
            CreateOwnerEntry(
                userPassword,
                ownerPassword,
                revision: 3,
                keyLength);
        var fileKey =
            CreateLegacyFileKey(
                userPassword,
                ownerEntry,
                permissions,
                id,
                revision: 3,
                keyLength,
                encryptMetadata: true);
        var userEntry =
            CreateUserEntry(
                id,
                revision: 3,
                fileKey);

        var encryption =
            new PdfDictionary(
                new Dictionary<string, PdfObject>(
                    StringComparer.Ordinal)
                {
                    ["Filter"] = new PdfName("Standard"),
                    ["V"] = new PdfNumber(2),
                    ["R"] = new PdfNumber(3),
                    ["Length"] = new PdfNumber(128),
                    ["O"] = new PdfString(ownerEntry),
                    ["U"] = new PdfString(userEntry),
                    ["P"] = new PdfNumber(permissions)
                });
        var trailer =
            CreateTrailer(id);
        var plain =
            Encoding.ASCII.GetBytes(
                "legacy password marker");
        var encrypted =
            Rc4(
                plain,
                CreateLegacyObjectKey(
                    fileKey,
                    objectNumber: 17,
                    generation: 2));

        foreach (var password in
                 new[]
                 {
                     userPassword,
                     ownerPassword
                 })
        {
            var handler =
                PdfStandardSecurityHandler.CreateForPasswordBytes(
                    encryption,
                    trailer,
                    password);
            var decrypted =
                handler.DecryptObject(
                    new PdfString(encrypted),
                    objectNumber: 17,
                    generation: 2) as PdfString
                ?? throw new InvalidOperationException(
                    "PDF legacy password regression не вернул PdfString.");

            if (!decrypted.Data.SequenceEqual(plain))
            {
                throw new InvalidOperationException(
                    "PDF legacy user/owner password не восстановил file key.");
            }
        }
    }

    private static void TestRevision5Passwords()
    {
        var userPassword =
            Encoding.UTF8.GetBytes(
                "reader-r5");
        var ownerPassword =
            Encoding.UTF8.GetBytes(
                "owner-r5");
        var fileKey =
            Enumerable
                .Range(0, 32)
                .Select(value => checked((byte)(value + 11)))
                .ToArray();

        var userValidationSalt =
            Encoding.ASCII.GetBytes("UVALSAL1");
        var userKeySalt =
            Encoding.ASCII.GetBytes("UKEYSAL1");
        var user =
            new byte[48];
        Sha256Concat(
                userPassword,
                userValidationSalt)
            .CopyTo(user, 0);
        userValidationSalt.CopyTo(user, 32);
        userKeySalt.CopyTo(user, 40);
        var userKey =
            Sha256Concat(
                userPassword,
                userKeySalt);
        var userEncryptedFileKey =
            EncryptAesCbcNoPadding(
                fileKey,
                userKey);

        var ownerValidationSalt =
            Encoding.ASCII.GetBytes("OVALSAL1");
        var ownerKeySalt =
            Encoding.ASCII.GetBytes("OKEYSAL1");
        var owner =
            new byte[48];
        Sha256Concat(
                ownerPassword,
                ownerValidationSalt,
                user)
            .CopyTo(owner, 0);
        ownerValidationSalt.CopyTo(owner, 32);
        ownerKeySalt.CopyTo(owner, 40);
        var ownerKey =
            Sha256Concat(
                ownerPassword,
                ownerKeySalt,
                user);
        var ownerEncryptedFileKey =
            EncryptAesCbcNoPadding(
                fileKey,
                ownerKey);

        const int permissions = -4;
        var permissionsBlock =
            new byte[16];
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
        permissionsBlock[12] = 9;
        permissionsBlock[13] = 10;
        permissionsBlock[14] = 11;
        permissionsBlock[15] = 12;

        var cryptFilter =
            new PdfDictionary(
                new Dictionary<string, PdfObject>(
                    StringComparer.Ordinal)
                {
                    ["Type"] = new PdfName("CryptFilter"),
                    ["CFM"] = new PdfName("AESV3"),
                    ["AuthEvent"] = new PdfName("DocOpen")
                });
        var encryption =
            new PdfDictionary(
                new Dictionary<string, PdfObject>(
                    StringComparer.Ordinal)
                {
                    ["Filter"] = new PdfName("Standard"),
                    ["V"] = new PdfNumber(5),
                    ["R"] = new PdfNumber(5),
                    ["Length"] = new PdfNumber(256),
                    ["O"] = new PdfString(owner),
                    ["U"] = new PdfString(user),
                    ["OE"] = new PdfString(ownerEncryptedFileKey),
                    ["UE"] = new PdfString(userEncryptedFileKey),
                    ["P"] = new PdfNumber(permissions),
                    ["EncryptMetadata"] = new PdfBoolean(true),
                    ["Perms"] =
                        new PdfString(
                            EncryptAesEcbNoPadding(
                                permissionsBlock,
                                fileKey)),
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
        var plain =
            Encoding.ASCII.GetBytes(
                "revision5 password marker");
        var encrypted =
            EncryptAesV3Object(
                plain,
                fileKey);

        foreach (var password in
                 new[]
                 {
                     userPassword,
                     ownerPassword
                 })
        {
            var handler =
                PdfStandardSecurityHandler.CreateForPasswordBytes(
                    encryption,
                    trailer,
                    password);
            var decrypted =
                handler.DecryptObject(
                    new PdfString(encrypted),
                    objectNumber: 41,
                    generation: 0) as PdfString
                ?? throw new InvalidOperationException(
                    "PDF revision 5 password regression не вернул PdfString.");

            if (!decrypted.Data.SequenceEqual(plain))
            {
                throw new InvalidOperationException(
                    "PDF revision 5 user/owner password не восстановил file key.");
            }
        }
    }

    private static PdfDictionary CreateTrailer(
        byte[] id)
    {
        return new PdfDictionary(
            new Dictionary<string, PdfObject>(
                StringComparer.Ordinal)
            {
                ["ID"] =
                    new PdfArray(
                        new PdfObject[]
                        {
                            new PdfString(id),
                            new PdfString(id)
                        })
            });
    }

    private static byte[] CreateOwnerEntry(
        byte[] userPassword,
        byte[] ownerPassword,
        int revision,
        int keyLength)
    {
        var digest =
            MD5.HashData(
                PadPassword(ownerPassword));

        if (revision >= 3)
        {
            for (var index = 0; index < 50; index++)
            {
                digest =
                    MD5.HashData(digest);
            }
        }

        var key =
            digest[..keyLength];
        var value =
            Rc4(
                PadPassword(userPassword),
                key);

        if (revision >= 3)
        {
            for (var iteration = 1; iteration <= 19; iteration++)
            {
                value =
                    Rc4(
                        value,
                        key
                            .Select(
                                item =>
                                    checked((byte)(item ^ iteration)))
                            .ToArray());
            }
        }

        return value;
    }

    private static byte[] CreateLegacyFileKey(
        byte[] password,
        byte[] ownerEntry,
        int permissions,
        byte[] id,
        int revision,
        int keyLength,
        bool encryptMetadata)
    {
        var padded =
            PadPassword(password);
        var appendMetadata =
            revision >= 4 &&
            !encryptMetadata;
        var material =
            new byte[
                32 +
                ownerEntry.Length +
                4 +
                id.Length +
                (appendMetadata ? 4 : 0)];

        padded.CopyTo(material, 0);
        ownerEntry.CopyTo(material, 32);
        BinaryPrimitives.WriteInt32LittleEndian(
            material.AsSpan(
                32 + ownerEntry.Length,
                4),
            permissions);
        id.CopyTo(
            material,
            36 + ownerEntry.Length);

        if (appendMetadata)
        {
            material[^4] = 0xFF;
            material[^3] = 0xFF;
            material[^2] = 0xFF;
            material[^1] = 0xFF;
        }

        var hash =
            MD5.HashData(material);

        if (revision >= 3)
        {
            for (var index = 0; index < 50; index++)
            {
                hash =
                    MD5.HashData(
                        hash.AsSpan(0, keyLength));
            }
        }

        return hash[..keyLength];
    }

    private static byte[] CreateUserEntry(
        byte[] id,
        int revision,
        byte[] fileKey)
    {
        if (revision == 2)
        {
            return Rc4(
                PasswordPadding,
                fileKey);
        }

        var seed =
            new byte[
                PasswordPadding.Length +
                id.Length];
        PasswordPadding.CopyTo(seed, 0);
        id.CopyTo(
            seed,
            PasswordPadding.Length);

        var value =
            Rc4(
                MD5.HashData(seed),
                fileKey);

        for (var iteration = 1; iteration <= 19; iteration++)
        {
            value =
                Rc4(
                    value,
                    fileKey
                        .Select(
                            item =>
                                checked((byte)(item ^ iteration)))
                        .ToArray());
        }

        return
        [
            .. value,
            .. new byte[16]
        ];
    }

    private static byte[] CreateLegacyObjectKey(
        byte[] fileKey,
        int objectNumber,
        int generation)
    {
        var material =
            new byte[fileKey.Length + 5];
        fileKey.CopyTo(material, 0);
        material[fileKey.Length] =
            checked((byte)(objectNumber & 0xFF));
        material[fileKey.Length + 1] =
            checked((byte)((objectNumber >> 8) & 0xFF));
        material[fileKey.Length + 2] =
            checked((byte)((objectNumber >> 16) & 0xFF));
        material[fileKey.Length + 3] =
            checked((byte)(generation & 0xFF));
        material[fileKey.Length + 4] =
            checked((byte)((generation >> 8) & 0xFF));

        var hash =
            MD5.HashData(material);
        return hash[
            ..Math.Min(
                fileKey.Length + 5,
                16)];
    }

    private static byte[] PadPassword(
        ReadOnlySpan<byte> password)
    {
        var output =
            new byte[32];
        var copied =
            Math.Min(
                password.Length,
                output.Length);
        password[..copied].CopyTo(output);

        if (copied < output.Length)
        {
            PasswordPadding
                .AsSpan(
                    0,
                    output.Length - copied)
                .CopyTo(
                    output.AsSpan(copied));
        }

        return output;
    }

    private static byte[] Rc4(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> key)
    {
        var state =
            Enumerable
                .Range(0, 256)
                .Select(value => checked((byte)value))
                .ToArray();
        var j = 0;

        for (var i = 0; i < 256; i++)
        {
            j =
                (j +
                 state[i] +
                 key[i % key.Length]) &
                0xFF;
            (state[i], state[j]) =
                (state[j], state[i]);
        }

        var output =
            new byte[data.Length];
        var x = 0;
        j = 0;

        for (var index = 0; index < data.Length; index++)
        {
            x = (x + 1) & 0xFF;
            j = (j + state[x]) & 0xFF;
            (state[x], state[j]) =
                (state[j], state[x]);
            output[index] =
                checked((byte)(
                    data[index] ^
                    state[
                        (state[x] + state[j]) &
                        0xFF]));
        }

        return output;
    }

    private static byte[] Sha256Concat(
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second)
    {
        var data =
            new byte[first.Length + second.Length];
        first.CopyTo(data);
        second.CopyTo(
            data.AsSpan(first.Length));
        return SHA256.HashData(data);
    }

    private static byte[] Sha256Concat(
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second,
        ReadOnlySpan<byte> third)
    {
        var data =
            new byte[
                first.Length +
                second.Length +
                third.Length];
        first.CopyTo(data);
        second.CopyTo(
            data.AsSpan(first.Length));
        third.CopyTo(
            data.AsSpan(
                first.Length +
                second.Length));
        return SHA256.HashData(data);
    }

    private static byte[] EncryptAesCbcNoPadding(
        byte[] data,
        byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = new byte[16];
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

    private static byte[] EncryptAesV3Object(
        byte[] data,
        byte[] key)
    {
        var iv =
            Enumerable
                .Range(0, 16)
                .Select(value => checked((byte)(0x60 + value)))
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
}
