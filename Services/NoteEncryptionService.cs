using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using GitHubTopicReports.Models;

namespace GitHubTopicReports.Services;

public sealed class NoteCryptoKey : IDisposable
{
    internal NoteCryptoKey(byte[] key, byte[] salt, int iterations)
    {
        Key = key;
        Salt = salt;
        Iterations = iterations;
    }

    internal byte[] Key { get; }
    internal byte[] Salt { get; }
    internal int Iterations { get; }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(Key);
        CryptographicOperations.ZeroMemory(Salt);
    }
}

public static class NoteEncryptionService
{
    private const int DefaultIterations = 310_000;
    private const int MinIterations = 100_000;
    private const int MaxIterations = 2_000_000;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    public static NoteCryptoKey CreateKey(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new NoteCryptoKey(
            DeriveKey(password, salt, DefaultIterations),
            salt,
            DefaultIterations);
    }

    public static NoteCryptoKey RestoreKey(
        string password,
        EncryptedNotePayload payload)
    {
        if (payload.Version != 1 ||
            payload.Iterations is < MinIterations or > MaxIterations)
        {
            throw new InvalidDataException("加密笔记格式不受支持。");
        }

        var salt = Decode(payload.Salt, SaltSize);
        return new NoteCryptoKey(
            DeriveKey(password, salt, payload.Iterations),
            salt,
            payload.Iterations);
    }

    public static EncryptedNotePayload Encrypt(NoteContent content, NoteCryptoKey key)
    {
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(content);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(key.Key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        return new EncryptedNotePayload
        {
            Version = 1,
            Iterations = key.Iterations,
            Salt = Convert.ToBase64String(key.Salt),
            Nonce = Convert.ToBase64String(nonce),
            Ciphertext = Convert.ToBase64String(ciphertext),
            Tag = Convert.ToBase64String(tag)
        };
    }

    public static NoteContent Decrypt(
        EncryptedNotePayload payload,
        NoteCryptoKey key)
    {
        var nonce = Decode(payload.Nonce, NonceSize);
        var ciphertext = Decode(payload.Ciphertext, null);
        var tag = Decode(payload.Tag, TagSize);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key.Key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return JsonSerializer.Deserialize<NoteContent>(plaintext)
                ?? throw new InvalidDataException("加密笔记内容为空。");
        }
        catch (Exception exception) when (
            exception is CryptographicException or JsonException or InvalidDataException)
        {
            throw new NotePasswordException(exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("密码不能为空。");
        }

        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            KeySize);
    }

    private static byte[] Decode(string value, int? expectedLength)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("加密笔记数据损坏。", exception);
        }

        if (expectedLength is { } length && bytes.Length != length)
        {
            throw new InvalidDataException("加密笔记数据损坏。");
        }

        return bytes;
    }
}

public sealed class NotePasswordException : Exception
{
    public NotePasswordException(Exception innerException)
        : base("密码错误或笔记数据已损坏。", innerException)
    {
    }
}
