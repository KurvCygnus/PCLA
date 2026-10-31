using MeloongCore.Extensions;
using PCLCS.Settings;
using System.Text;

namespace PCLCS.Tests.Settings;

public class SettingsCryptoTests
{
    // Identify 现值（PCLA\Modules\ModSecret.vb:177-179 返回常量 "0000-0000-0000-0000"）。
    const string Identify = "0000-0000-0000-0000";
    static string Passphrase => "PCL" + Identify;

    [Fact]
    public void DeriveKey_matches_legacy_construction()
    {
        // VB: key.GetStableHashCode().ToString().EnsureLength("X"c, 8).Substring(0, 8)
        var expectedSeed = Passphrase.GetStableHashCode().ToString().EnsureLength('X', 8).Substring(0, 8);
        var key = SettingsCrypto.DeriveKey(Passphrase);
        Assert.Equal(expectedSeed, Encoding.UTF8.GetString(key));
        Assert.Equal(8, key.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("中文值")]
    [InlineData("{\"oauth\":\"token123\"}")]
    public void RoundTrip_preserves_unicode_and_json(string plain)
    {
        var cipher = SettingsCrypto.Encrypt(plain, Passphrase);
        Assert.NotEqual(plain, cipher);
        Assert.Equal(plain, SettingsCrypto.Decrypt(cipher, Passphrase));
    }

    [Fact]
    public void Decrypt_garbage_throws()
    {
        Assert.ThrowsAny<Exception>(() => SettingsCrypto.Decrypt("not-base64!!", Passphrase));
    }

    // golden 向量由旧 VB 算法（Settings.vb:434-447）在 net48 实测产出，钉死防算法漂移。
    [Theory]
    [InlineData("", "wlsufEa1cXw=")]
    [InlineData("hello", "XesOm/hMiQk=")]
    [InlineData("中文值", "AiMeApceX4cLV7/kFBLMvA==")]
    public void Encrypt_matches_legacy_byte_for_byte(string plain, string expected)
    {
        Assert.Equal(expected, SettingsCrypto.Encrypt(plain, Passphrase));
    }
}