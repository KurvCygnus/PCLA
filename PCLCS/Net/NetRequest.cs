using PCLCS.Settings;
#if NET48
using PCLCS.Startup;
#endif
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using ThrottleDebounce;

namespace PCLCS.Net;

/// <summary>
/// 网络请求核心（自宿主层 Modules\Base\ModNet.vb 迁入的请求面）。
/// 签名提供者（CdnSigner / HeadersSigner）由 VB 启动时接线到 ModSecret.SecretCdnSign / SecretHeadersSign，签名逻辑本身留在 VB。
/// </summary>
public static class NetRequest
{
    /// <summary>URL 签名提供者；VB 启动时指向 ModSecret.SecretCdnSign（ModNet.vb:233 接线点）。null 时原样返回 URL。</summary>
    public static Func<string, string>? CdnSigner { get; set; }

    /// <summary>请求头签名提供者；VB 启动时指向 ModSecret.SecretHeadersSign 的适配器（ModNet.vb:251 接线点）。
    /// 逐字锁定签名应为 Action&lt;HttpRequestHeaders, string&gt;，但 SecretHeadersSign 的模拟浏览器 UA/Referer 分支依赖第三参，
    /// 经确认以 +bool 参数承载 SimulateBrowserHeaders，否则 VB 既有 6 处调用方的行为会静默回退。</summary>
    public static Action<HttpRequestHeaders, string, bool>? HeadersSigner { get; set; }

    //RequestClient / RequestClientLock（ModNet.vb:297-298）：全局唯一的请求客户端，延迟初始化
    private static HttpClient? requestClient;
    private static readonly object RequestClientLock = new();

#if NET48
    /// <summary>缓存文件夹（ModBase.vb:46 PathTemp 的等价实现：设置优先，否则 %TEMP%\&lt;应用文件夹&gt;\；仅 net48 的 CacheCow 使用）。</summary>
    private static readonly Lazy<string> PathTemp = new(static () =>
    {
        var cacheSetting = SettingsService.Get<string>("SystemSystemCache");
        var baseFolder = string.IsNullOrEmpty(cacheSetting)
            ? Path.GetTempPath() + LauncherIdentity.FolderName + "\\"
            : cacheSetting; //VB :46 的 `= ""` 比较对 null 同样成立，故用 IsNullOrEmpty 对齐
        return PathUtils.AddSlashSuffix(baseFolder.Replace("/", "\\"));
    });
#endif

    /// <summary>获取一个单调递增时间值（毫秒）（ModBase.vb:834 的等价实现）。</summary>
    private static long GetTimeMs() => Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000L);

    /// <summary>字节级请求核心（ModNet.vb:216-296 逐行移植：CacheCow net48 条件、重试、超时、UI 线程守卫经 RunInUi 回调）。</summary>
    public static NetResult SendRequest(string url, NetRequestOptions options)
    {
        var method = options.Method ?? HttpMethod.Get; //VB 调用方在转发前已归一化，这里对 C# 直调入口做同样处理
        //在 UI 线程执行了网络请求（ModNet.vb:219）；RunInUi 回调为 null 视为非 UI
        if (options.RunInUi?.Invoke() == true && !url.Contains("//127.")) throw new Exception("在 UI 线程执行了网络请求");
        HttpRequestMessage? request = null;
        CancellationTokenSource? cancelToken = null;
        HttpResponseMessage? response = null;
        var result = new NetResult();
        try
        {
            //构建 RequestClient（ModNet.vb:222-231）
#if NET48
            Directory.CreateDirectory(PathTemp + "Cache\\Http\\");
            lock (RequestClientLock)
            {
                if (requestClient == null) //延迟初始化，以避免在程序启动前加载 CacheCow 导致 DLL 加载失败
                {
                    requestClient = CacheCow.Client.ClientExtensions.CreateClient(new CacheCow.Client.FileCacheStore.FileStore(PathTemp + "Cache/Http/"), new HttpClientHandler
                    {
                        AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
                        UseCookies = false //不设为 False 就不能从 Header 手动传入 Cookies
                    });
                }
            }
#else
            //net8 无 CacheCow（net48-only），跳过缓存协商，仅保留相同的处理器配置
            lock (RequestClientLock)
            {
                requestClient ??= new HttpClient(new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
                    UseCookies = false //不设为 False 就不能从 Header 手动传入 Cookies
                });
            }
#endif
            //构建请求（ModNet.vb:233-251）
            url = CdnSigner?.Invoke(url) ?? url; //ModNet.vb:233 SecretCdnSign(Url)
            request = new HttpRequestMessage(method, url);
            if (options.Content != null && request.Method != HttpMethod.Get && request.Method != HttpMethod.Head) //写入 Content
            {
                if (options.Content is HttpContent content)
                {
                    request.Content = content.Clone();
                } else if (options.Content is byte[] bytes)
                {
                    request.Content = new ByteArrayContent(bytes);
                } else
                {
                    request.Content = new ByteArrayContent((options.Encoding ?? Encoding.UTF8).GetBytes(options.Content.ToString()!)); //! ReSharper 视此 ! 为冗余，但 Roslyn (net8) 要求它；与 VB :241 的 GetBytes(Content.ToString()) 语义一致
                }
            }
            //ModNet.vb:17-19 由 NetRequestByClient 把 ContentType / Accept 合并进请求头的语义，收敛至此处统一处理
            var headers = new Dictionary<string, string>(options.Headers ?? []);
            if (options.ContentType != null) headers["Content-Type"] = options.ContentType;
            if (options.Accept != null) headers["Accept"] = options.Accept;
            foreach (var header in headers) //写入 Headers（ModNet.vb:244-250）
            {
                if (header.Key.Lower() == "content-type")
                {
                    request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                } else
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            HeadersSigner?.Invoke(request.Headers, url, options.SimulateBrowserHeaders); //ModNet.vb:251 SecretHeadersSign
            //发送请求
            //CacheCow 读取缓存时若文件被占用，会调用不存在的 System.Net.Http.Formatting.resources，抛出异常（#8150）
            Retrier.Attempt(action: _ =>
            {
                cancelToken?.Dispose();
                cancelToken = new CancellationTokenSource(options.TimeoutMs);
                var clonedRequest = request.Clone(); //HttpRequestMessage 只能发送一次，重试时需要克隆一个新的
                try
                {
                    response = requestClient!.SendAsync(clonedRequest, HttpCompletionOption.ResponseHeadersRead, cancelToken.Token)
                        .GetResultWithTimeout(cancelToken, options.TimeoutMs);
                } catch (Exception ex) when (ex is FileNotFoundException or IOException)
                {
                    result.Errors.Add(ex); //Retrier 将吞掉并重试的异常，记入载体备查
                    throw;
                } finally
                {
                    clonedRequest.Dispose();
                }
            }, delay: static _ => TimeSpan.FromMilliseconds(200), isRetryAllowed: static ex => ex is FileNotFoundException or IOException);
            //读取响应（ModNet.vb:265-272）
            byte[] responseBytes;
            cancelToken!.Dispose(); //! Retrier.Attempt 已实际执行过 action，此处必非 null；VB :266 亦为无守卫直接解引用
            cancelToken = new CancellationTokenSource(options.TimeoutMs);
            var responseContent = response!.Content!;
            using (var responseStream = responseContent.ReadAsStreamAsync().GetResultWithTimeout(cancelToken, options.TimeoutMs))
            {
                using var stream = new MemoryStream();
                responseStream.CopyToAsync(stream, 81920, cancelToken.Token).GetResultWithTimeout(cancelToken, options.TimeoutMs);
                responseBytes = stream.ToArray();
            }
            if (!response.IsSuccessStatusCode)
            {
                var responseMessage = responseBytes.GetString(options.Encoding);
                throw new HttpRequestCodeException(
                    $"错误码 {response.StatusCode} ({(int) response.StatusCode})，{method}，{url}" +
                    (string.IsNullOrEmpty(responseMessage) ? "" : "\r\n" + responseMessage), response.StatusCode, responseMessage);
            }
            result.StatusCode = (int) response.StatusCode;
            result.Bytes = responseBytes;
            result.Url = url;
            return result;
        } catch (HttpRequestException)
        {
            throw; //HttpRequestCodeException 继承 HttpRequestException，原样抛出不包裹（ModNet.vb:280-281）
        } catch (TimeoutException)
        {
            throw new WebException($"连接服务器超时，请稍后再试，或使用 VPN 改善网络环境（{method}, {url}）", WebExceptionStatus.Timeout);
        } catch (Exception ex)
        {
            if (ex is not TaskCanceledException) ex.ThrowIfCanceled(); //GetResultWithTimeout 会抛出 TaskCanceledException（ModNet.vb:285）
            if (ex.IsBadNetwork())
            {
                throw new WebException($"网络请求失败，请稍后再试，或使用 VPN 改善网络环境（{method}, {url}）", WebExceptionStatus.Timeout);
            }
            throw new Exception($"网络请求出现意外异常（{method}, {url}）", ex);
        } finally
        {
            request?.Dispose();
            cancelToken?.Dispose();
            response?.Dispose();
        }
    }

    /// <summary>发送一次网络请求并获取返回内容（ModNet.vb:10-38）。</summary>
    public static string NetRequestByClient(string url, string? accept = null, bool requireJson = false, Func<bool>? runInUi = null)
        => NetRequestByClient(url, new NetRequestOptions { Accept = accept, RequireJson = requireJson, RunInUi = runInUi });

    /// <summary>发送一次网络请求并获取返回内容（完整选项入口，供 VB 侧转发）。</summary>
    public static string NetRequestByClient(string url, NetRequestOptions options)
    {
        var method = options.Method ?? HttpMethod.Get;
        if (options.MakeLog) Logger.Info($"发起网络请求（{method.Method}，{url}），最大超时 {options.TimeoutMs}");
        try
        {
            var result = SendRequest(url, options).Bytes.GetString(options.Encoding);
            //检查结果是否为完整 JSON（在 #6683 中可能返回空，在被 GFW 截断时可能不完整）
            //在此处检查并报错，可以让 NetRequestByClientRetry 等方法进行重试
            if (!options.RequireJson) return result;
            result = result.Trim('\r', '\n', ' '); //Authlib-Injector 的结果会在末尾附带一个空行
            if (!(result.StartsWithF("{") && result.EndsWithF("}")) && !(result.StartsWithF("[") && result.EndsWithF("]")))
            {
                throw new FormatException("返回结果并非 JSON 格式：" +
                    (result.Length > 2000 ? result[..1000] + "..." + result[^1000..] : result));
            }
            return result;
        } catch (FormatException)
        {
            throw;
        } catch (Exception ex)
        {
            if (options.MakeLog) Logger.Warn(ex, $"网络请求失败（{method.Method}，{url}）");
            throw;
        }
    }

    /// <summary>
    /// 发送网络请求并获取返回内容（ModNet.vb:44-88）。
    /// 会进行至多 45 秒 3 次的尝试，允许最长 30s 的超时；429 睡 10s，403/404 直接抛出。
    /// </summary>
    public static string NetRequestByClientRetry(string url, string? accept = null, bool requireJson = false, Func<bool>? runInUi = null)
        => NetRequestByClientRetry(url, new NetRequestOptions { Accept = accept, RequireJson = requireJson, RunInUi = runInUi });

    /// <summary>发送网络请求并获取返回内容（完整选项入口，供 VB 侧转发）。</summary>
    public static string NetRequestByClientRetry(string url, NetRequestOptions options)
    {
        var retryCount = 0;
        Exception? retryException = null;
        var startTime = GetTimeMs();
        while (true)
        {
            var attemptOptions = options.Clone(); //每次尝试改写超时，避免污染调用方的选项对象
            try
            {
                switch (retryCount)
                {
                case 0: //正常尝试
                    attemptOptions.TimeoutMs = 10000;
                    return NetRequestByClient(url, attemptOptions);
                case 1: //慢速重试
                    Thread.Sleep(500);
                    attemptOptions.TimeoutMs = 30000;
                    return NetRequestByClient(url, attemptOptions);
                default: //快速重试
                    if (GetTimeMs() - startTime <= 5500) throw retryException!; //若前两次加载耗费 5 秒以上，才进行重试
                    Thread.Sleep(500);
                    attemptOptions.TimeoutMs = 4000;
                    return NetRequestByClient(url, attemptOptions);
                }
            } catch (Exception ex)
            {
                ex.ThrowIfCanceled();
                if (ex is HttpRequestCodeException codeException)
                {
                    if (codeException.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) throw;
                    if ((int) codeException.StatusCode == 429) Thread.Sleep(10000); //Too Many Requests
                }
                //重试
                switch (retryCount)
                {
                case 0:
                    retryException = ex;
                    retryCount += 1;
                    continue; //GoTo Retry
                case 1:
                    retryCount += 1;
                    continue; //GoTo Retry
                default:
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// 生成数个线程，同时发送网络请求并获取返回内容（ModNet.vb:93-138 的多 URL 收敛：每个 URL 一个线程竞速）。
    /// 每个线程开始之前会略有延迟；全部完成或全部失败后以 Thread.Interrupt 收尾。
    /// </summary>
    /// <returns>所有成功 URL 的响应体（按传入顺序）；全部失败时抛出最后一次的异常。</returns>
    public static List<string> NetRequestByClientMultiple(IEnumerable<string> urls, string? accept = null, bool requireJson = false, Func<bool>? runInUi = null)
        => NetRequestByClientMultiple(urls, new NetRequestOptions { Accept = accept, RequireJson = requireJson, RunInUi = runInUi });

    /// <summary>多 URL 竞速的完整选项入口，供后续下载任务使用。</summary>
    public static List<string> NetRequestByClientMultiple(IEnumerable<string> urls, NetRequestOptions options)
    {
        var urlList = urls as IList<string> ?? urls.ToList();
        var bodies = new string?[urlList.Count];
        Exception? requestEx = null;
        var failCount = 0;
        var threads = new List<Thread>();
        for (var i = 0; i < urlList.Count; i++)
        {
            var index = i;
            var thread = new Thread(() =>
            {
                try
                {
                    bodies[index] = NetRequestByClient(urlList[index], options);
                } catch (Exception ex)
                {
                    failCount++;
                    requestEx = ex;
                }
            });
            thread.Start();
            threads.Add(thread);
            Thread.Sleep((i + 1) * 250); //ModNet.vb:113 每个线程开始之前会略有延迟
        }
        while (bodies.Count(static body => body != null) + failCount < urlList.Count) Thread.Sleep(20); //等待全部完成或全部失败
        InterruptThreads(threads); //ModNet.vb:119-124 的收尾
        if (failCount < urlList.Count) return bodies.Where(static body => body != null).Select(static body => body!).ToList();
        InterruptThreads(threads); //ModNet.vb:127-132 的收尾
        throw requestEx!;
    }

    /// <summary>
    /// 单 URL 多线程竞速（ModNet.vb:93-138 逐字语义：先成功者胜出并中断其余线程），供 VB 侧转发保持既有行为。
    /// </summary>
    public static string NetRequestByClientMultiple(string url, int threadCount, NetRequestOptions options)
    {
        var threads = new List<Thread>();
        string? requestResult = null;
        Exception? requestEx = null;
        var failCount = 0;
        for (var i = 1; i <= threadCount; i++)
        {
            var thread = new Thread(() =>
            {
                try
                {
                    requestResult = NetRequestByClient(url, options);
                } catch (Exception ex)
                {
                    failCount++;
                    requestEx = ex;
                }
            });
            thread.Start();
            threads.Add(thread);
            Thread.Sleep(i * 250); //ModNet.vb:113
            if (requestResult != null) break; //ModNet.vb:114 GoTo RequestFinished
        }
        while (true) //ModNet.vb:116-136
        {
            if (requestResult != null)
            {
                InterruptThreads(threads);
                return requestResult;
            }
            if (failCount == threadCount)
            {
                InterruptThreads(threads);
                throw requestEx!;
            }
            Thread.Sleep(20);
        }
    }

    /// <summary>ModNet.vb:119-124 / 127-132 的中断收尾。</summary>
    private static void InterruptThreads(List<Thread> threads)
    {
        try
        {
            foreach (var thread in threads)
            {
                if (thread.IsAlive) thread.Interrupt();
            }
        } catch
        {
            //VB 侧为空 catch：中断竞态时静默
        }
    }

    /// <summary>通过网络请求直接下载小文件，若文件已存在将被覆盖（ModNet.vb:172-187，3 次重试）。</summary>
    public static bool NetDownloadByClient(string url, string localPath, Func<bool>? runInUi = null)
        => NetDownloadByClient(url, localPath, new NetRequestOptions { RunInUi = runInUi, TimeoutMs = 25000 }); //VB 旧 SendRequest 默认 Timeout=25000，显式对齐以免落入选项默认 15000

    /// <summary>通过网络请求直接下载小文件（完整选项入口，供 VB 侧转发）；成功返回 true，失败抛出异常。</summary>
    public static bool NetDownloadByClient(string url, string localPath, NetRequestOptions options)
    {
        Logger.Info($"通过网络请求直接下载小文件：{url} → {localPath}");
        var retryCount = 0;
        while (true)
        {
            try
            {
                FileUtils.Write(localPath, SendRequest(url, options).Bytes);
                return true;
            } catch (Exception ex)
            {
                FileUtils.Delete(localPath);
                ex.ThrowIfCanceled();
                if (retryCount >= 2) throw new HttpRequestException($"通过网络请求直接下载小文件失败（{localPath}）", ex);
                retryCount++; //GoTo Retry（落回循环开头）
            }
        }
    }

    /// <summary>测试 Ping。失败则返回 -1（ModNet.vb:1892-1907）。</summary>
    /// <remarks>ModNet.vb:1895 从未把 Timeout 传给 Ping.Send，timeoutMs 仅为签名兼容保留；VB 的 MakeLog 参数无现调用方，C# 固定记日志。</remarks>
    public static int Ping(string host, int timeoutMs = 3000)
    {
        PingReply? pingResult;
        try
        {
            pingResult = new Ping().Send(host); //VB :1895 逐字：Send 未传超时
        } catch (Exception ex)
        {
            Logger.Info($"Ping {host} 失败：{ex.Message}");
            return -1;
        }
        if (pingResult?.Status == IPStatus.Success)
        {
            Logger.Info($"Ping {host} 结束：{pingResult.RoundtripTime}ms");
            return (int) pingResult.RoundtripTime;
        }
        Logger.Info($"Ping {host} 失败");
        return -1;
    }

    /// <summary>随机获取单个可用的端口（ModNet.vb:1855-1861）。</summary>
    public static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>获取当前已被占用的端口列表（ModNet.vb:1866-1873）。</summary>
    public static List<int> GetUsedPorts()
    {
        var ipProperties = IPGlobalProperties.GetIPGlobalProperties();
        var usedPorts = new List<int>();
        usedPorts.AddRange(ipProperties.GetActiveTcpListeners().Select(static ep => ep.Port));
        usedPorts.AddRange(ipProperties.GetActiveUdpListeners().Select(static ep => ep.Port));
        usedPorts.AddRange(ipProperties.GetActiveTcpConnections().Select(static conn => conn.LocalEndPoint.Port));
        return usedPorts.Distinct().ToList();
    }

    /// <summary>寻找数个连续编号的可用端口（ModNet.vb:1878-1885）。</summary>
    public static List<int> FindFreePorts(int count) => FindFreePorts(count, []);

    /// <summary>寻找数个连续编号的可用端口，额外拉黑指定端口（供 VB 侧转发 ParamArray 签名）。</summary>
    public static List<int> FindFreePorts(int count, params int[] extraBlacklists)
    {
        var usedPorts = GetUsedPorts().Concat(extraBlacklists).ToHashSet();
        for (var port = 12000 + new Random().Next(0, 1001); port <= 65000 - count; port++) //VB RandomInteger(0, 1000) 含两端
        {
            var range = Enumerable.Range(port, count).ToList();
            if (!range.Any(usedPorts.Contains)) return range;
        }
        throw new Exception("未能找到可用的端口！");
    }

    /// <summary>设置是否应当在正版登录时校验 SSL 证书（ModNet.vb:1912-1927）；按 ToolDownloadCert 设置生效。</summary>
    public static void ShouldValidateSslCertificateOnLogin() => ShouldValidateSslCertificateOnLogin(SettingsService.Get<bool>("ToolDownloadCert"));

    /// <summary>设置是否应当在正版登录时校验 SSL 证书（供 VB 侧转发设置回调的逐字签名；进程级副作用与 VB 一致）。</summary>
    public static void ShouldValidateSslCertificateOnLogin(bool enabled)
    {
        ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, _, failure) =>
        {
            var request = sender as HttpWebRequest;
            if (failure == SslPolicyErrors.None) return true; //已通过验证
            //基于 #3018 和 #5879，只在访问正版登录 API 时跳过证书验证
            Logger.Warn($"未通过 SSL 证书验证（{failure}），提供的证书为 {certificate?.Subject}，URL：{request?.Address}");
            if (request == null || request.Address.Host.Contains("xboxlive") || request.Address.Host.Contains("minecraftservices"))
                return !enabled; //根据设置决定是否忽略错误
            return false;
        };
    }

    /// <summary>为 Task 设置超时，在超时时抛出 TimeoutException（ModBase.vb:764 扩展的私有等价实现，逐字语义）。</summary>
    private static T GetResultWithTimeout<T>(this Task<T> targetTask, CancellationTokenSource tokenSource, int timeoutMs)
    {
        var delayTask = Task.Delay(timeoutMs);
        if (Task.WhenAny(targetTask, delayTask).ConfigureAwait(false).GetAwaiter().GetResult() != delayTask)
            return targetTask.GetAwaiter().GetResult(); //VB 的 TargetTask.Run()
        tokenSource.Cancel();
        throw new TimeoutException($"任务超时（{timeoutMs} ms）");
    }

    /// <summary>为 Task 设置超时，在超时时抛出 TimeoutException（ModBase.vb:775 扩展的私有等价实现，逐字语义）。</summary>
    private static void GetResultWithTimeout(this Task targetTask, CancellationTokenSource tokenSource, int timeoutMs)
    {
        var delayTask = Task.Delay(timeoutMs);
        if (Task.WhenAny(targetTask, delayTask).ConfigureAwait(false).GetAwaiter().GetResult() != delayTask)
        {
            targetTask.GetAwaiter().GetResult();
            return;
        }
        tokenSource.Cancel();
        throw new TimeoutException($"任务超时（{timeoutMs} ms）");
    }
}