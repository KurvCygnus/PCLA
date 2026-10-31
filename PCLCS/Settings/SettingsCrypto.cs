using System.Security.Cryptography;

namespace PCLCS.Settings;

/// <summary>
/// DES 互通加密，与旧 VB Settings.DesEncrypt/DesDecrypt 逐字节兼容。
/// </summary>
public static class SettingsCrypto
{
    //* 与 VB Settings.desInitialVector 同源；改一处必须同步另一处，否则旧数据无法解密
    static readonly byte[] Iv = Encoding.UTF8.GetBytes("95168702");

    /// <summary>由口令推导 8 字节 DES 密钥（VB Settings.DesEncrypt/DesDecrypt 同源算法）。</summary>
    public static byte[] DeriveKey(string passphrase)
    {
        // VB: key.GetStableHashCode().ToString().EnsureLength("X"c, 8).Substring(0, 8)
        var seed = passphrase.GetStableHashCode().ToString().EnsureLength('X', 8).Substring(0, 8);
        return Encoding.UTF8.GetBytes(seed);
    }

    public static string Encrypt(string plain, string passphrase)
    {
        using var des = DES.Create();
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, des.CreateEncryptor(DeriveKey(passphrase), Iv), CryptoStreamMode.Write))
        {
            var inData = Encoding.UTF8.GetBytes(plain);
            cs.Write(inData, 0, inData.Length);
            cs.FlushFinalBlock();
            return Convert.ToBase64String(ms.ToArray());
        }
    }

    public static string Decrypt(string cipher, string passphrase)
    {
        using var des = DES.Create();
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, des.CreateDecryptor(DeriveKey(passphrase), Iv), CryptoStreamMode.Write))
        {
            var inData = Convert.FromBase64String(cipher);
            cs.Write(inData, 0, inData.Length);
            cs.FlushFinalBlock();
            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}