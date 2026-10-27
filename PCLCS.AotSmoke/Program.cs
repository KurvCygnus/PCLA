using MeloongCore;
using PCLCS.Json;
using PCLCS.Minecraft.Download;
using System.Text;

//? NativeAOT 冒烟入口：只触碰 AOT 安全的公共 API，绝不调用 Config<T>（反射反序列化，AOT 豁免项）。

// 1. STJ 节点解析
var json = JsonNodeUtils.ParseObject("""{"versions":[]}""");
Console.WriteLine(json["versions"] is not null);

// 2. 枚举转换（EnumUtils 走 Enum.TryParse，AOT 安全）
Console.WriteLine(EnumUtils.FromString<DownloadFailureKind>("TooFewEntries"));

// 3. 版本 id 规范化
Console.WriteLine(ClientListParser.NormalizeId("1.20.1"));

// 4. 文件读取链（ReadAsString -> ReadAsBytes -> ReadAsStream）。
//! publish 输出目录下没有 csproj 源文件，改为读取发布产物中的自文件，验证非空读取即可。
//! 显式传 UTF8：无 BOM 的 GB18030 回退依赖 CodePages 编码表，InvariantGlobalization 的 AOT 镜像里已被裁剪。
var self = Path.Combine(AppContext.BaseDirectory, "PCLCS.AotSmoke.exe");
Console.WriteLine(FileUtils.ReadAsString(self, Encoding.UTF8).Length > 0);
