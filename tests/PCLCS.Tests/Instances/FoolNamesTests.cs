using PCLCS.Instances;

namespace PCLCS.Tests.Instances;

public class FoolNamesTests
{
    [Theory]
    [InlineData("2.0", "2013 | 这个秘密计划了两年的更新将游戏推向了一个新高度！")]
    [InlineData("2.0-rc1", "2013 | 这个秘密计划了两年的更新将游戏推向了一个新高度！")]   // StartsWithF("2.0")
    [InlineData("15w14a", "2015 | 作为一款全年龄向的游戏，我们需要和平，需要爱与拥抱。")]
    [InlineData("1.rv-pre1", "2016 | 是时候将现代科技带入 Minecraft 了！")]
    [InlineData("3d shareware v1.34", "2019 | 我们从地下室的废墟里找到了这个开发于 1994 年的杰作！")]
    [InlineData("20w14infinite", "2020 | 我们加入了 20 亿个新的维度，让无限的想象变成了现实！")]   // StartsWithF("20w14inf")
    [InlineData("20w14∞", "2020 | 我们加入了 20 亿个新的维度，让无限的想象变成了现实！")]
    [InlineData("22w13oneblockatatime", "2022 | 一次一个方块更新！迎接全新的挖掘、合成与骑乘玩法吧！")]
    [InlineData("23w13a_or_b", "2023 | 研究表明：玩家喜欢作出选择——越多越好！")]
    [InlineData("24w14potato", "2024 | 毒马铃薯一直都被大家忽视和低估，于是我们超级加强了它！")]
    [InlineData("25w14craftmine", "2025 | 你可以合成任何东西——包括合成你的世界！")]
    [InlineData("26w14a", "2026 | 为什么需要物品栏？让方块们跟着你走吧！")]
    public void Known_fool_names_return_descriptions(string name, string expected)
    {
        // ModMinecraft.vb:905-930 名表逐字
        Assert.Equal(expected, FoolNames.Get(name));
    }

    [Theory]
    [InlineData("15W14A", "2015 | 作为一款全年龄向的游戏，我们需要和平，需要爱与拥抱。")]
    [InlineData("1.RV-PRE1", "2016 | 是时候将现代科技带入 Minecraft 了！")]
    public void Input_is_lowercased_before_lookup(string name, string expected)
    {
        // :906 先转小写再比对
        Assert.Equal(expected, FoolNames.Get(name));
    }

    [Theory]
    [InlineData("1.20.1")]
    [InlineData("23w14a")]
    [InlineData("")]
    [InlineData("2.1")]
    public void Non_fool_names_return_empty(string name)
    {
        Assert.Equal("", FoolNames.Get(name));
    }
}