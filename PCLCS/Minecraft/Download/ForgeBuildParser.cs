using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace PCLCS.Minecraft.Download;

/// <summary>Forge 构建条目，字段与 DlForgeVersionEntry 一一对应。</summary>
public sealed class ForgeBuildEntry : IComparable<ForgeBuildEntry> {
    /// <summary>创建条目；1.8.9 的三个遗留构建与 1.7.10 的高构建号会补上分支。</summary>
    /// <param name="version">可读的版本名，如 47.1.0。</param>
    /// <param name="branch">文件版本的分支后缀；无分支时为 null。</param>
    /// <param name="inherit">对应的 Minecraft 版本。</param>
    public ForgeBuildEntry(string version, string? branch, string inherit) {
        //* 三个 1.8.9 遗留构建在官方源里没有正确分支，旧实现按硬编码修正。
        if (version is "11.15.1.2318" or "11.15.1.1902" or "11.15.1.1890") branch = "1.8.9";
        if (branch is null && inherit == "1.7.10" && int.Parse(version.Split('.')[3]) >= 1300) branch = "1.7.10";
        VersionName = version;
        Version = new Version(version);
        Inherit = inherit;
        Branch = branch;
        FileVersion = branch is null ? version : version + "-" + branch;
    }

    /// <summary>可读的版本名，如 47.1.0。</summary>
    public string VersionName { get; }

    /// <summary>标准化后的版本号，仅用于比较与排序。</summary>
    public Version Version { get; }

    /// <summary>对应的 Minecraft 版本。</summary>
    public string Inherit { get; }

    /// <summary>文件版本的分支后缀，可能是修正后的值；无分支时为 null。</summary>
    public string? Branch { get; }

    /// <summary>发布时间，格式为 yyyy/MM/dd HH:mm。</summary>
    public string? ReleaseTime { get; internal set; }

    /// <summary>文件的 MD5 或 SHA1。</summary>
    public string? Hash { get; internal set; }

    /// <summary>是否为推荐版本。</summary>
    public bool IsRecommended { get; internal set; }

    /// <summary>安装类型：installer、universal、client 或 unknown。</summary>
    public string Category { get; internal set; } = "unknown";

    /// <summary>用于下载的文件版本名，可能在 VersionName 基础上添加了分支。</summary>
    public string FileVersion { get; }

    /// <summary>按标准化版本号排序，同版本时再比较可读版本名。</summary>
    public int CompareTo(ForgeBuildEntry? other) {
        if (other is null) return 1;
        return Version != other.Version
            ? Version.CompareTo(other.Version)
            : McUtils.CompareVersion(VersionName, other.VersionName);
    }
}

/// <summary>Forge 构建列表解析。</summary>
public static class ForgeBuildParser {
    /// <summary>解析 Forge 官方下载页中的构建列表。</summary>
    /// <param name="html">官方下载页的 HTML。</param>
    /// <param name="inherit">对应的 Minecraft 版本。</param>
    /// <exception cref="DownloadSourceException">响应过短、缺少构建表、发布时间异常或没有可用构建。</exception>
    public static IReadOnlyList<ForgeBuildEntry> ParseOfficialHtml(string html, string inherit) {
        if (html.Length < 1000) {
            throw new DownloadSourceException(DownloadFailureKind.TooFewEntries,
                $"Forge 官方源响应只有 {html.Length} 个字符", "Forge 官方源", DownloadDiagnostics.Snippet(html));
        }
        var tableEnd = html.LastIndexOf("</table>", StringComparison.Ordinal);
        if (tableEnd < 0) {
            throw new DownloadSourceException(DownloadFailureKind.InvalidPayload,
                "Forge 官方源页面缺少构建表", "Forge 官方源", DownloadDiagnostics.Snippet(html));
        }
        var chunks = html.Substring(0, tableEnd).Split(new[] { "<td class=\"download-version" }, StringSplitOptions.None);
        var entries = new List<ForgeBuildEntry>();
        for (var i = 1; i < chunks.Length; i++) {
            var chunk = chunks[i];
            try {
                var name = Regex.Match(chunk, "(?<=[^(0-9)]+)[0-9\\.]+").Value;
                if (string.IsNullOrEmpty(name)) continue;
                var branch = Regex.Match(chunk, $"(?<=-{name}-)[^-\"]+(?=-[a-z]+.[a-z]{{3}})").Value;
                if (string.IsNullOrWhiteSpace(branch)) branch = null;
                //* 没有任何下载的构建（1.6.4 有一部分这种情况）直接跳过，再检查发布时间。
                var hasInstaller = chunk.Contains("classifier-installer\"");
                var hasUniversal = chunk.Contains("classifier-universal\"");
                var hasClient = chunk.Contains("client.zip");
                if (!hasInstaller && !hasUniversal && !hasClient) continue;
                var time = Regex.Match(chunk, "(?<=\"download-time\" title=\")[^\"]+").Value;
                var parts = time.Split(new[] { ' ', '-', ':' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6) {
                    throw new DownloadSourceException(DownloadFailureKind.InvalidPayload,
                        $"Forge 构建 {name} 的发布时间格式异常：{time}", "Forge 官方源", DownloadDiagnostics.Snippet(chunk));
                }
                var release = new DateTime(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]),
                    int.Parse(parts[3]), int.Parse(parts[4]), int.Parse(parts[5]), DateTimeKind.Utc)
                    .ToLocalTime().ToString("yyyy/MM/dd HH:mm");
                //* promo 标记在下载链接所在行，截断前先读取。
                var recommended = chunk.Contains("fa promo-recommended");
                string? hash;
                string category;
                if (hasInstaller) {
                    chunk = chunk.Substring(chunk.IndexOf("installer.jar", StringComparison.Ordinal));
                    hash = Regex.Match(chunk, "(?<=MD5:</strong> )[^<]+").Value;
                    category = "installer";
                } else if (hasUniversal) {
                    chunk = chunk.Substring(chunk.IndexOf("universal.zip", StringComparison.Ordinal));
                    hash = Regex.Match(chunk, "(?<=MD5:</strong> )[^<]+").Value;
                    category = "universal";
                } else {
                    chunk = chunk.Substring(chunk.IndexOf("client.zip", StringComparison.Ordinal));
                    hash = Regex.Match(chunk, "(?<=MD5:</strong> )[^<]+").Value;
                    category = "client";
                }
                entries.Add(new ForgeBuildEntry(name, branch, inherit) {
                    Category = category,
                    IsRecommended = recommended,
                    Hash = hash.Replace("\r", "").Replace("\n", ""),
                    ReleaseTime = release,
                });
            } catch (DownloadSourceException) {
                throw;
            } catch (Exception ex) {
                throw new DownloadSourceException(DownloadFailureKind.InvalidPayload,
                    $"Forge 官方源构建条目解析失败", "Forge 官方源", DownloadDiagnostics.Snippet(chunk), ex);
            }
        }
        if (entries.Count == 0) {
            throw new DownloadSourceException(DownloadFailureKind.NotFound,
                $"Forge 官方源没有 {inherit} 的可用构建", "Forge 官方源", DownloadDiagnostics.Snippet(html));
        }
        return entries;
    }

    /// <summary>解析 BMCLAPI 返回的 Forge 构建数组。</summary>
    /// <param name="json">BMCLAPI 的 JSON 数组。</param>
    /// <param name="inherit">对应的 Minecraft 版本。</param>
    /// <param name="recommendedVersion">推荐版本的版本名；未知时为 null。</param>
    /// <exception cref="DownloadSourceException">JSON 非法、条目缺字段或没有可用构建。</exception>
    public static IReadOnlyList<ForgeBuildEntry> ParseBmclapiJson(string json, string inherit, string? recommendedVersion) {
        JArray array;
        try {
            array = JArray.Parse(json);
        } catch (Newtonsoft.Json.JsonException ex) {
            throw new DownloadSourceException(DownloadFailureKind.InvalidPayload,
                "Forge BMCLAPI 返回的内容不是合法 JSON 数组", "BMCLAPI", DownloadDiagnostics.Snippet(json), ex);
        }
        var entries = new List<ForgeBuildEntry>();
        foreach (var token in array.OfType<JObject>()) {
            string? hash = null;
            var category = "unknown";
            var priority = -1;
            if (token["files"] is JArray files) {
                foreach (var file in files.OfType<JObject>()) {
                    var fileCategory = file["category"]?.ToString();
                    var format = file["format"]?.ToString();
                    if (fileCategory == "installer" && format == "jar") {
                        hash = file["hash"]?.ToString();
                        category = "installer";
                        priority = 2;
                    } else if (fileCategory == "universal" && priority <= 1 && format == "zip") {
                        hash = file["hash"]?.ToString();
                        category = "universal";
                        priority = 1;
                    } else if (fileCategory == "client" && priority <= 0 && format == "zip") {
                        hash = file["hash"]?.ToString();
                        category = "client";
                        priority = 0;
                    }
                }
            }
            var version = token["version"]?.ToString()
                ?? throw new DownloadSourceException(DownloadFailureKind.MissingField,
                    "Forge BMCLAPI 条目缺少 version", "BMCLAPI", DownloadDiagnostics.Snippet(token.ToString()));
            var branch = token["branch"]?.Type == JTokenType.Null ? null : token["branch"]?.ToString();
            try {
                var modified = token["modified"]?.ToObject<DateTime>()
                    ?? throw new DownloadSourceException(DownloadFailureKind.MissingField,
                        $"Forge BMCLAPI 构建 {version} 缺少 modified", "BMCLAPI", DownloadDiagnostics.Snippet(token.ToString()));
                entries.Add(new ForgeBuildEntry(version, branch, inherit) {
                    Hash = hash,
                    Category = category,
                    IsRecommended = recommendedVersion == version,
                    ReleaseTime = modified.ToLocalTime().ToString("yyyy/MM/dd HH:mm"),
                });
            } catch (DownloadSourceException) {
                throw;
            } catch (Exception ex) {
                throw new DownloadSourceException(DownloadFailureKind.InvalidPayload,
                    $"Forge BMCLAPI 构建 {version} 的数据解析失败", "BMCLAPI",
                    DownloadDiagnostics.Snippet(token.ToString()), ex);
            }
        }
        if (entries.Count == 0) {
            throw new DownloadSourceException(DownloadFailureKind.NotFound,
                $"BMCLAPI 没有 {inherit} 的可用构建", "BMCLAPI", DownloadDiagnostics.Snippet(json));
        }
        return entries;
    }
}
