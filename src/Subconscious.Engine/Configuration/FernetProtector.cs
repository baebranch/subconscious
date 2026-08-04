using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Subconscious.Engine.Configuration;

/// <summary>RFC 1288 Fernet implementation compatible with Python cryptography.fernet.Fernet.</summary>
internal sealed class FernetProtector
{
    private const byte Version = 0x80;
    private const int SigningKeyLength = 16;
    private const int EncryptionKeyLength = 16;
    private const int HeaderLength = 1 + sizeof(ulong) + 16;
    private const int SignatureLength = 32;
    private readonly byte[] _signingKey;
    private readonly byte[] _encryptionKey;

    public FernetProtector(string encodedKey)
    {
        var key = DecodeBase64Url(encodedKey);
        if (key.Length != SigningKeyLength + EncryptionKeyLength)
        {
            throw new ModelConfigurationStoreException("The Subconscious encryption key is not a valid Fernet key.");
        }

        _signingKey = key[..SigningKeyLength];
        _encryptionKey = key[SigningKeyLength..];
    }

    public byte[] Encrypt(string plaintext)
    {
        var iv = RandomNumberGenerator.GetBytes(16);
        var encrypted = EncryptAes(Encoding.UTF8.GetBytes(plaintext), iv);
        var token = new byte[HeaderLength + encrypted.Length + SignatureLength];
        token[0] = Version;
        BinaryPrimitives.WriteUInt64BigEndian(token.AsSpan(1, sizeof(ulong)), (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        iv.CopyTo(token, 1 + sizeof(ulong));
        encrypted.CopyTo(token, HeaderLength);

        var signature = HMACSHA256.HashData(_signingKey, token.AsSpan(0, token.Length - SignatureLength));
        signature.CopyTo(token, token.Length - SignatureLength);
        return Encoding.ASCII.GetBytes(EncodeBase64Url(token));
    }

    public string Decrypt(ReadOnlySpan<byte> encodedToken)
    {
        try
        {
            var token = DecodeBase64Url(Encoding.ASCII.GetString(encodedToken));
            if (token.Length < HeaderLength + 16 + SignatureLength || token[0] != Version)
            {
                throw new ModelConfigurationStoreException("data.enc is not a valid Fernet payload.");
            }

            var signedDataLength = token.Length - SignatureLength;
            var expectedSignature = HMACSHA256.HashData(_signingKey, token.AsSpan(0, signedDataLength));
            if (!CryptographicOperations.FixedTimeEquals(expectedSignature, token.AsSpan(signedDataLength)))
            {
                throw new ModelConfigurationStoreException("data.enc could not be authenticated with the Subconscious encryption key.");
            }

            var ciphertext = token.AsSpan(HeaderLength, signedDataLength - HeaderLength).ToArray();
            return Encoding.UTF8.GetString(DecryptAes(ciphertext, token.AsSpan(1 + sizeof(ulong), 16).ToArray()));
        }
        catch (ModelConfigurationStoreException)
        {
            throw;
        }
        catch (CryptographicException exception)
        {
            throw new ModelConfigurationStoreException("data.enc could not be decrypted with the Subconscious encryption key.", exception);
        }
        catch (FormatException exception)
        {
            throw new ModelConfigurationStoreException("data.enc is not a valid Fernet payload.", exception);
        }
    }

    private byte[] EncryptAes(byte[] plaintext, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }

    private byte[] DecryptAes(byte[] ciphertext, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    }

    private static string EncodeBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string encoded)
    {
        var normalized = encoded.Trim().Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException("Invalid base64url length."),
        };
        return Convert.FromBase64String(normalized);
    }
}
