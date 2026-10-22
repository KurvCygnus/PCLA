namespace PCLCS.Minecraft.Download;

/// <summary>源顺序与超时计划。第一项是首源自身超时，第二项是次源的绝对截止时间。</summary>
/// <param name="OfficialFirst">为真时先尝试官方源，为假时先尝试镜像源。</param>
/// <param name="FirstSeconds">首源的超时秒数。</param>
/// <param name="SecondSeconds">次源的绝对截止秒数，自请求开始计时。</param>
public readonly record struct SourcePlan(bool OfficialFirst, int FirstSeconds, int SecondSeconds);

/// <summary>下载源的顺序与超时策略，把设置项映射为 <see cref="SourcePlan"/> 与版本列表偏好。</summary>
public static class SourcePolicy {
    /// <summary>按 ToolDownloadVersion 取源顺序与超时，数值来自 legacy 的 DlSource 超时表。</summary>
    /// <param name="toolDownloadVersion">设置项 ToolDownloadVersion：0 为镜像优先 30/90，1 为官方优先 5/35，2 及其他为官方优先 60/120。</param>
    public static SourcePlan GetPlan(int toolDownloadVersion) {
        return toolDownloadVersion switch {
            0 => new SourcePlan(false, 30, 90),
            1 => new SourcePlan(true, 5, 35),
            _ => new SourcePlan(true, 60, 120),
        };
    }

    /// <summary>版本列表是否优先官方源：版本 2 恒为真，版本 1 取决于 DlPreferMojang，其余为假。</summary>
    /// <param name="toolDownloadVersion">设置项 ToolDownloadVersion。</param>
    /// <param name="downloadPreferMojang">设置项 DlPreferMojang，仅在版本 1 下参与判定。</param>
    public static bool PreferMojangForVersionList(int toolDownloadVersion, bool downloadPreferMojang) {
        return toolDownloadVersion == 2 || (toolDownloadVersion == 1 && downloadPreferMojang);
    }
}
