namespace PCLCS.Minecraft.Download;

/// <summary>源顺序与超时计划。第一项是首源自身超时，第二项是次源的绝对截止时间。</summary>
public readonly record struct SourcePlan(bool OfficialFirst, int FirstSeconds, int SecondSeconds);

public static class SourcePolicy {
    public static SourcePlan GetPlan(int toolDownloadVersion) {
        return toolDownloadVersion switch {
            0 => new SourcePlan(false, 30, 90),
            1 => new SourcePlan(true, 5, 35),
            _ => new SourcePlan(true, 60, 120),
        };
    }

    public static bool PreferMojangForVersionList(int toolDownloadVersion, bool downloadPreferMojang) {
        return toolDownloadVersion == 2 || (toolDownloadVersion == 1 && downloadPreferMojang);
    }
}
