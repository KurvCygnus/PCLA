
namespace PCLCS.Net;

/// <summary>
/// 下载线程（ModNet.vb:237-384 逐字移植：线程链表、Range 分配与按 Uuid 判等）。
/// </summary>
public sealed class NetThread : IEnumerable<NetThread>, IEquatable<NetThread>
{
    /// <summary>对应的下载任务。</summary>
    public NetFile Task = null!; //由 NetFile.TryBeginThread 的对象初始化器必赋

    /// <summary>对应的线程。</summary>
    public Thread? Thread; //字段与类型同名，C# 的 Color-Color 规则允许（对照 VB Public Thread As Thread）

    /// <summary>链表中的下一个线程。</summary>
    public NetThread? NextThread;

    private IEnumerable<NetThread> Next
    {
        get
        {
            for (var currentChain = this; currentChain != null; currentChain = currentChain.NextThread)
                yield return currentChain;
        }
    }

    public IEnumerator<NetThread> GetEnumerator() => Next.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => Next.GetEnumerator();

    /// <summary>分配给任务中每个线程（无论其是否失败）的编号。</summary>
    public int Uuid;

    /// <summary>是否为第一个线程。</summary>
    public bool IsFirstThread => DownloadStart == 0 && Task.FileSize == -2;

    /// <summary>该线程的缓存文件。</summary>
    public string? Temp;

    /// <summary>线程下载起始位置。</summary>
    public long DownloadStart;

    /// <summary>线程下载结束位置。</summary>
    public long DownloadEnd
    {
        get
        {
            //! 逐字移植豁免（Rider 反转 if）：VB :295-300 的 DownloadEnd 结构
            lock (Task.LockChain)
            {
                if (NextThread == null)
                {
                    if (Task.IsUnknownSize) return 1000 * 1000 * 1000 * 1000L; //约 1T
                    return Task.FileSize - 1;
                }
                return NextThread.DownloadStart - 1;
            }
        }
    }

    /// <summary>线程未下载的文件大小。</summary>
    public long DownloadUndone => DownloadEnd - (DownloadStart + DownloadDone) + 1;

    /// <summary>线程已下载的文件大小。</summary>
    public long DownloadDone;

    /// <summary>上次记速时的时间。</summary>
    private long _speedLastTime = NetFile.GetTimeMs();

    /// <summary>上次记速时的已下载大小。</summary>
    private long _speedLastDone;

    /// <summary>当前的下载速度，单位为 Byte / 秒。</summary>
    public long Speed
    {
        get
        {
            //! 逐字移植豁免（Rider 反转 if）：VB :333-341 的记速结构
            if (NetFile.GetTimeMs() - _speedLastTime > 200)
            {
                var deltaTime = NetFile.GetTimeMs() - _speedLastTime;
                _speed = Convert.ToInt64((DownloadDone - _speedLastDone) / (deltaTime / 1000.0));
                _speedLastDone = DownloadDone;
                _speedLastTime += deltaTime;
            }
            return _speed;
        }
    }
    private long _speed;

    /// <summary>线程初始化时的时间。</summary>
    public long InitTime = NetFile.GetTimeMs();

    /// <summary>上次接受到有效数据的时间，-1 表示尚未有有效数据。</summary>
    public long LastReceiveTime = -1;

    /// <summary>当前线程的状态。</summary>
    public NetState State = NetState.WaitingToDownload;

    /// <summary>是否已经结束。</summary>
    public bool IsEnded => State is NetState.Finished or NetState.Canceled;

    /// <summary>当前选取的是哪一个 Url。</summary>
    public NetSource Source = null!; //由 NetFile.TryBeginThread 的对象初始化器必赋

    //允许进行 UUID 比较
    public bool Equals(NetThread? other) => other != null && Uuid == other.Uuid;

    public override bool Equals(object? obj) => Equals(obj as NetThread);

    //! VB 未重写 GetHashCode；C# 重写 Equals 须配套以避免 CS0659
    //! 逐字移植豁免（Rider 非只读字段）：VB 未重写 GetHashCode；C# 重写 Equals 须配套（CS0659），Uuid 实为构造期定值
    public override int GetHashCode() => Uuid;

    public static bool operator ==(NetThread? left, NetThread? right) => left is null ? right is null : left.Equals(right); //VB 原为 EqualityComparer.Default.Equals，null 语义一致

    public static bool operator !=(NetThread? left, NetThread? right) => !(left == right);
}