using System.Globalization;

namespace PCLCS.Instances;

/// <summary>
/// 实例扫描与卡片分类排序的纯函数面（自 VB ModMinecraft.vb:1141-1324 移植）。
/// 只做纯计算：PCL.ini 写入、Loader 编排与 UI 均留在 VB。
/// </summary>
public static class InstanceScanner
{
    /// <summary>卡片种类数值，与 VB McInstanceCardType（ModMinecraft.vb:1352-1360）一一对应。</summary>
    private const int CardStar = -1;
    private const int CardAuto = 0;
    private const int CardHidden = 1;
    private const int CardApi = 2;
    private const int CardOriginalLike = 3;
    private const int CardRubbish = 4;
    private const int CardFool = 5;
    private const int CardError = 6;

    /// <summary>
    /// 扫描 versions\ 下的版本文件夹（VB :1145-1168 的文件系统规则，分支顺序一致）：
    /// 跳过空文件夹；cache/BLClient/PCL 无同名 Json 跳过；.pclignore 首次加载清理（#2781）、后续加载跳过。
    /// </summary>
    /// <param name="mcFolder">Minecraft 根文件夹，以 \ 结尾。</param>
    /// <param name="isFirstLoad">是否为本次打开 PCL 后的第一次加载；控制 .pclignore 的清理行为。</param>
    /// <returns>以 \ 结尾的版本文件夹路径列表（VB 由调用方先行确保 versions 存在，这里缺失时返回空列表）。
    /// 与 VB 一致，路径可能带 <c>\\?\</c> 前缀，由消费方的 <see cref="InstancePaths.ResolveVersionFolder"/> 归一。</returns>
    public static List<string> ScanVersionFolders(string mcFolder, bool isFirstLoad)
    {
        var versionFolders = new List<string>();
        string versionsFolder = Path.Combine(mcFolder, "versions");
        if (!DirectoryUtils.Exists(versionsFolder)) return versionFolders;
        foreach (var versionFolderInfo in DirectoryUtils.GetInfo(versionsFolder).GetDirectories().OrderBy(static d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            // 空文件夹跳过（:1146-1149，仅检查文件）
            if (!versionFolderInfo.Exists || !versionFolderInfo.EnumerateFiles().Any())
            {
                Logger.Info($"跳过空文件夹：{versionFolderInfo.FullName}");
                continue;
            }
            // 可能不是版本文件夹的项目跳过（:1150-1154）
            if ((versionFolderInfo.Name is "cache" or "BLClient" or "PCL") &&
                !FileUtils.Exists(Path.Combine(versionFolderInfo.FullName, versionFolderInfo.Name + ".json")))
            {
                Logger.Info($"跳过可能不是版本文件夹的项目：{versionFolderInfo.FullName}");
                continue;
            }
            string versionFolder = PathUtils.AddSlashSuffix(versionFolderInfo.FullName);
            // 忽略标记：首次加载清理残留，后续加载跳过对应版本（:1156-1164，#2781）
            if (FileUtils.Exists(versionFolder + ".pclignore"))
            {
                if (isFirstLoad)
                {
                    Logger.Info($"清理残留的忽略项目：{versionFolder}");
                    FileUtils.Delete(versionFolder + ".pclignore");
                } else
                {
                    Logger.Info($"跳过要求忽略的项目：{versionFolder}");
                    continue;
                }
            }
            versionFolders.Add(versionFolder);
        }
        return versionFolders;
    }

    /// <summary>
    /// 分类 + 排序 + 卡片序（VB :1171-1324 的纯逻辑面，分支顺序照抄）。
    /// </summary>
    /// <param name="instances">待分类的实例列表，不会被修改。</param>
    /// <returns>按卡片序（Star,API,OriginalLike,Rubbish,Fool,Error,Hidden）排列的非空分组；
    /// cardType 取 VB McInstanceCardType 数值（-1 Star … 6 Error，1 Hidden）。</returns>
    public static List<(int cardType, List<InstanceCore> items)> ClassifyAndSort(List<InstanceCore> instances)
    {
        // 未经过自定义的版本列表（:1174）
        var cards = new Dictionary<int, List<InstanceCore>>();
        var remaining = new List<InstanceCore>(instances);

        // 单独列出收藏的版本（:1177-1184）
        var star = new List<InstanceCore>();
        foreach (var instance in remaining.ToList())
        {
            if (!instance.IsStar) continue;
            if (instance.DisplayType == CardHidden) continue;
            star.Add(instance);
            remaining.Remove(instance);
        }
        if (star.Count > 0) cards.Add(CardStar, star);

        // 预先筛选出愚人节和错误的版本（:1186-1188）
        FilterToCard(remaining, cards, [InstanceState.Error], CardError);
        FilterToCard(remaining, cards, [InstanceState.Fool], CardFool);

        // 筛选 API 版本（:1190-1191）
        FilterToCard(remaining, cards, [InstanceState.Forge, InstanceState.NeoForge, InstanceState.LiteLoader, InstanceState.Fabric], CardApi);

        // 将老版本预先分类入不常用，只剩余原版、快照、OptiFine（:1193-1196）
        var useful = new List<InstanceCore>();
        var rubbish = new List<InstanceCore>();
        FilterToList(remaining, [InstanceState.Old], rubbish);

        // 确认最新版本，若为快照则加入常用列表（:1198-1205；Enumerable.MaxBy 等值取先）
        InstanceCore? largest = null;
        foreach (var instance in remaining)
        {
            if (instance.State is not (InstanceState.Original or InstanceState.Snapshot)) continue;
            if (largest is null || instance.Version.ReleaseTime > largest.Version.ReleaseTime) largest = instance;
        }
        if (largest is not null && largest.State == InstanceState.Snapshot)
        {
            useful.Add(largest);
            remaining.Remove(largest);
        }

        // 将剩余的快照全部拖进不常用列表（:1207-1208）
        FilterToList(remaining, [InstanceState.Snapshot], rubbish);

        // 获取每个 Drop 下最新的原版与 OptiFine（:1210-1226）
        var newer = new Dictionary<(int Drop, InstanceState State), InstanceCore>();
        var existDrops = new List<int>();
        foreach (var instance in remaining)
        {
            if (!instance.Version.Valid) continue;
            if (!existDrops.Contains(instance.Version.Drop)) existDrops.Add(instance.Version.Drop);
            var key = (instance.Version.Drop, instance.State);
            if (!newer.TryGetValue(key, out var current))
            {
                newer.Add(key, instance);
                continue;
            }
            if (instance.State == InstanceState.OptiFine)
            {
                if (instance.Version.OptiFineCode > current.Version.OptiFineCode) newer[key] = instance;   // OptiFine 根据版本号判断
            } else if (instance.Version.ReleaseTime > current.Version.ReleaseTime)
            {
                newer[key] = instance;   // 原版根据发布时间判断
            }
        }

        // 将每个 Drop 下的最常规版本加入（:1228-1250）
        foreach (int drop in existDrops)
        {
            bool hasOptiFine = newer.ContainsKey((drop, InstanceState.OptiFine));
            bool hasOriginal = newer.ContainsKey((drop, InstanceState.Original));
            if (hasOptiFine && hasOriginal)
            {
                // 同时存在 OptiFine 与原版
                var vanilla = newer[(drop, InstanceState.Original)];
                var optiFine = newer[(drop, InstanceState.OptiFine)];
                if (vanilla.Version.Drop > optiFine.Version.Drop)
                {
                    // 仅在原版比 OptiFine 更新时才加入原版（同组内 Drop 恒等，分支实际不可达，照抄 VB）
                    useful.Add(vanilla);
                    remaining.Remove(vanilla);
                }
                useful.Add(optiFine);
                remaining.Remove(optiFine);
            } else if (hasOptiFine)
            {
                // 没有原版，直接加入 OptiFine
                useful.Add(newer[(drop, InstanceState.OptiFine)]);
                remaining.Remove(newer[(drop, InstanceState.OptiFine)]);
            } else if (hasOriginal)
            {
                // 没有 OptiFine，直接加入原版
                useful.Add(newer[(drop, InstanceState.Original)]);
                remaining.Remove(newer[(drop, InstanceState.Original)]);
            }
        }

        // 将剩余的东西添加进去（:1252-1255）
        rubbish.AddRange(remaining);
        if (useful.Count > 0) cards.Add(CardOriginalLike, useful);
        if (rubbish.Count > 0) cards.Add(CardRubbish, rubbish);

        // 按照自定义版本分类重新添加（:1257-1264）
        var results = new Dictionary<int, List<InstanceCore>>();
        foreach (var pair in cards)
        {
            foreach (var instance in pair.Value)
            {
                int realType = instance.DisplayType == CardAuto || pair.Key == CardStar ? pair.Key : instance.DisplayType;
                if (!results.TryGetValue(realType, out var list)) results.Add(realType, list = []);
                list.Add(instance);
            }
        }

        // 卡片排序（:1271-1278）
        var ordered = new List<(int cardType, List<InstanceCore> items)>();
        foreach (int sortRule in new[] { CardStar, CardApi, CardOriginalLike, CardRubbish, CardFool, CardError, CardHidden })
        {
            if (results.TryGetValue(sortRule, out var items)) ordered.Add((sortRule, items));
        }

        // 版本排序（:1280-1305，仅前五种卡片参与）
        var sorted = new List<(int cardType, List<InstanceCore> items)>(ordered.Count);
        foreach (var (cardType, items) in ordered)
        {
            sorted.Add(cardType is CardStar or CardApi or CardOriginalLike or CardRubbish or CardFool
                ? (cardType, SortByComparison(items, CompareInstances))
                : (cardType, items));
        }
        return sorted;
    }

    /// <summary>
    /// 卡片缓存辨识码（VB :979 同源哈希；VB 负责写 PCL.ini）。
    /// </summary>
    /// <param name="mcFolder">Minecraft 根文件夹，以 \ 结尾；不参与哈希，仅为与 ScanVersionFolders 对称的调用面。</param>
    /// <param name="folderNames">versions\ 下的文件夹名列表。</param>
    /// <param name="cacheVersion">缓存版本常量，默认取 Versions.McInstanceCacheVersion。</param>
    /// <returns>辨识码文本；文件夹列表为空时返回空串（对应 VB :971-973 直接清空缓存的分支）。</returns>
    public static string CacheCheckCode(string mcFolder, IReadOnlyList<string> folderNames, int cacheVersion = Versions.McInstanceCacheVersion)
    {
        if (folderNames.Count == 0) return "";
        // VB :979：(Versions.McInstanceCacheVersion & "#" & FolderList.Join("#"c)).GetStableHashCode() Mod (Integer.MaxValue - 1)
        string source = cacheVersion.ToString(CultureInfo.InvariantCulture) + "#" + string.Join("#", folderNames);
        return ((long) (source.GetStableHashCode() % (int.MaxValue - 1))).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>筛选特定种类的版本，并直接添加为卡片（VB McInstanceFilter 第一重载 :1128-1135）。</summary>
    private static void FilterToCard(List<InstanceCore> remaining, Dictionary<int, List<InstanceCore>> target, InstanceState[] formula, int cardType)
    {
        var keep = remaining.Where(v => formula.Contains(v.State)).ToList();
        if (keep.Count == 0) return;
        target.Add(cardType, keep);
        remaining.RemoveAll(v => formula.Contains(v.State));   // 与 VB Except(KeepList) 等价（引用唯一）
    }

    /// <summary>筛选特定种类的版本，并增加入一个已有列表中（VB McInstanceFilter 第二重载 :1142-1148）。</summary>
    private static void FilterToList(List<InstanceCore> remaining, InstanceState[] formula, List<InstanceCore> keep)
    {
        keep.AddRange(remaining.Where(v => formula.Contains(v.State)));
        if (keep.Count > 0) remaining.RemoveAll(v => formula.Contains(v.State));
    }

    /// <summary>版本排序比较链（:1285-1304）：发布时间 → 组件种类 → 组件版本 → 名称；返回前者是否应排在前。</summary>
    private static bool CompareInstances(InstanceCore left, InstanceCore right)
    {
        // 发布时间（任一边年份 >= 2000 才参与）
        if ((left.Version.ReleaseTime.Year >= 2000 || right.Version.ReleaseTime.Year >= 2000) &&
            left.Version.ReleaseTime != right.Version.ReleaseTime) return left.Version.ReleaseTime > right.Version.ReleaseTime;
        // 附加组件种类
        if (left.Version.ModLoaders.HasFlagF(ModLoaders.Fabric) != right.Version.ModLoaders.HasFlagF(ModLoaders.Fabric))
            return left.Version.ModLoaders.HasFlagF(ModLoaders.Fabric);
        if (left.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) != right.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge))
            return left.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge);
        if (left.Version.ModLoaders.HasFlagF(ModLoaders.Forge) != right.Version.ModLoaders.HasFlagF(ModLoaders.Forge))
            return left.Version.ModLoaders.HasFlagF(ModLoaders.Forge);
        if (left.Version.HasOptiFine != right.Version.HasOptiFine) return left.Version.HasOptiFine;
        if (left.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader) != right.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader))
            return left.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader);
        // 附加组件版本
        if (GetComponentCode(left) != GetComponentCode(right)) return GetComponentCode(left) > GetComponentCode(right);
        // 名称（VB Option Compare Binary 下的字符串比较即顺序比较）
        return string.CompareOrdinal(left.Name, right.Name) > 0;
    }

    /// <summary>排序组件序数（:1283-1288）：优先类 Forge 版本，其次 OptiFine。</summary>
    private static int GetComponentCode(InstanceCore instance)
    {
        if (instance.Version.ForgelikeCode > 0) return instance.Version.ForgelikeCode;
        return instance.Version.HasOptiFine ? instance.Version.OptiFineCode : 0;
    }

    /// <summary>VB SortByComparison（ModBase.vb:959-970）的移植：等值保序的选择排序，SortRule 判定前者应否排前。</summary>
    private static List<T> SortByComparison<T>(List<T> list, Func<T, T, bool> sortRule)
    {
        var remaining = new List<T>(list);
        var sorted = new List<T>(list.Count);
        while (remaining.Count > 0)
        {
            var highest = remaining[0];
            for (int i = 1; i < remaining.Count; i++)
            {
                if (sortRule(remaining[i], highest)) highest = remaining[i];
            }
            remaining.Remove(highest);
            sorted.Add(highest);
        }
        return sorted;
    }
}