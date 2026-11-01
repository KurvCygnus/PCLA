using PCLCS.Minecraft.Download;
using PCLCS.Net;
using PCLCS.Settings;
using System.Text.Json.Nodes;

namespace PCLCS.Download;

/// <summary>
/// 资源检索请求的数据面（自宿主层 ModDownload.vb:911-944 与 ModDownloadLib.vb:1440-1492 迁入的请求核）。
/// 线程派生、刷新标记与缓存目录等宿主层全局状态经 provider / 宿主入口接线（与 NetRequest.CdnSigner 同模式）。
/// </summary>
public static class ModRequests
{
    /// <summary>UI 线程判定回调（宿主层 RunInUi 接线；null 视为非 UI 线程，即测试直用语义）。</summary>
    public static Func<bool>? RunInUiProvider { get; set; }

    /// <summary>Forge 推荐版缓存目录提供者（宿主层接线 PathTemp &amp; "Cache\"；测试注入临时目录）。null 时在取值处抛出未接线异常。</summary>
    public static Func<string>? ForgeRecommendedCacheFolder { get; set; }

    /// <summary>
    /// 对可能涉及 Mod 镜像源的请求进行处理（ModDownload.vb:911-944 逐行移植），返回 JSON。
    /// 调用 NetRequestByClient，按 Settings ToolDownloadMod 三档策略排列 MCIM 镜像源；逐源尝试，全部失败时归并异常。
    /// </summary>
    public static JsonNode? DlModRequest(string url, HttpMethod? method = null, string? content = null, string? contentType = null)
    {
        var urls = BuildModRequestUrls(url, DownloadSources.Mod(url), SettingsService.Get<int>("ToolDownloadMod"));
        var errors = new List<Exception>();
        foreach (var (sourceUrl, timeoutSeconds) in urls)
        {
            try
            {
                var body = NetRequest.NetRequestByClient(sourceUrl, new NetRequestOptions
                {
                    Method = method,
                    Content = content,
                    ContentType = contentType,
                    Accept = "*/*", //VB 便捷入口 NetRequestByClient 的默认值（ModNet.vb:21）
                    TimeoutMs = timeoutSeconds * 1000,
                    Encoding = Encoding.UTF8,
                    RequireJson = true,
                    RunInUi = RunInUiProvider
                });
                return JsonNode.Parse(body); //VB DeserializeJson()：RequireJson 已保证括号闭合的完整 JSON
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }
        throw new AggregateException(errors);
    }

    /// <summary>Mod 镜像源的三档源排列（ModDownload.vb:914-935 逐字）。internal 供测试对三档策略做免网络的排列断言。</summary>
    internal static List<(string Url, int TimeoutSeconds)> BuildModRequestUrls(string url, string mcimUrl, int toolDownloadMod)
    {
        var urls = new List<(string Url, int TimeoutSeconds)>();
        var initialTimeout = url.Contains("modrinth") ? 20 : 10; //至少 20s，要不然 Modrinth 返回不过来
        if (mcimUrl != url)
        {
            switch (toolDownloadMod)
            {
                case 0:
                    urls.Add((mcimUrl, 10));
                    urls.Add((mcimUrl, 10));
                    urls.Add((url, 30));
                    break;
                case 1:
                    urls.Add((url, initialTimeout));
                    urls.Add((mcimUrl, 10));
                    urls.Add((url, 30));
                    urls.Add((mcimUrl, 30));
                    break;
                default:
                    urls.Add((url, initialTimeout));
                    urls.Add((url, 30));
                    urls.Add((mcimUrl, 30));
                    break;
            }
        }
        else
        {
            urls.Add((url, initialTimeout));
            urls.Add((url, 30));
        }
        return urls;
    }

    /// <summary>
    /// 尝试获取某个 MC 版本对应的 Forge 推荐版本（ModDownloadLib.vb:1477-1492 逐行移植）。如果不可用会返回 null。
    /// </summary>
    public static string? ForgeRecommendedGet(string? mcVersionName)
    {
        try
        {
            if (mcVersionName is null) return null;
            var list = FileUtils.TryReadAsString(Path.Combine(ForgeRecommendedCacheFolder!(), "ForgeRecommendedList.json")); //未接线时 NRE 走 Catch
            if (string.IsNullOrEmpty(list))
            {
                Logger.Info("没有 Forge 推荐版本缓存文件");
                return null;
            }
            var json = JsonNode.Parse(list!) as JsonObject; //VB JObject 强转：根非对象时走 Catch 返回 null；net48 引用程序集缺 NotNullWhen 标注，此 ! 为 Roslyn 所需（net8 下冗余）
            if (json is null || !mcVersionName.Contains('.') || !json.ContainsKey(mcVersionName)) return null;
            return json[mcVersionName]?.ToString() ?? ""; //VB If(Json(McVersionName), "").ToString：缺失/JSON null 均为空串
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"获取 Forge 推荐版本失败（{mcVersionName ?? "null"}）");
            return null;
        }
    }

    /// <summary>
    /// 拉取并重写 Forge 推荐版本缓存（ModDownloadLib.vb:1450-1465 的决策核）。
    /// 失败直接抛出；“开始/成功”日志、线程派生与 Catch 留在宿主层 RunInNewThread 入口（计划裁定 3）。
    /// </summary>
    public static void ForgeRecommendedRefreshCore()
    {
        var result = NetRequest.NetRequestByClientRetry("https://bmclapi2.bangbang93.com/forge/promos", requireJson: true);
        if (result.Length < 1000) throw new Exception($"获取的结果过短（{result}）");
        var resultJson = JsonNode.Parse(result); //VB DeserializeJson()
        //获取所有推荐版本列表
        var recommendedList = new List<string>();
        foreach (var item in (resultJson as JsonArray)!) //根非数组时在取元素处抛出；条目非对象在强转处抛出（对照 VB 遍历强转），皆走宿主层 Catch
        {
            var version = (JsonObject)item!; //STJ 对非对象节点的强转抛 InvalidOperationException，与 VB InvalidCastException 同归宿主层 Catch
            if (version["name"] is null || version["build"] is null) continue;
            var name = (string?)version["name"]; //VB: Dim Name As String = Version("name")；非字符串值同样走 Catch
            if (!name.EndsWithF("-recommended")) continue;
            //内容为："1.15.2":"31.2.0"
            recommendedList.Add("\"" + name!.Replace("-recommended", "\":\"" + version["build"]!["version"]! + "\""));
        }
        if (recommendedList.Count < 5) throw new Exception($"获取的推荐版本数过少（{result}）");
        //保存
        FileUtils.Write(Path.Combine(ForgeRecommendedCacheFolder!(), "ForgeRecommendedList.json"), "{" + string.Join(",", recommendedList) + "}");
    }
}
