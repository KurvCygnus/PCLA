using PCLCS.Settings;

namespace PCLCS.Net;

/// <summary>
/// 下载引擎的全局调度状态（原 ModNet.vb:311-351 随引擎迁入；曾暂置于 NetFile，后归位于此独立承载，
/// 与管理器/引擎分离）。VB 侧经 ModNet 的同名转发成员访问。
/// </summary>
public static class NetSpeed
{
    /// <summary>正在运行中的线程数（ModNet.vb:349-351；引擎经 Interlocked 增减，管理器守护线程与 VB 转发读取）。</summary>
    public static int ThreadCount;

    /// <summary>最大线程数（ModNet.vb:311-317）。</summary>
    public static int ThreadLimit => SettingsService.Get<int>("ToolDownloadThread") + 1;

    /// <summary>速度下限（ModNet.vb:319-323；管理器 RefreshStat 上调，设置页可写）。</summary>
    public static long SpeedLimitLow = 256 * 1024L; //256K/s

    private static long? speedLimitHighCache;

    /// <summary>速度上限。若无限制则为 -1（ModNet.vb:323-343 三段换算）。</summary>
    public static long SpeedLimitHigh
    {
        get
        {
            //! 逐字移植豁免（Rider 反转 if）：VB :329-343 的 If 缓存判空结构
            if (speedLimitHighCache == null)
            {
                var setting = SettingsService.Get<int>("ToolDownloadSpeed");
                if (setting <= 14)
                {
                    speedLimitHighCache = Convert.ToInt64((setting + 1) * 0.1 * 1024 * 1024L);
                } else if (setting <= 31)
                {
                    speedLimitHighCache = Convert.ToInt64((setting - 11) * 0.5 * 1024 * 1024L);
                } else if (setting <= 41)
                {
                    speedLimitHighCache = Convert.ToInt64((setting - 21) * 1024 * 1024L);
                } else
                {
                    speedLimitHighCache = -1;
                }
            }
            return speedLimitHighCache.Value;
        }
    }

    /// <summary>清空速度上限缓存（ModNet.vb:345-347；Value 参数与 VB 一致被忽略）。</summary>
    public static void UpdateNetTaskSpeedLimitHigh(int value) => speedLimitHighCache = null;

    /// <summary>基于限速，当前可以下载的剩余量（ModNet.vb:345 前后；VB 为公共字段，引擎以 Interlocked 记账，保留公共字段语义）。</summary>
    public static long SpeedLimitLeft = -1;

    /// <summary>增加限速余量（ModNet.vb:1758 StatRefresher 的 Interlocked.Add(NetTaskSpeedLimitLeft, CLng(NetTaskSpeedLimitHigh / 10))；管理器 StatRefresher 直呼本方法）。</summary>
    public static void RefillSpeedLimit()
    {
        //! 逐字移植豁免（Rider 反转 if）：VB :1758 的 If 结构镜像
        if (SpeedLimitHigh > 0) Interlocked.Add(ref SpeedLimitLeft, Convert.ToInt64(SpeedLimitHigh / 10.0));
    }
}