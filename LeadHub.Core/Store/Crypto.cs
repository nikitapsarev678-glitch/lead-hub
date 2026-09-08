using System.IO;
using System.Security.Cryptography;

namespace LeadHub.Core.Store;

/// <summary>Шифрование секретов (cookies аккаунтов): AES-GCM. Ключ — из файла-ключа или пароля.</summary>
public static class Crypto
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>Загрузить ключ из keyFile или создать новый (32 байта случайных).</summary>
    public static byte[] LoadOrCreateKey(string keyFile)
    {
        if (File.Exists(keyFile))
        {
            var existing = File.ReadAllBytes(keyFile);
            if (existing.Length == 32) return existing;
        }
        var key = RandomNumberGenerator.GetBytes(32);
        Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
        File.WriteAllBytes(keyFile, key);
        return key;
    }

    public static byte[] DeriveKeyFromPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);

    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, cipher, tag);
        using var ms = new MemoryStream();
        ms.Write(nonce);
        ms.Write(tag);
        ms.Write(cipher);
        return ms.ToArray();
    }

    public static byte[] Decrypt(byte[] payload, byte[] key)
    {
        using var aes = new AesGcm(key, TagSize);
        var nonce = payload[..NonceSize];
        var tag = payload[NonceSize..(NonceSize + TagSize)];
        var cipher = payload[(NonceSize + TagSize)..];
        var plain = new byte[cipher.Length];
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    public static byte[] EncryptString(string text, byte[] key) => Encrypt(Encoding.UTF8.GetBytes(text), key);

    public static string DecryptString(byte[] payload, byte[] key) => Encoding.UTF8.GetString(Decrypt(payload, key));

    /// <summary>Файл экспорта аккаунта .lhaccount: salt + шифрованный JSON (AES-GCM, ключ из пароля).</summary>
    public static void WriteAccountPackage(string path, string json, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = DeriveKeyFromPassword(password, salt);
        var payload = EncryptString(json, key);
        using var ms = new MemoryStream();
        ms.Write(salt);
        ms.Write(payload);
        File.WriteAllBytes(path, ms.ToArray());
    }

    public static string ReadAccountPackage(string path, string password)
    {
        var bytes = File.ReadAllBytes(path);
        var salt = bytes[..16];
        var key = DeriveKeyFromPassword(password, salt);
        return DecryptString(bytes[16..], key);
    }
}
