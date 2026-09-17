using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalManagement.Services;

/// <summary>设备码授权凭据:用户码要展示给用户,设备码用于轮询换令牌。</summary>
public sealed record BaiduDeviceCode(
    string DeviceCode,
    string UserCode,
    string VerificationUrl,
    string QrcodeUrl,
    int ExpiresInSeconds,
    int IntervalSeconds);

/// <summary>一次授权得到的令牌。refresh_token 每次刷新都会换新,必须覆盖保存。</summary>
public sealed record BaiduToken(string AccessToken, string RefreshToken, int ExpiresInSeconds);

/// <summary>绑定的百度账号信息。</summary>
public sealed record BaiduUserInfo(string Name, string Uk);

/// <summary>云端一个文件或目录。</summary>
public sealed record CloudEntry(
    long FsId,
    string Path,
    string Name,
    long Size,
    bool IsDir,
    DateTime? LocalMtimeUtc,
    string? Md5);

/// <summary>
/// 百度网盘开放平台的薄 HTTP 层:只管调接口,不含业务判断。
/// 网络走 .NET 默认系统代理(HttpClient.DefaultProxy,自动适配本机代理),请勿禁用。
/// </summary>
public sealed class BaiduPanClient
{
    private const string OauthBase = "https://openapi.baidu.com";
    private const string PanBase = "https://pan.baidu.com";
    private const string PcsBase = "https://d.pcs.baidu.com";

    /// <summary>百度强制要求:所有网盘接口(含 dlink 下载)都必须带这个 UA,否则 403。</summary>
    private const string UserAgent = "pan.baidu.com";

    /// <summary>授权接口固定要求的权限范围。</summary>
    private const string Scope = "basic,netdisk";

    /// <summary>普通账号单个分片固定 4MB,不可更改。</summary>
    public const int PartSize = 4 * 1024 * 1024;

    /// <summary>
    /// 内置的开放平台应用凭证。用户注册应用后填这里,或在「云存档」页里覆盖(设置优先)。
    /// 留空时云存档功能会提示「尚未配置 AppKey」。
    /// </summary>
    public const string DefaultAppKey = "";

    /// <inheritdoc cref="DefaultAppKey"/>
    public const string DefaultSecretKey = "";

    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(15);

    private readonly HttpClient _http;

    /// <summary>开放平台应用的 AppKey,调用前由 CloudSaveService 注入。</summary>
    public string? AppKey { get; set; }

    /// <summary>开放平台应用的 SecretKey,调用前由 CloudSaveService 注入。</summary>
    public string? SecretKey { get; set; }

    /// <summary>当前 access_token,调用前由 CloudSaveService 注入。</summary>
    public string? AccessToken { get; set; }

    public BaiduPanClient()
    {
        // 超时统一由每次请求的 CancellationToken 控制,避免 HttpClient.Timeout 掐断慢速大文件传输
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _ = _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
    }

    // ---------- 纯函数(无网络,可无头验证) ----------

    private const string InvalidNameChars = "\\/:*?\"<>|";

    /// <summary>
    /// 清理一段名字使其能安全地成为云端路径的一段:去掉路径分隔符与 Windows 非法字符、
    /// 去掉控制字符与首尾空白、去掉结尾的点(Windows 不允许),并杜绝 "." / ".." 造成的路径穿越。
    /// </summary>
    public static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "未命名";

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            if (ch < 0x20 || InvalidNameChars.Contains(ch))
                continue;
            sb.Append(ch);
        }

        var cleaned = sb.ToString().Trim().TrimEnd('.', ' ');
        return cleaned.Length == 0 || cleaned is "." or ".." ? "未命名" : cleaned;
    }

    /// <summary>拼接云端沙箱路径:百度只允许应用操作 /apps/{应用名}/ 下的内容。</summary>
    public static string BuildRemotePath(string appFolder, string gameName) =>
        $"/apps/{SanitizeName(appFolder)}/{SanitizeName(gameName)}";

    /// <summary>按 4MB 切片。空文件也返回一片(长度 0),因为百度要求 block_list 非空。</summary>
    public static IReadOnlyList<(long Offset, int Length)> SplitParts(long size, int partSize = PartSize)
    {
        if (size <= 0)
            return [(0, 0)];

        var parts = new List<(long, int)>((int)((size + partSize - 1) / partSize));
        for (long offset = 0; offset < size; offset += partSize)
            parts.Add((offset, (int)Math.Min(partSize, size - offset)));
        return parts;
    }

    /// <summary>算出百度要求的 block_list(每片 md5,32 位小写)。</summary>
    public static List<string> ComputeBlockList(string localPath)
    {
        var size = new FileInfo(localPath).Length;
        var list = new List<string>();
        foreach (var (offset, length) in SplitParts(size))
            list.Add(Md5HexOfRange(localPath, offset, length));
        return list;
    }

    public static long ToUnixSeconds(DateTime utc) =>
        (long)(utc.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds;

    public static DateTime FromUnixSeconds(long seconds) =>
        DateTime.UnixEpoch.AddSeconds(seconds);

    // ---------- 授权 ----------

    /// <summary>获取设备码与用户码;用户码要展示给用户,让他在授权页输入。</summary>
    public async Task<BaiduDeviceCode> RequestDeviceCodeAsync(CancellationToken ct = default)
    {
        var url = $"{OauthBase}/oauth/2.0/device/code?response_type=device_code" +
                  $"&client_id={Uri.EscapeDataString(RequireAppKey())}" +
                  $"&scope={Uri.EscapeDataString(Scope)}";

        var (dto, body) = await GetJsonRawAsync<DeviceCodeDto>(url, ct);
        if (string.IsNullOrWhiteSpace(dto?.DeviceCode))
            throw new InvalidDataException($"百度网盘未返回设备码,请确认 AppKey 是否填写正确。{Truncate(body, 200)}");

        return new BaiduDeviceCode(
            dto.DeviceCode,
            dto.UserCode ?? string.Empty,
            dto.VerificationUrl ?? string.Empty,
            dto.QrcodeUrl ?? string.Empty,
            dto.ExpiresIn <= 0 ? 300 : dto.ExpiresIn,
            dto.Interval <= 0 ? 5 : dto.Interval);
    }

    /// <summary>
    /// 用设备码轮询换令牌,直到用户完成授权或设备码过期。
    /// 官方没公开「尚未授权」的错误码,所以非成功响应一律当作「继续等」,不会误判成失败。
    /// </summary>
    public async Task<BaiduToken> PollDeviceTokenAsync(
        BaiduDeviceCode device, IProgress<int>? secondsLeft = null, CancellationToken ct = default)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, device.IntervalSeconds));
        var deadline = DateTime.UtcNow.AddSeconds(device.ExpiresInSeconds);
        var lastBody = string.Empty;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(interval, ct);
            secondsLeft?.Report((int)Math.Max(0, (deadline - DateTime.UtcNow).TotalSeconds));

            var url = $"{OauthBase}/oauth/2.0/token?grant_type=device_token" +
                      $"&code={Uri.EscapeDataString(device.DeviceCode)}" +
                      $"&client_id={Uri.EscapeDataString(RequireAppKey())}" +
                      $"&client_secret={Uri.EscapeDataString(RequireSecretKey())}";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(ApiTimeout);

            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            lastBody = await ReadBodyAsync(resp.Content, cts.Token);

            var dto = TryDeserialize<TokenDto>(lastBody);
            if (!string.IsNullOrWhiteSpace(dto?.AccessToken))
                return new BaiduToken(dto.AccessToken, dto.RefreshToken ?? string.Empty, dto.ExpiresIn);
        }

        throw new TimeoutException($"授权超时(设备码已失效),请重新发起绑定。最后一次响应:{Truncate(lastBody, 200)}");
    }

    /// <summary>刷新令牌;返回的 refresh_token 是新的,旧的立即作废。</summary>
    public async Task<BaiduToken> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        var url = $"{OauthBase}/oauth/2.0/token?grant_type=refresh_token" +
                  $"&refresh_token={Uri.EscapeDataString(refreshToken)}" +
                  $"&client_id={Uri.EscapeDataString(RequireAppKey())}" +
                  $"&client_secret={Uri.EscapeDataString(RequireSecretKey())}";

        var (dto, body) = await GetJsonRawAsync<TokenDto>(url, ct);
        if (string.IsNullOrWhiteSpace(dto?.AccessToken))
            throw new InvalidOperationException($"授权已失效,请重新绑定账号。{Truncate(body, 200)}");

        return new BaiduToken(dto.AccessToken, dto.RefreshToken ?? string.Empty, dto.ExpiresIn);
    }

    /// <summary>确认令牌有效并取回账号昵称。</summary>
    public async Task<BaiduUserInfo> GetUserInfoAsync(CancellationToken ct = default)
    {
        var url = $"{PanBase}/rest/2.0/xpan/nas?method=uinfo&access_token={Token()}";
        var dto = await GetJsonAsync<UInfoDto>(url, ct);
        if (dto.Errno != 0)
            throw new InvalidOperationException($"获取账号信息失败(errno={dto.Errno})。");

        return new BaiduUserInfo(dto.BaiduName ?? string.Empty, dto.Uk?.ToString() ?? string.Empty);
    }

    // ---------- 文件操作 ----------

    /// <summary>逐级创建云端目录;已存在时忽略。</summary>
    public async Task EnsureDirectoryAsync(string remoteDir, CancellationToken ct = default)
    {
        var current = string.Empty;
        foreach (var segment in remoteDir.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + segment;

            // /apps 是百度的系统目录,已存在但应用无权对它调 create(会报 errno=102),必须跳过。
            // 应用只能从属于自己的 /apps/{应用名}/ 开始建。
            if (current == "/apps")
                continue;

            var url = $"{PanBase}/rest/2.0/xpan/file?method=create&access_token={Token()}";
            var dto = await PostFormAsync<ErrnoDto>(url, new Dictionary<string, string>
            {
                ["path"] = current,
                ["isdir"] = "1",
                ["rtype"] = "0",
            }, ct);

            // -8 = 已存在,属正常
            if (dto.Errno != 0 && dto.Errno != -8)
                throw new HttpRequestException($"创建云端目录失败(errno={dto.Errno}):{current}");
        }
    }

    /// <summary>列出一个目录(非递归,自动翻页);目录不存在时返回空列表。</summary>
    public async Task<List<CloudEntry>> ListAsync(string dir, CancellationToken ct = default)
    {
        var result = new List<CloudEntry>();
        var start = 0;

        while (true)
        {
            var url = $"{PanBase}/rest/2.0/xpan/file?method=list&order=name" +
                      $"&dir={Uri.EscapeDataString(dir)}&start={start}&limit=1000&web=1" +
                      $"&access_token={Token()}";
            var dto = await GetJsonAsync<ListDto>(url, ct);

            if (dto.Errno == -9)        // 目录不存在
                return result;
            if (dto.Errno != 0)
                throw new HttpRequestException($"列云端目录失败(errno={dto.Errno}):{dir}");

            var items = dto.List ?? [];
            foreach (var item in items)
            {
                result.Add(new CloudEntry(
                    item.FsId ?? 0,
                    item.Path ?? string.Empty,
                    item.ServerFilename ?? string.Empty,
                    item.Size,
                    item.IsDir == 1,
                    item.LocalMtime > 0 ? FromUnixSeconds(item.LocalMtime) : null,
                    item.Md5));
            }

            if (items.Count < 1000)
                return result;
            start += items.Count;
        }
    }

    /// <summary>
    /// 上传一个文件(覆盖同名)。≤4MB 一次传完,更大时按 4MB 切片逐个上传。
    /// 进度按字节上报;串行发送,避免撞上免费账号的低 QPS 限制。
    /// </summary>
    public async Task UploadFileAsync(
        string localPath, string remotePath, DateTime mtimeUtc, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var size = new FileInfo(localPath).Length;
        var blockList = await Task.Run(() => ComputeBlockList(localPath), ct);
        var (uploadId, missingBlocks) = await PrecreateAsync(remotePath, size, blockList, mtimeUtc, ct);
        progress?.Report(5);

        if (uploadId is not null)
        {
            var parts = SplitParts(size);

            // 只补传云端还缺的分片;万一响应没给缺片列表,保守起见全传一遍。
            // (不要用 return_type 判断要不要传:实测 return_type=1 时依然必须先传分片,
            //  跳过会让后面的 create 报 errno=31500。)
            var sequences = missingBlocks is null
                ? Enumerable.Range(0, parts.Count)
                : missingBlocks.Where(i => i >= 0 && i < parts.Count).Distinct().OrderBy(i => i);

            long sent = 0;
            foreach (var seq in sequences)
            {
                await UploadPartAsync(localPath, remotePath, uploadId, seq, parts[seq], ct);
                sent += parts[seq].Length;
                progress?.Report(size > 0 ? 5 + sent * 85.0 / size : 90);
            }
        }

        await CreateFileAsync(remotePath, size, blockList, uploadId, mtimeUtc, ct);
        progress?.Report(100);
    }

    /// <summary>下载一个云端文件;dlink 的 302 跳转由 HttpClient 自动跟随。</summary>
    public async Task DownloadFileAsync(
        long fsId, string localPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var metaUrl = $"{PanBase}/rest/2.0/xpan/multimedia?method=filemetas" +
                      $"&fsids={Uri.EscapeDataString($"[{fsId}]")}&dlink=1&access_token={Token()}";
        var meta = await GetJsonAsync<FileMetasDto>(metaUrl, ct);

        if (meta.Errno != 0 || meta.List is null || meta.List.Count == 0)
            throw new HttpRequestException($"获取下载地址失败(errno={meta.Errno})。");

        var dlink = meta.List[0].Dlink?.Replace("\\u0026", "&");
        if (string.IsNullOrWhiteSpace(dlink))
            throw new InvalidDataException("百度网盘未返回下载地址。");

        var url = dlink.Contains('?') ? $"{dlink}&access_token={Token()}" : $"{dlink}?access_token={Token()}";

        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TransferTimeout);

        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;

        await using var src = await resp.Content.ReadAsStreamAsync(cts.Token);
        await using var dst = File.Create(localPath);
        var buffer = new byte[64 * 1024];
        long copied = 0;
        int read;
        while ((read = await src.ReadAsync(buffer, cts.Token)) > 0)
        {
            await dst.WriteAsync(buffer, 0, read, cts.Token);
            copied += read;
            if (total is > 0)
                progress?.Report(copied * 100.0 / total.Value);
        }
    }

    /// <summary>
    /// 删除云端一个文件或目录。实测只有 async=1 配单个 path 能过,所以一次删一个。
    /// 注意 async=1 是「入队」:接口立刻返回,百度在后台慢慢处理,
    /// 删完不会当场反映到 list 结果上,调用方别指望删完立刻查不到。
    /// </summary>
    public async Task DeleteAsync(string remotePath, CancellationToken ct = default)
    {
        var url = $"{PanBase}/rest/2.0/xpan/file?method=delete&access_token={Token()}";
        var dto = await PostFormAsync<ErrnoDto>(url, new Dictionary<string, string>
        {
            ["async"] = "1",
            ["path"] = remotePath,
        }, ct);

        // -9 = 目标不存在,当作已删掉,重试时能幂等
        if (dto.Errno != 0 && dto.Errno != -9)
            throw new HttpRequestException($"删除云端文件失败(errno={dto.Errno}):{remotePath}");
    }

    // ---------- 分片上传内部步骤 ----------

    /// <summary>
    /// 预上传。返回 uploadid(为 null 才表示秒传命中、云端已有同样内容)与「云端还缺的分片下标」。
    /// 注意响应里的 block_list 是缺片下标数组,与请求里那个 md5 字符串数组同名但含义相反。
    /// </summary>
    private async Task<(string? UploadId, List<int>? MissingBlocks)> PrecreateAsync(
        string remotePath, long size, List<string> blockList, DateTime mtimeUtc, CancellationToken ct)
    {
        var url = $"{PanBase}/rest/2.0/xpan/file?method=precreate&access_token={Token()}";
        var dto = await PostFormAsync<PrecreateDto>(url, new Dictionary<string, string>
        {
            ["path"] = remotePath,
            ["size"] = size.ToString(),
            ["isdir"] = "0",
            ["autoinit"] = "1",
            ["rtype"] = "0",
            ["local_mtime"] = ToUnixSeconds(mtimeUtc).ToString(),
            ["block_list"] = JsonSerializer.Serialize(blockList),
        }, ct);

        if (dto.Errno == 31024)
            throw new InvalidOperationException("该应用尚未获得上传权限,请先在百度网盘开放平台申请「上传」权限。");
        if (dto.Errno != 0)
            throw new HttpRequestException($"预上传失败(errno={dto.Errno}):{remotePath}");

        return (dto.UploadId, dto.MissingBlocks);
    }

    private async Task UploadPartAsync(
        string localPath, string remotePath, string uploadId, int partSeq, (long Offset, int Length) part,
        CancellationToken ct)
    {
        var url = $"{PcsBase}/rest/2.0/pcs/superfile2?method=upload&access_token={Token()}" +
                  $"&type=tmpfile&path={Uri.EscapeDataString(remotePath)}" +
                  $"&uploadid={Uri.EscapeDataString(uploadId)}&partseq={partSeq}";

        using var form = new MultipartFormDataContent();
        var bytes = await Task.Run(() => ReadRange(localPath, part.Offset, part.Length), ct);
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, "file", "part");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TransferTimeout);

        using var resp = await _http.PostAsync(url, form, cts.Token);
        var body = await ReadBodyAsync(resp.Content, cts.Token);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"分片上传失败({(int)resp.StatusCode}):{Truncate(body, 200)}");

        var dto = TryDeserialize<ErrnoDto>(body);
        if (dto is not null && dto.Errno != 0)
            throw new HttpRequestException($"分片上传失败(errno={dto.Errno},第 {partSeq + 1} 片)。");
    }

    private async Task CreateFileAsync(
        string remotePath, long size, List<string> blockList, string? uploadId, DateTime mtimeUtc,
        CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["path"] = remotePath,
            ["size"] = size.ToString(),
            ["isdir"] = "0",
            // rtype 必须是 3 才是「原地覆盖」。实测(同路径先后写 100B 与 300B 不同内容):
            //   0 → 文件已存在时 errno=-8 直接失败
            //   1 → 改名另存 rc1_<时间戳>.txt,云端越堆越多
            //   2 → 改名另存 rc2(1).txt,同样是堆副本
            //   3 → 覆盖成功,仍是原名 rc3.txt(内容已变 300B)
            ["rtype"] = "3",
            ["local_mtime"] = ToUnixSeconds(mtimeUtc).ToString(),
            ["block_list"] = JsonSerializer.Serialize(blockList),
        };
        if (uploadId is not null)
            form["uploadid"] = uploadId;

        var url = $"{PanBase}/rest/2.0/xpan/file?method=create&access_token={Token()}";
        var dto = await PostFormAsync<ErrnoDto>(url, form, ct);
        if (dto.Errno != 0)
            throw new HttpRequestException($"创建云端文件失败(errno={dto.Errno}):{remotePath}");
    }

    // ---------- 底层 HTTP ----------

    private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct) where T : class
    {
        var (dto, body) = await GetJsonRawAsync<T>(url, ct);
        return dto ?? throw new InvalidDataException($"百度网盘返回内容无法解析:{Truncate(body, 200)}");
    }

    /// <summary>同时返回原始响应体,便于在授权类接口出错时把百度的话原样带给用户。</summary>
    private async Task<(T? Dto, string Body)> GetJsonRawAsync<T>(string url, CancellationToken ct) where T : class
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ApiTimeout);

        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        var body = await ReadBodyAsync(resp.Content, cts.Token);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"百度网盘请求失败({(int)resp.StatusCode}):{Truncate(body, 200)}");

        return (TryDeserialize<T>(body), body);
    }

    private async Task<T> PostFormAsync<T>(string url, Dictionary<string, string> form, CancellationToken ct)
        where T : class
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ApiTimeout);

        using var content = new FormUrlEncodedContent(form);
        using var resp = await _http.PostAsync(url, content, cts.Token);
        var body = await ReadBodyAsync(resp.Content, cts.Token);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"百度网盘请求失败({(int)resp.StatusCode}):{Truncate(body, 200)}");

        return TryDeserialize<T>(body)
            ?? throw new InvalidDataException($"百度网盘返回内容无法解析:{Truncate(body, 200)}");
    }

    /// <summary>解析失败返回 null(轮询授权时要靠它区分「还没授权」和「真出错」)。</summary>
    private static T? TryDeserialize<T>(string body) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string Token() => Uri.EscapeDataString(
        string.IsNullOrWhiteSpace(AccessToken)
            ? throw new InvalidOperationException("尚未绑定百度网盘账号,请在左侧「云存档」页里绑定。")
            : AccessToken);

    private string RequireAppKey() => string.IsNullOrWhiteSpace(AppKey)
        ? throw new InvalidOperationException("尚未配置百度网盘 AppKey,请在「云存档」页里填写。")
        : AppKey;

    private string RequireSecretKey() => string.IsNullOrWhiteSpace(SecretKey)
        ? throw new InvalidOperationException("尚未配置百度网盘 SecretKey,请在「云存档」页里填写。")
        : SecretKey;

    private static byte[] ReadRange(string path, long offset, int count)
    {
        var buffer = new byte[count];
        using var fs = File.OpenRead(path);
        fs.Seek(offset, SeekOrigin.Begin);

        var read = 0;
        while (read < count)
        {
            var n = fs.Read(buffer, read, count - read);
            if (n <= 0)
                break;
            read += n;
        }
        return read == count ? buffer : buffer[..read];
    }

    private static string Md5HexOfRange(string path, long offset, int count)
    {
        using var fs = File.OpenRead(path);
        fs.Seek(offset, SeekOrigin.Begin);
        using var md5 = MD5.Create();

        var buffer = new byte[64 * 1024];
        var remaining = count;
        while (remaining > 0)
        {
            var n = fs.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (n <= 0)
                break;
            md5.TransformBlock(buffer, 0, n, null, 0);
            remaining -= n;
        }
        md5.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(md5.Hash ?? []).ToLowerInvariant();
    }

    /// <summary>
    /// 按 UTF-8 解响应体。百度部分接口会返回 charset 非法的 Content-Type,
    /// 那时 HttpContent.ReadAsStringAsync 会直接抛「invalid character set」,拿不到真正的错误码。
    /// 百度的响应恒为 UTF-8 JSON,自己解字节比信任响应头更可靠。
    /// </summary>
    private static async Task<string> ReadBodyAsync(HttpContent content, CancellationToken ct) =>
        Encoding.UTF8.GetString(await content.ReadAsByteArrayAsync(ct));

    private static string Truncate(string? text, int max) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Length <= max ? text : text[..max] + "…";

    // ---------- 接口返回的数据结构 ----------

    private sealed class DeviceCodeDto
    {
        [JsonPropertyName("device_code")] public string? DeviceCode { get; set; }
        [JsonPropertyName("user_code")] public string? UserCode { get; set; }
        [JsonPropertyName("verification_url")] public string? VerificationUrl { get; set; }
        [JsonPropertyName("qrcode_url")] public string? QrcodeUrl { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
    }

    private sealed class TokenDto
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private sealed class UInfoDto
    {
        [JsonPropertyName("errno")] public int Errno { get; set; }
        [JsonPropertyName("baidu_name")] public string? BaiduName { get; set; }
        [JsonPropertyName("uk")] public long? Uk { get; set; }
    }

    private sealed class ErrnoDto
    {
        [JsonPropertyName("errno")] public int Errno { get; set; }
    }

    private sealed class ListDto
    {
        [JsonPropertyName("errno")] public int Errno { get; set; }
        [JsonPropertyName("list")] public List<ListItemDto>? List { get; set; }
    }

    private sealed class ListItemDto
    {
        [JsonPropertyName("fs_id")] public long? FsId { get; set; }
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("server_filename")] public string? ServerFilename { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("isdir")] public int IsDir { get; set; }
        [JsonPropertyName("local_mtime")] public long LocalMtime { get; set; }
        [JsonPropertyName("md5")] public string? Md5 { get; set; }
    }

    private sealed class PrecreateDto
    {
        [JsonPropertyName("errno")] public int Errno { get; set; }
        [JsonPropertyName("uploadid")] public string? UploadId { get; set; }

        /// <summary>响应里的缺片下标(与请求里同名的 md5 数组不是一回事)。</summary>
        [JsonPropertyName("block_list")] public List<int>? MissingBlocks { get; set; }
    }

    private sealed class FileMetasDto
    {
        [JsonPropertyName("errno")] public int Errno { get; set; }
        [JsonPropertyName("list")] public List<FileMetaItemDto>? List { get; set; }
    }

    private sealed class FileMetaItemDto
    {
        [JsonPropertyName("dlink")] public string? Dlink { get; set; }
    }
}
