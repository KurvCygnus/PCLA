using PCLCS.Json;
using PCLCS.Settings;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PCLCS.Instances;

/// <summary>实例种类。数值与 VB McInstanceState（ModMinecraft.vb:222-233）一一对应。</summary>
public enum InstanceState
{
    Error,
    Original,
    Snapshot,
    Fool,
    OptiFine,
    Old,
    Forge,
    NeoForge,
    LiteLoader,
    Fabric
}

/// <summary>
/// 实例核心数据面。自 VB McInstance（ModMinecraft.vb:220-871）移植；
/// UI/ini/Logo 编排与 JsonConfigProvider 留在 VB，由薄 wrapper 调用本类。
/// </summary>
public sealed class InstanceCore
{
    /// <summary>版本文件夹路径，以 \ 结尾。</summary>
    public string PathVersion { get; }
    private readonly string _mcFolderSelected;
    /// <summary>实例设置的存储键，遵循 VB PathVersion 约定（尾部带 \）。</summary>
    private readonly string _instanceSettingsKey;
    /// <summary>是否处于 UI 主线程；null 视为非主线程（JSON 读取失败时允许重试）。</summary>
    private readonly Func<bool>? _runInUi;

    /// <param name="pathVersion">版本文件夹路径，以 \ 结尾。</param>
    /// <param name="mcFolderSelected">当前 Minecraft 根文件夹，以 \ 结尾；PathIndie 的回落值。</param>
    /// <param name="instanceSettingsKey">实例设置键（PathVersion 约定）。</param>
    /// <param name="runInUi">VB RunInUi() 的判断回调；null 视为非 UI 线程。</param>
    public InstanceCore(string pathVersion, string mcFolderSelected, string instanceSettingsKey, Func<bool>? runInUi = null)
    {
        PathVersion = pathVersion;
        _mcFolderSelected = mcFolderSelected;
        _instanceSettingsKey = instanceSettingsKey;
        _runInUi = runInUi;
        // VB New 中还会创建 JsonConfigProvider（:578），该类型留在 VB，由 wrapper 负责
    }

    /// <summary>当前 Minecraft 根文件夹，以 \ 结尾（ModMinecraft.vb 模块属性 McFolderSelected 的 C# 面）。</summary>
    public string McFolderSelected => _mcFolderSelected;

    /// <summary>实例设置的存储键（构造参数 instanceSettingsKey），供实例级设置读取。</summary>
    public string InstanceSettingsKey => _instanceSettingsKey;

    /// <summary>该版本的版本文件夹名称（ModMinecraft.vb:286-292）。</summary>
    public string Name
    {
        get
        {
            if (_name is null && PathVersion.Length > 0) _name = PathUtils.GetLastPart(PathVersion);
            return _name ?? "";
        }
    }
    private string? _name;

    /// <summary>显示的描述文本（ModMinecraft.vb:297）。</summary>
    public string Info { get; set; } = "该版本未被加载，请向作者反馈此问题";

    /// <summary>该版本的列表检查原始结果，不受自定义影响（ModMinecraft.vb:302）。</summary>
    public InstanceState State { get; set; } = InstanceState.Error;

    public bool IsLoaded { get; set; }

    /// <summary>是否为收藏的版本（ini 读取由 VB wrapper 完成后赋值）。</summary>
    public bool IsStar { get; set; }

    /// <summary>强制版本分类，0 为未启用，1 为隐藏，2 及以上为其他普通分类（McInstanceCardType 数值，枚举仍在 VB）。</summary>
    public int DisplayType { get; set; }

    /// <summary>该版本是否可以安装 Mod（ModMinecraft.vb:318-323）。供 PathIndie 判定，不进公共面。</summary>
    private bool Modable
    {
        get
        {
            if (!IsLoaded) Load(null);
            return Version.ModLoaders != ModLoaders.None || DisplayType == 2;   // 2 = McInstanceCardType.API
        }
    }

    /// <summary>版本信息（ModMinecraft.vb:327-346）：从合并后的 JSON 识别，失败回落 Unknown。</summary>
    public McVersion Version
    {
        get
        {
            if (_version is not null) return _version;
            try
            {
                var result = new McVersion(Name, VersionJson, JsonVersion);
                _version = result;
                if (result.VanillaName == "Unknown") Info = "PCL 无法识别该版本的 MC 版本号";
            } catch (Exception ex)
            {
                Logger.Warn(ex, "识别 Minecraft 版本时出错");
                _version = new McVersion("Unknown");
                Info = "无法识别：" + ex.Message;
            }
            return _version;
        }
    }
    private McVersion? _version;

    /// <summary>
    /// 该版本的 Json 对象（ModMinecraft.vb:424-499）：解析 → HMCL patches 合并 → inherit 链合并。
    /// 若 Json 存在问题，在获取该属性时即会抛出异常。
    /// </summary>
    public JsonObject VersionJson
    {
        get
        {
            if (_versionJson is not null) return _versionJson;
            string text = JsonText;   // 触发 JsonText 的 Get 事件（:427，在 Try 外，异常不经包装直接抛出）
            try
            {
                _versionJson = JsonNodeUtils.ParseObject(text);
                ConvertHmclPatches();
                MergeInheritedVersions();
            } catch (Exception ex)
            {
                throw new Exception("初始化版本 JSON 时失败（" + Name + "）", ex);
            }
            return _versionJson;
        }
    }
    private JsonObject? _versionJson;

    /// <summary>Json 是否为 HMCL 格式（ModMinecraft.vb:512）。</summary>
    public bool IsHmclFormatJson { get; private set; }

    /// <summary>是否为旧版 Json 格式（ModMinecraft.vb:504-508）：minecraftArguments 存在且非空。</summary>
    public bool IsOldJson
    {
        get
        {
            var arguments = VersionJson["minecraftArguments"];
            return arguments is not null && arguments.ToString().Length > 0;
        }
    }

    /// <summary>
    /// 该版本的依赖版本。若无依赖版本则为空字符串（ModMinecraft.vb:544-560）。
    /// </summary>
    public string InheritName
    {
        get
        {
            if (_inheritName is not null) return _inheritName;
            _inheritName = VersionJson["inheritsFrom"]?.ToString() ?? "";
            // 过老的 LiteLoader 没有 inheritsFrom（例如 1.5.2），需要手动判断以获取真实继承版本；
            // 如果版本提供了不同的 Jar，代表所需的 Jar 可能已被更改，则跳过替换（:551-552）
            if (JsonText.Contains("liteloader") && Version.VanillaName != Name && !JsonText.Contains("logging"))
            {
                // VB If(JsonObject("jar"), Version.VanillaName)：键存在但为 JSON null 时取空串
                string jarOrVanillaName = VersionJson.ContainsKey("jar") ? VersionJson["jar"]?.ToString() ?? "" : Version.VanillaName;
                if (jarOrVanillaName == Version.VanillaName) _inheritName = Version.VanillaName;
            }
            // HMCL 版本无 Json（:555）
            if (IsHmclFormatJson) _inheritName = "";
            return _inheritName;
        }
    }
    private string? _inheritName;

    /// <summary>
    /// 应用版本隔离后，该版本所对应的 Minecraft 根文件夹，以 \ 结尾（ModMinecraft.vb:242-281）。
    /// 首次判定会写回 VersionArgumentIndieV2。
    /// </summary>
    public string PathIndie
    {
        get
        {
            if (SettingsService.HasSaved("VersionArgumentIndieV2", _instanceSettingsKey))
                return SettingsService.Get<bool>("VersionArgumentIndieV2", _instanceSettingsKey) ? PathVersion : _mcFolderSelected;
            if (!IsLoaded) Load(null);   // VB Load() 无参；此处无 ini 回调，缓存写入由 wrapper 的完整 Load 完成
            SettingsService.Set("VersionArgumentIndieV2", ShouldBeIndie(), _instanceSettingsKey);
            return SettingsService.Get<bool>("VersionArgumentIndieV2", _instanceSettingsKey) ? PathVersion : _mcFolderSelected;
        }
    }

    /// <summary>决定该版本是否应该被隔离（ModMinecraft.vb:247-276）。</summary>
    private bool ShouldBeIndie()
    {
        // 从老的版本独立设置中迁移：-1 未决定，0 使用全局设置，1 手动开启，2 手动关闭（:250-253）
        if (SettingsService.HasSaved("VersionArgumentIndie", _instanceSettingsKey) && SettingsService.Get<int>("VersionArgumentIndie", _instanceSettingsKey) > 0)
        {
            Logger.Info($"版本隔离初始化（{Name}）：从老的版本独立设置中迁移");
            return SettingsService.Get<int>("VersionArgumentIndie", _instanceSettingsKey) == 1;
        }
        // 若版本文件夹下包含 mods 或 saves 文件夹，则自动开启版本隔离（:255-260）
        var modFolder = DirectoryUtils.GetInfo(PathVersion + @"mods\");
        var saveFolder = DirectoryUtils.GetInfo(PathVersion + @"saves\");
        if ((modFolder.Exists && modFolder.EnumerateFiles().Any()) || (saveFolder.Exists && saveFolder.EnumerateDirectories().Any()))
        {
            Logger.Info($"版本隔离初始化（{Name}）：版本文件夹下存在 mods 或 saves 文件夹，自动开启");
            return true;
        }
        // 根据全局的默认设置决定是否隔离（:262-275）
        bool isRelease = State != InstanceState.Fool && State != InstanceState.Old && State != InstanceState.Snapshot;
        Logger.Info($"版本隔离初始化（{Name}）：从全局默认设置中（{SettingsService.Get<int>("LaunchArgumentIndieV2")}）判断，State {State}，IsRelease {isRelease}，Modable {Modable}");
        return SettingsService.Get<int>("LaunchArgumentIndieV2") switch
        {
            0 => false,                          // 关闭
            1 => Modable,                        // 仅隔离可安装 Mod 的版本
            2 => !isRelease,                     // 仅隔离非正式版
            3 => !isRelease || Modable,          // 隔离非正式版与可安装 Mod 的版本
            _ => true                            // 隔离所有版本
        };
    }

    /// <summary>
    /// 检查 Minecraft 版本，若检查通过 State 则为 Original 且返回 True（ModMinecraft.vb:584-630）。
    /// </summary>
    public bool Check()
    {
        // 检查文件夹（:587-591）
        if (!DirectoryUtils.Exists(PathVersion))
        {
            State = InstanceState.Error;
            Info = "未找到版本 " + Name;
            return false;
        }
        // 确认 Json 可用性（:592-602）
        try
        {
            // 与 VB JsonObject("mainClass") Is Nothing 一致：键存在（含 JSON null 值）即视为可用
            if (!VersionJson.ContainsKey("mainClass")) throw new Exception("版本 JSON 中没有 mainClass 项！");   // 这里也会触发 VersionJson 的构建
        } catch (Exception ex)
        {
            Logger.Warn(ex, $"版本 JSON 可用性检查失败（{PathVersion}）");
            _jsonText = "";
            _versionJson = null;
            Info = ex.Message;
            State = InstanceState.Error;
            return false;
        }
        // 检查版本号获取（:603-611）
        try
        {
            if (string.IsNullOrEmpty(Version.VanillaName)) throw new Exception("无法获取版本号，结果为空");
        } catch (Exception ex)
        {
            Logger.Warn(ex, $"版本号获取失败（{Name}）");
            State = InstanceState.Error;
            Info = "版本号获取失败：" + ex.GetDisplay(false);
            return false;
        }
        // 检查依赖版本（:612-626）
        try
        {
            if (InheritName != "")
            {
                if (!FileUtils.Exists($@"{PathUtils.RemoveLastPart(PathVersion)}\{InheritName}\{InheritName}.json"))
                {
                    State = InstanceState.Error;
                    Info = "需要安装 " + InheritName + " 作为前置版本";
                    return false;
                }
            }
        } catch (Exception ex)
        {
            Logger.Warn(ex, $"依赖版本检查出错（{Name}）");
            State = InstanceState.Error;
            Info = "未知错误：" + ex.GetDisplay(false);
            return false;
        }
        State = InstanceState.Original;
        return true;
    }

    /// <summary>
    /// 加载 Minecraft 版本的详细信息。不使用其缓存，且会更新缓存（ModMinecraft.vb:634-727）。
    /// ini 缓存写入经 <paramref name="persistCache"/> 回调交 VB；Logo 选择留在 VB。
    /// </summary>
    public InstanceCore Load(Action<string, string?>? persistCache)
    {
        try
        {
            // 检查版本，若出错则跳过数据确定阶段（:636-637，对应 VB GoTo ExitDataLoad）
            if (!Check())
            {
                // ExitDataLoad
            } else
            {
                // 确定版本分类（:638-667），赋值顺序即 VB 分支顺序
                switch (Version.VanillaName)   // 在获取 Version 对象时会完成它的加载
                {
                case "Unknown":
                    State = InstanceState.Error;
                    break;
                case "Old":
                    State = InstanceState.Old;
                    break;
                default:
                    // 愚人节与快照版本
                    if (Version.IsFool)
                    {
                        State = InstanceState.Fool;
                    } else if (Version.IsSnapshot)
                    {
                        State = InstanceState.Snapshot;
                    }
                    // OptiFine
                    if (Version.HasOptiFine) State = InstanceState.OptiFine;
                    // LiteLoader
                    if (Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader)) State = InstanceState.LiteLoader;
                    // Fabric、Forge、NeoForge
                    if (Version.ModLoaders.HasFlagF(ModLoaders.Fabric))
                    {
                        State = InstanceState.Fabric;
                    } else if (Version.ModLoaders.HasFlagF(ModLoaders.Forge))
                    {
                        State = InstanceState.Forge;
                    } else if (Version.ModLoaders.HasFlagF(ModLoaders.NeoForge))
                    {
                        State = InstanceState.NeoForge;
                    }
                    break;
                }
            }
            // 确定版本图标（:670-695）—— Logo 选择依赖 VB 资源路径，留在 VB wrapper
            // 确定版本描述（:696-697）；VB 经 Settings.Get(Instance:=Me) 读取，C# 后端经实例设置键读取
            RefreshInfo(() => SettingsService.Get<int>("VersionServerLogin", _instanceSettingsKey));
            // 确定版本收藏状态与显示种类（:698-701）—— ini 读取由 VB wrapper 完成后赋值 IsStar/DisplayType
            // 写入缓存（:702-717）
            if (DirectoryUtils.Exists(PathVersion))
            {
                persistCache?.Invoke("State", State.ToString());
                persistCache?.Invoke("Info", Info);
                // Logo 缓存项由 VB wrapper 写入
            }
            if (State != InstanceState.Error && Version.Reliable)
            {
                persistCache?.Invoke("ReleaseTime", Version.ReleaseTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
                persistCache?.Invoke("VersionFabric", Version.ModLoaders.HasFlagF(ModLoaders.Fabric) ? Version.ModLoaderVersions[ModLoaders.Fabric] : null);
                persistCache?.Invoke("VersionOptiFine", Version.OptiFineAsMod ? null : Version.OptiFine);
                persistCache?.Invoke("VersionLiteLoader", Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader).ToString());
                persistCache?.Invoke("VersionForge", Version.ModLoaders.HasFlagF(ModLoaders.Forge) ? Version.ModLoaderVersions[ModLoaders.Forge] : null);
                persistCache?.Invoke("VersionNeoForge", Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) ? Version.ModLoaderVersions[ModLoaders.NeoForge] : null);
                persistCache?.Invoke("VersionVanillaName", Version.VanillaName);
                persistCache?.Invoke("VersionVanilla", Version.Vanilla.ToString());
            }
        } catch (Exception ex)
        {
            Info = ex.GetDisplay(false);
            State = InstanceState.Error;
            Logger.Warn(ex, $"加载版本失败（{Name}）");
        } finally
        {
            IsLoaded = true;
        }
        return this;
    }

    /// <summary>
    /// 根据当前组件信息刷新版本描述（ModMinecraft.vb:732-745）。
    /// CustomInfo ini 读取留在 VB wrapper；<paramref name="versionServerLogin"/> 供给 VB VersionServerLogin 设置值。
    /// </summary>
    public void RefreshInfo(Func<int> versionServerLogin)
    {
        Info = VersionDisplayName();
        if (State == InstanceState.Fool)
        {
            Info = FoolNames.Get(Version.VanillaName);
        } else if (State != InstanceState.Error)
        {
            if (versionServerLogin() == 3) Info += ", 统一通行证验证";
            if (versionServerLogin() == 4) Info += ", Authlib 验证";
        }
    }

    /// <summary>
    /// 获取对版本信息的简短描述（ModMinecraft.vb:750-785）。
    /// 例如 “快照版 16w01a”、“正式版 1.12.2, Forge 1.2.3”、“愚人节版本 2.0”。
    /// </summary>
    public string VersionDisplayName()
    {
        // Mod Loader 信息（:751-757）
        string modLoaderInfo = "";
        if (Version.ModLoaders.HasFlagF(ModLoaders.Forge))
            modLoaderInfo += ", Forge" + (Version.ModLoaderVersions[ModLoaders.Forge] == "未知版本" ? "" : " " + Version.ModLoaderVersions[ModLoaders.Forge]);
        if (Version.ModLoaders.HasFlagF(ModLoaders.NeoForge))
            modLoaderInfo += ", NeoForge" + (Version.ModLoaderVersions[ModLoaders.NeoForge] == "未知版本" ? "" : " " + Version.ModLoaderVersions[ModLoaders.NeoForge]);
        if (Version.ModLoaders.HasFlagF(ModLoaders.Fabric))
            modLoaderInfo += ", Fabric" + (Version.ModLoaderVersions[ModLoaders.Fabric] == "未知版本" ? "" : " " + Version.ModLoaderVersions[ModLoaders.Fabric]);
        if (Version.HasOptiFine)
            modLoaderInfo += ", OptiFine" + (Version.OptiFine == "未知版本" ? "" : " " + Version.OptiFine!.Replace("-", " ").Replace("_", " "));
        if (Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader)) modLoaderInfo += ", LiteLoader";
        // 基础信息（:759-783）
        string info;
        switch (State)
        {
        case InstanceState.Snapshot or InstanceState.Original or InstanceState.Forge or InstanceState.NeoForge
                or InstanceState.Fabric or InstanceState.OptiFine or InstanceState.LiteLoader:
            if (Version.VanillaName.ContainsIgnoreCase("pre"))
            {
                info = "预发布版 " + Version.VanillaName;
            } else if (Version.VanillaName.ContainsIgnoreCase("rc"))
            {
                info = "发布候选 " + Version.VanillaName;
            } else if (Version.VanillaName.ContainsIgnoreCase("experimental"))
            {
                info = "实验性快照" + Version.VanillaName;
            } else if (Version.VanillaName == "pending")
            {
                info = "实验性快照";
            } else if (Version.IsSnapshot)
            {
                info = Version.Reliable ? "快照版 " + Version.VanillaName.Replace("-snapshot", "") : "快照版";
            } else
            {
                info = Version.Reliable ? "正式版 " + Version.VanillaName : "正式版";
            }
            break;
        case InstanceState.Old:
            info = "远古版本";
            break;
        case InstanceState.Fool:
            info = "愚人节版本 " + Version.VanillaName;
            break;
        case InstanceState.Error:
            return Info;   // 已有错误信息
        default:
            return "发生了未知错误，请向作者反馈此问题";
        }
        return (info + modLoaderInfo).Replace("_", "-");
    }

    /// <summary>
    /// 重命名 MC 版本的文件面（ModMinecraft.vb:818-855）：
    /// 主文件夹、natives 文件夹、jar 与版本 JSON；Setup.ini 路径替换与 PCL.ini 选中名修正由 VB wrapper 前后处理。
    /// </summary>
    public void Rename(string newName)
    {
        string oldName = Name;
        string oldPath = PathVersion;
        string newPath = PathUtils.AddSlashSuffix(Path.Combine(PathUtils.RemoveLastPart(oldPath), newName));
        // 重新读取版本 JSON 信息，避免 JsonObject 中已被合并的项被重新存储（:822-830）
        JsonObject? jsonObject;
        string oldJsonPath = InstancePaths.GetJsonPath(oldPath, oldName);
        try
        {
            jsonObject = FileUtils.ReadAsJson(oldJsonPath)?.AsObject();
        } catch (Exception ex)
        {
            Logger.Warn(ex, "在重命名读取 JSON 时失败");
            jsonObject = VersionJson;
        }
        // 清理缓存（:831-833，Config.Save 与 ResetSettingsCache）由 VB wrapper 前后处理
        // 重命名主文件夹（:834-835）
        DirectoryUtils.Move(oldPath, newPath);
        // 重命名 jar 文件与 natives 文件夹（:836-839）
        // 不能进行遍历重命名，否则在版本名很短的时候容易误伤其他文件（#6443）
        if (DirectoryUtils.Exists($@"{newPath}{oldName}-natives")) DirectoryUtils.Move($@"{newPath}{oldName}-natives", $@"{newPath}{newName}-natives");
        if (FileUtils.Exists($@"{newPath}{oldName}.jar")) FileUtils.Move($@"{newPath}{oldName}.jar", $@"{newPath}{newName}.jar");
        // 替换版本设置文件中的路径与更改已选中的版本（:840-845）由 VB wrapper 前后处理
        // 更新版本 Json（:846-853）
        try
        {
            jsonObject!["id"] = newName;   // 读取结果可能为 null（空 JSON 文件），与 VB 一样在此抛 NRE 并告警
            FileUtils.Delete(newPath + PathUtils.GetLastPart(oldJsonPath));
            FileUtils.Write(newPath + newName + ".json", JsonSerializer.Serialize(jsonObject, RenameJsonOptions));
        } catch (Exception ex)
        {
            Logger.Warn(ex, "重命名版本 json 失败");
        }
    }

    /// <summary>原相等与不等运算符（ModMinecraft.vb:858-869）：按 PathVersion 比较（PathUtils.IsSame）。</summary>
    public static bool SamePath(string? a, string? b) => PathUtils.IsSame(a, b);

    #region 内部实现

    /// <summary>该版本的 Json 文本。会抛出异常（ModMinecraft.vb:389-418）。</summary>
    private string JsonText
    {
        get
        {
            if (_jsonText is not null) return _jsonText;
            string jsonPath = InstancePaths.GetJsonPath(PathVersion, Name);
            _jsonText = FileUtils.TryReadAsString(jsonPath) ?? "";
            // 如果读取失败会返回空字符串；这可能是由于文件被临时占用，故延时后重试（:400）
            if (FastJsonCheck(_jsonText)) return _jsonText;
            if (_runInUi?.Invoke() ?? false)
            {
                Logger.Warn("版本 JSON 文件为空或有误，由于代码在主线程运行，将不再进行重试");
                JsonNodeUtils.Parse(_jsonText);   // 触发异常
            } else
            {
                Logger.Warn($"版本 JSON 文件为空或有误，将在 2s 后重试读取（{jsonPath}）");
                Thread.Sleep(2000);
                _jsonText = FileUtils.ReadAsString(jsonPath);
                if (!FastJsonCheck(_jsonText)) JsonNodeUtils.Parse(_jsonText);   // 触发异常
            }
            return _jsonText;
        }
    }
    private string? _jsonText;

    /// <summary>该版本 jar 中的 version.json 文件对象；若没有则返回 null（ModMinecraft.vb:518-539）。</summary>
    private JsonObject? JsonVersion
    {
        get
        {
            if (_jsonVersionInited) return _jsonVersion;
            _jsonVersionInited = true;
            try
            {
                if (!FileUtils.Exists($@"{PathVersion}{Name}.jar")) return null;
                using var jarArchive = FileUtils.OpenZip(PathVersion + Name + ".jar");
                var versionJson = jarArchive.GetEntry("version.json");
                if (versionJson is null) return null;
                using var reader = new StreamReader(versionJson.Open());
                _jsonVersion = JsonNodeUtils.ParseObject(reader.ReadToEnd());
            } catch (Exception ex)
            {
                Logger.Warn(ex, $"从版本 jar 中读取 version.json 失败（{PathVersion}{Name}.jar）");
            }
            return _jsonVersion;
        }
    }
    private JsonObject? _jsonVersion;
    private bool _jsonVersionInited;

    /// <summary>
    /// 转换 HMCL 关键项（ModMinecraft.vb:431-460）：patches 分支项按 priority 升序合并，
    /// id 为空的项跳过，合并后 id=Name 并删除 inheritsFrom。
    /// </summary>
    private void ConvertHmclPatches()
    {
        var json = _versionJson!;
        if (!json.ContainsKey("patches") || json.ContainsKey("time")) return;
        IsHmclFormatJson = true;
        JsonObject? currentObject = null;
        var subjsonList = new List<JsonObject>();
        foreach (var subjsonNode in json["patches"]!.AsArray())   // 非对象项与 VB 的 DirectCast 一样抛错
        {
            if (subjsonNode is not JsonObject subjson) throw new InvalidCastException("HMCL 分支项不是 JSON 对象");
            subjsonList.Add(subjson);
        }
        // priority 升序；VB SortByComparison（ModBase.vb:959）为等值保序的选择排序，与 LINQ OrderBy 一致
        var sortedSubjsonList = subjsonList.OrderBy(static subjson => Val(subjson["priority"])).ToList();
        foreach (var subjson in sortedSubjsonList)
        {
            var id = JTokenToString(subjson["id"]);
            if (id is not null)
            {
                // 合并 Json
                Logger.Info($"合并 HMCL 分支项：{id}");
                if (currentObject is not null)
                {
                    MergeJson(currentObject, subjson);
                } else
                {
                    currentObject = subjson;
                }
            } else
            {
                Logger.Info("存在为空的 HMCL 分支项");
            }
        }
        _versionJson = currentObject!;   // 全部分支项均无 id 时与 VB 一样抛出 NRE，由外层包装
        _versionJson["id"] = Name;
        if (_versionJson.ContainsKey("inheritsFrom")) _versionJson.Remove("inheritsFrom");
    }

    /// <summary>
    /// 与继承版本合并（ModMinecraft.vb:461-489）：自引用拒绝、父版本自引用按嵌套抛错，
    /// libraries 子版本在前父版本在后，其余项子版本优先。合并失败仅告警并保留部分结果。
    /// </summary>
    private void MergeInheritedVersions()
    {
        string? instanceNameInheritFrom = null;
        try
        {
            instanceNameInheritFrom = _versionJson!["inheritsFrom"]?.ToString() ?? "";
            if (instanceNameInheritFrom == Name)
            {
                Logger.Warn($"自引用的继承版本：{Name}");
                instanceNameInheritFrom = "";
            } else
            {
                while (instanceNameInheritFrom != "")
                {
                    string inheritPath = InstancePaths.ResolveVersionFolder(instanceNameInheritFrom, _mcFolderSelected);
                    var inheritInstance = new InstanceCore(inheritPath, _mcFolderSelected, inheritPath, _runInUi);
                    // 继续循环
                    if (inheritInstance.InheritName == instanceNameInheritFrom) throw new Exception("版本依赖项出现嵌套：" + instanceNameInheritFrom);
                    instanceNameInheritFrom = inheritInstance.InheritName;
                    // 合并 Libraries 项：子版本放在前面，父版本放在后面（5978#：如果多个 jar 包中含有相同的类，Java 8 和之前的版本按照 -cp 指定的顺序选择第一个）
                    var currentLib = _versionJson!["libraries"]!.DeepClone().AsArray();
                    foreach (var libToken in inheritInstance.VersionJson["libraries"]!.AsArray())
                    {
                        currentLib.Add(libToken?.DeepClone());
                    }
                    // 合并其他项：子版本优先于父版本
                    MergeJson(inheritInstance.VersionJson, _versionJson);
                    _versionJson = inheritInstance.VersionJson;
                    _versionJson["libraries"] = currentLib;
                }
            }
        } catch (Exception ex)
        {
            Logger.Warn(ex, $"合并版本依赖项 JSON 失败（{instanceNameInheritFrom ?? "null"}）");
        }
    }

    /// <summary>
    /// Newtonsoft JObject.Merge（默认 MergeArrayHandling.Concat / MergeNullValueHandling.Ignore）的 STJ 等价实现：
    /// 对象递归合并、数组拼接（已有项在前）、标量与 null 覆盖规则按默认设置。
    /// </summary>
    private static void MergeJson(JsonObject target, JsonObject incoming)
    {
        foreach (var property in incoming.ToArray())
        {
            var node = property.Value;
            if (node is null || node.GetValueKind() == JsonValueKind.Null) continue;   // 默认忽略来源 null 值
            if (target[property.Key] is JsonObject targetObject && node is JsonObject incomingObject)
            {
                MergeJson(targetObject, incomingObject);
            } else if (target[property.Key] is JsonArray targetArray && node is JsonArray incomingArray)
            {
                foreach (var item in incomingArray) targetArray.Add(item?.DeepClone());
            } else
            {
                target[property.Key] = node.DeepClone();
            }
        }
    }

    /// <summary>快速检查 JSON 是否以 { 开头、} 结尾；忽略空白字符（ModMinecraft.vb:392-396）。</summary>
    private static bool FastJsonCheck(string json)
    {
        string trimedJson = json.Trim();
        return trimedJson.StartsWithF("{") && trimedJson.EndsWithF("}");
    }

    /// <summary>VB JToken→String 转换语义：缺失/JSON null → null；对象/数组 → 抛转换异常；其余取文本。</summary>
    private static string? JTokenToString(JsonNode? node)
    {
        if (node is null || node.GetValueKind() == JsonValueKind.Null) return null;
        return node switch
        {
            JsonObject or JsonArray => throw new ArgumentException("无法将 JSON 对象或数组转换为字符串"),
            _ => node.ToString()
        };
    }

    private static readonly Regex ValPrefixRegex = new(@"^\s*[+-]?(\d+(\.\d*)?|\.\d+)([eE][+-]?\d+)?", RegexOptions.Compiled);

    /// <summary>VB Val 的移植（ModMinecraft.vb:441）：解析字符串前缀的数值部分，无效时为 0。</summary>
    private static double Val(JsonNode? node)
    {
        if (node is null) return 0;
        try
        {
            var match = ValPrefixRegex.Match(node.ToString());
            return match.Success ? double.Parse(match.Value, CultureInfo.InvariantCulture) : 0;
        } catch (Exception)
        {
            return 0;   // 溢出等解析失败与 VB Val 回落 0 一致
        }
    }

    /// <summary>Rename 写出的 JSON 与 Newtonsoft ToString 的缩进习惯对齐。</summary>
    private static readonly JsonSerializerOptions RenameJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    #endregion
}