namespace PCLCS.Instances;

/// <summary>
/// 愚人节版本名表。自 ModMinecraft.vb GetMcFoolName（ModMinecraft.vb:905-930）逐字搬运。
/// </summary>
public static class FoolNames
{
    /// <summary>
    /// 根据版本名获取对应的愚人节版本描述。非愚人节版本会返回空字符串。
    /// </summary>
    public static string Get(string vanillaName)
    {
        var name = vanillaName.Lower();
        if (name.StartsWithF("2.0")) return "2013 | 这个秘密计划了两年的更新将游戏推向了一个新高度！";
        if (name == "15w14a") return "2015 | 作为一款全年龄向的游戏，我们需要和平，需要爱与拥抱。";
        if (name == "1.rv-pre1") return "2016 | 是时候将现代科技带入 Minecraft 了！";
        if (name == "3d shareware v1.34") return "2019 | 我们从地下室的废墟里找到了这个开发于 1994 年的杰作！";
        if (name.StartsWithF("20w14inf") || name == "20w14∞") return "2020 | 我们加入了 20 亿个新的维度，让无限的想象变成了现实！";
        if (name == "22w13oneblockatatime") return "2022 | 一次一个方块更新！迎接全新的挖掘、合成与骑乘玩法吧！";
        if (name == "23w13a_or_b") return "2023 | 研究表明：玩家喜欢作出选择——越多越好！";
        if (name == "24w14potato") return "2024 | 毒马铃薯一直都被大家忽视和低估，于是我们超级加强了它！";
        if (name == "25w14craftmine") return "2025 | 你可以合成任何东西——包括合成你的世界！";
        return name == "26w14a" ? "2026 | 为什么需要物品栏？让方块们跟着你走吧！" : "";
    }
}