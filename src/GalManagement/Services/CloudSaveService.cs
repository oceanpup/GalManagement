using System.IO;
using System.Net.Http;
using System.Text.Json;
using GalManagement.Models;

namespace GalManagement.Services;

/// <summary>同步档位:默认每次都问。</summary>
public enum SaveSyncMode
{
    /// <summary>弹窗询问用户。</summary>
    Ask,

    /// <summary>静默把较新的一方同步过来。</summary>
    Auto,

    /// <summary>完全不做云存档。</summary>
    Off,
}

/// <summary>本地或云端一侧的存档概况。</summary>
public sealed record SaveStamp(int FileCount, DateTime? MaxMtimeUtc);

/// <summary>本地与云端存档的新旧关系。</summary>
public enum SaveSyncState
{
    /// <summary>本地没有设置存档目录,或目录里没有文件。</summary>
    LocalMissing,

    /// <summary>云端还没有这个游戏的存档。</summary>
    CloudMissing,

    /// <summary>云端缺收尾标记,上次上传没传完。</summary>
    CloudIncomplete,

    /// <summary>两侧都有且时间戳一致。</summary>
    Same,

    /// <summary>本地更新。</summary>
    LocalNewer,

    /// <summary>云端更新。</summary>
    RemoteNewer,
}

/// <summary>一次新旧比对的结论。</summary>
public sealed record SaveCompare(SaveSyncState State, SaveStamp Local, SaveStamp Remote);

/// <summary>
/// 云端一个游戏目录的概况。FileCount / TotalBytes / MaxMtimeUtc 只统计真正的存档文件,
/// 不收尾标记 _meta.json;IsComplete 为 false 说明那目录里没找到收尾标记,上次没传完。
/// </summary>
public sealed record CloudGameDir(
    string DirName, string Path, int FileCount, long TotalBytes, DateTime? MaxMtimeUtc, bool IsComplete);

/// <summary>删云端存档的结果:删掉的文件数,以及没删掉的残留目录数(空目录,不在列表里显示)。</summary>
public sealed record CloudDeleteResult(int FileCount, int LeftoverDirs);

/// <summary>本地一个待上传的存档文件;相对路径统一用 / 分隔,与云端一致。</summary>
public sealed record LocalFile(string RelativePath, string FullPath, DateTime MtimeUtc);

/// <summary>
/// 云端存档业务层:令牌维护 + 逐文件上传下载 + 新旧比对。UI 无关,
/// 纯逻辑(名字清理、路径安全、时间戳判定)都做成静态函数以便无头验证。
/// </summary>
public sealed class CloudSaveService
{
    /// <summary>云端沙箱根目录名,必须与开放平台上注册的应用名一致。</summary>
    public const string AppFolderName = "旮旯给木管理";

    /// <summary>收尾标记文件名:最后上传,云端缺它说明上次没传完。</summary>
    public const string MetaFileName = "_meta.json";

    /// <summary>
    /// 时间戳容差:local_mtime 是 unix 秒,本地是 100ns 精度,上传会把本地时间截断到秒,
    /// 不设容差会让本地看起来永远"新"不到 1 秒,反复提示不一致。
    /// </summary>
    private const double ToleranceSeconds = 2;

    private readonly SettingsService _settings;
    private readonly string _stagingDir;
    private readonly BaiduPanClient _client = new();

    public CloudSaveService(SettingsService settings, string dataDirectory)
    {
        _settings = settings;
        _stagingDir = Path.Combine(dataDirectory, "sync");
    }

    private AppSettings Settings => _settings.Current;

    /// <summary>账号是否已绑定。</summary>
    public bool IsBound => !string.IsNullOrWhiteSpace(Settings.BaiduAccessToken);

    /// <summary>已绑定的百度账号昵称。</summary>
    public string BoundAccountName => Settings.BaiduUserName ?? string.Empty;

    /// <summary>AppKey / SecretKey 是否已具备(设置页填过,或代码常量里有)。</summary>
    public bool HasCredentials
    {
        get
        {
            ApplyCredentials();
            return !string.IsNullOrWhiteSpace(_client.AppKey) && !string.IsNullOrWhiteSpace(_client.SecretKey);
        }
    }

    public SaveSyncMode BeforeLaunchMode => ParseMode(Settings.SyncModeBeforeLaunch);

    public SaveSyncMode AfterPlayMode => ParseMode(Settings.SyncModeAfterPlay);

    public static SaveSyncMode ParseMode(string? raw) =>
        Enum.TryParse<SaveSyncMode>(raw, out var mode) ? mode : SaveSyncMode.Ask;

    /// <summary>云端沙箱根目录:所有游戏的存档目录都挂在这下面。</summary>
    public static string AppRoot => $"/apps/{BaiduPanClient.SanitizeName(AppFolderName)}";

    /// <summary>该游戏在云端的目录(百度只允许应用操作 /apps/{应用名}/ 下的内容)。</summary>
    public static string RemoteDirOf(string gameName) =>
        $"{AppRoot}/{BaiduPanClient.SanitizeName(gameName)}";

    /// <summary>按游戏记录算云端目录:优先用钉死的云端目录名,留空才回落到游戏名。</summary>
    public static string RemoteDirOf(Game game) => RemoteDirOf(CloudDirOf(game));

    /// <summary>该游戏实际用的云端目录名(清理过,可直接和云端列回来的目录名比对)。</summary>
    public static string CloudDirOf(Game game) =>
        BaiduPanClient.SanitizeName(EffectiveCloudDir(game.CloudDir, game.Name));

    /// <summary>
    /// 实际使用的云端目录名。游戏改名不该挪动云端存档,所以名字一旦钉进 CloudDir 就以它为准;
    /// 只有从没钉过(老数据)才跟着游戏名走,这样加这一列之前的行为保持不变。
    /// </summary>
    public static string EffectiveCloudDir(string? cloudDir, string gameName) =>
        string.IsNullOrWhiteSpace(cloudDir) ? gameName : cloudDir;

    /// <summary>
    /// 待删路径必须落在沙箱内、且不是沙箱根目录本身。删除不可逆,这道闸不能省:
    /// 路径本来都来自云端列表,但多挡一层不亏,免得哪天上游传进来一个空串就把整个沙箱端了。
    /// </summary>
    public static bool IsInsideAppRoot(string remotePath) =>
        remotePath.Length > AppRoot.Length + 1
        && remotePath.StartsWith(AppRoot + "/", StringComparison.Ordinal);

    // ---------- 账号绑定 ----------

    /// <summary>获取设备码;调用方要把用户码展示给用户,然后调 WaitForAuthorizationAsync 等授权。</summary>
    public async Task<BaiduDeviceCode> BeginBindAsync(CancellationToken ct = default)
    {
        ApplyCredentials();
        return await _client.RequestDeviceCodeAsync(ct);
    }

    /// <summary>轮询等待用户完成授权;成功后保存令牌与账号昵称并落盘。返回账号昵称。</summary>
    public async Task<string> WaitForAuthorizationAsync(
        BaiduDeviceCode device, IProgress<int>? secondsLeft = null, CancellationToken ct = default)
    {
        var token = await _client.PollDeviceTokenAsync(device, secondsLeft, ct);
        StoreToken(token);

        var user = await _client.GetUserInfoAsync(ct);
        Settings.BaiduUserName = user.Name;
        Settings.BaiduUserUk = user.Uk;
        _settings.Save();
        return user.Name;
    }

    /// <summary>解绑并清空令牌。</summary>
    public void Unbind()
    {
        ClearTokens();
        _settings.Save();
    }

    /// <summary>确保 access_token 可用:快到期的提前刷新,并把轮换后的 refresh_token 覆盖落盘。</summary>
    public async Task EnsureTokenAsync(CancellationToken ct = default)
    {
        ApplyCredentials();
        if (!IsBound)
            throw new InvalidOperationException("尚未绑定百度网盘账号,请在左侧「云存档」页里绑定。");

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(Settings.BaiduTokenExpiresAtUnix).UtcDateTime;
        if (expiresAt > DateTime.UtcNow.AddDays(1))
            return;

        if (string.IsNullOrWhiteSpace(Settings.BaiduRefreshToken))
            throw new InvalidOperationException("授权已失效,请重新绑定账号。");

        try
        {
            StoreToken(await _client.RefreshTokenAsync(Settings.BaiduRefreshToken, ct));
            _settings.Save();
        }
        catch (Exception ex)
        {
            // 刷新一旦失败,旧的 refresh_token 立即作废,清掉免得后续反复失败
            ClearTokens();
            _settings.Save();
            throw new InvalidOperationException($"授权已失效,请重新绑定账号。({ex.Message})", ex);
        }
    }

    private void ApplyCredentials()
    {
        _client.AppKey = Pick(Settings.BaiduAppKey, BaiduPanClient.DefaultAppKey);
        _client.SecretKey = Pick(Settings.BaiduSecretKey, BaiduPanClient.DefaultSecretKey);
        _client.AccessToken = Settings.BaiduAccessToken;
    }

    /// <summary>设置页留空时用内置常量兜底。</summary>
    private static string? Pick(string? fromSettings, string fallback) =>
        string.IsNullOrWhiteSpace(fromSettings) ? fallback : fromSettings;

    private void StoreToken(BaiduToken token)
    {
        Settings.BaiduAccessToken = token.AccessToken;
        // refresh_token 只能用一次且每次刷新都会换新,必须覆盖保存,漏存一次就要重新授权
        if (!string.IsNullOrWhiteSpace(token.RefreshToken))
            Settings.BaiduRefreshToken = token.RefreshToken;
        Settings.BaiduTokenExpiresAtUnix =
            DateTimeOffset.UtcNow.AddSeconds(token.ExpiresInSeconds).ToUnixTimeSeconds();
        _client.AccessToken = token.AccessToken;
    }

    private void ClearTokens()
    {
        Settings.BaiduAccessToken = null;
        Settings.BaiduRefreshToken = null;
        Settings.BaiduTokenExpiresAtUnix = 0;
        Settings.BaiduUserName = null;
        Settings.BaiduUserUk = null;
        _client.AccessToken = null;
    }

    // ---------- 云端目录一览 ----------

    /// <summary>
    /// 列出云端已经存过存档的游戏目录。每个目录都得再递归列一层才知道里面有几个文件、
    /// 传完没有,所以是「1 + N」次请求;免费账号 QPS 低,目录多时会慢一点。
    /// 只剩收尾标记或压根空着的目录不算有存档,直接略过。
    /// </summary>
    public async Task<List<CloudGameDir>> ListCloudGamesAsync(CancellationToken ct = default)
    {
        await EnsureTokenAsync(ct);

        var result = new List<CloudGameDir>();
        foreach (var dir in await _client.ListAsync(AppRoot, ct))
        {
            if (!dir.IsDir)
                continue;

            var entries = await ListRecursiveAsync(dir.Path, ct);
            var files = entries.Where(e => !IsMeta(e.Path)).ToList();
            if (files.Count == 0)
                continue;

            long total = 0;
            DateTime? max = null;
            foreach (var file in files)
            {
                total += file.Size;
                if (file.LocalMtimeUtc is { } mtime && (max is null || mtime > max))
                    max = mtime;
            }

            result.Add(new CloudGameDir(
                dir.Name, dir.Path, files.Count, total, max, entries.Any(e => IsMeta(e.Path))));
        }
        return result;
    }

    // ---------- 比对 ----------

    /// <summary>比对本地与云端存档,判断谁更新。</summary>
    public async Task<SaveCompare> CompareAsync(Game game, CancellationToken ct = default)
    {
        var local = ComputeLocalStamp(game.SavePath);
        var empty = new SaveStamp(0, null);

        if (string.IsNullOrWhiteSpace(game.SavePath))
            return new SaveCompare(SaveSyncState.LocalMissing, local, empty);

        await EnsureTokenAsync(ct);

        var entries = await ListRecursiveAsync(RemoteDirOf(game), ct);
        return CompareEntries(local, entries);
    }

    /// <summary>
    /// 由云端条目列表推导结论(纯函数,不碰网络)。收尾标记 _meta.json 只是完整性凭证,
    /// 不计入文件数与时间戳;缺它就说明上次没传完,不能当成「一致」。
    /// </summary>
    public static SaveCompare CompareEntries(SaveStamp local, IReadOnlyList<CloudEntry> entries)
    {
        var files = entries.Where(e => !IsMeta(e.Path)).ToList();

        DateTime? remoteMax = null;
        foreach (var file in files)
        {
            if (file.LocalMtimeUtc is { } mtime && (remoteMax is null || mtime > remoteMax))
                remoteMax = mtime;
        }
        var remote = new SaveStamp(files.Count, remoteMax);

        if (files.Count == 0)
            return new SaveCompare(SaveSyncState.CloudMissing, local, remote);
        if (!entries.Any(e => IsMeta(e.Path)))
            return new SaveCompare(SaveSyncState.CloudIncomplete, local, remote);
        if (local.FileCount == 0)
            return new SaveCompare(SaveSyncState.LocalMissing, local, remote);

        return new SaveCompare(Classify(local.MaxMtimeUtc, remote.MaxMtimeUtc), local, remote);
    }

    /// <summary>按最新修改时间判定新旧;容差之内的差异视为一致。</summary>
    public static SaveSyncState Classify(DateTime? localMax, DateTime? remoteMax)
    {
        if (localMax is null && remoteMax is null)
            return SaveSyncState.Same;
        if (localMax is null)
            return SaveSyncState.RemoteNewer;
        if (remoteMax is null)
            return SaveSyncState.LocalNewer;

        var diff = (localMax.Value - remoteMax.Value).TotalSeconds;
        if (Math.Abs(diff) < ToleranceSeconds)
            return SaveSyncState.Same;
        return diff > 0 ? SaveSyncState.LocalNewer : SaveSyncState.RemoteNewer;
    }

    // ---------- 上传 / 下载 ----------

    /// <summary>
    /// 把本地存档目录整体上传到云端该游戏目录下(覆盖同名文件)。
    /// 先传所有存档文件、最后传 _meta.json 作收尾标记,所以半途断网留下的残档不会被当成完整存档。
    /// </summary>
    public async Task UploadAsync(Game game, IProgress<string>? status = null, CancellationToken ct = default)
    {
        var saveDir = RequireSaveDir(game);
        await EnsureTokenAsync(ct);

        var files = EnumerateLocalFiles(saveDir);
        if (files.Count == 0)
            throw new InvalidOperationException("存档目录里没有文件,已取消上传。");

        var remoteDir = RemoteDirOf(game);
        await _client.EnsureDirectoryAsync(remoteDir, ct);

        // 每个文件所在的云端子目录只需建一次
        var ensured = new HashSet<string>(StringComparer.Ordinal) { remoteDir };
        var done = 0;
        foreach (var file in files)
        {
            var remotePath = $"{remoteDir}/{file.RelativePath}";
            var subDir = remotePath[..remotePath.LastIndexOf('/')];
            if (ensured.Add(subDir))
                await _client.EnsureDirectoryAsync(subDir, ct);

            status?.Report($"上传中 {++done}/{files.Count}:{file.RelativePath}");
            await _client.UploadFileAsync(file.FullPath, remotePath, file.MtimeUtc, null, ct);
        }

        var stamp = ComputeLocalStamp(saveDir);
        var metaPath = WriteMetaFile(game.Id, stamp, files.Count);
        await _client.UploadFileAsync(
            metaPath, $"{remoteDir}/{MetaFileName}", stamp.MaxMtimeUtc ?? DateTime.UtcNow, null, ct);

        status?.Report($"已上传 {files.Count} 个文件");
    }

    /// <summary>把云端该游戏的存档下载到本地存档目录(覆盖同名文件),并回写原始时间戳。</summary>
    public async Task DownloadAsync(Game game, IProgress<string>? status = null, CancellationToken ct = default)
    {
        var saveDir = RequireSavePath(game);
        await EnsureTokenAsync(ct);

        var remoteDir = RemoteDirOf(game);
        var entries = await ListRecursiveAsync(remoteDir, ct);
        var files = entries.Where(e => !IsMeta(e.Path)).ToList();
        if (files.Count == 0)
            throw new InvalidOperationException("云端没有这个游戏的存档,无法下载。");

        Directory.CreateDirectory(saveDir);

        var done = 0;
        foreach (var file in files)
        {
            var localPath = ResolveLocalPath(saveDir, remoteDir, file.Path);
            status?.Report($"下载中 {++done}/{files.Count}:{file.Name}");
            await _client.DownloadFileAsync(file.FsId, localPath, null, ct);

            // 必须回写时间戳,否则下完两边时间不等,会一直提示「不一致」
            if (file.LocalMtimeUtc is { } mtime)
                File.SetLastWriteTimeUtc(localPath, mtime);
        }

        status?.Report($"已下载 {files.Count} 个文件");
    }

    /// <summary>
    /// 删掉一个游戏在云端的整个存档目录(含 _meta.json 与所有子目录)。只动云端,本地存档原样保留。
    /// 百度删目录不保证递归,所以自己来:先逐个删光文件,再自底向上删目录。
    /// 删除是异步入队的,接口返回不代表网盘上已经没了,调用方要提示用户稍后刷新确认。
    /// </summary>
    public async Task<CloudDeleteResult> DeleteCloudSaveAsync(
        string remoteDir, IProgress<string>? status = null, CancellationToken ct = default)
    {
        if (!IsInsideAppRoot(remoteDir))
            throw new InvalidOperationException($"拒绝删除云端沙箱外的路径:{remoteDir}");

        await EnsureTokenAsync(ct);

        var entries = await ListRecursiveAsync(remoteDir, ct, includeDirs: true);
        var files = entries.Where(e => !e.IsDir).ToList();

        var done = 0;
        foreach (var file in files)
        {
            status?.Report($"删除中 {++done}/{files.Count}:{file.Name}");
            await _client.DeleteAsync(file.Path, ct);
        }

        // 自底向上删目录(路径长的更深),空目录才删得掉。删不掉不算失败:百度删文件同样是排队的,
        // 目录此刻可能还「非空」;残留的空目录不会出现在列表里,用户顶多在网盘上看到个空壳。
        var leftover = 0;
        foreach (var dir in entries.Where(e => e.IsDir).OrderByDescending(e => e.Path.Length))
        {
            if (!await TryDeleteAsync(dir.Path, ct))
                leftover++;
        }
        if (!await TryDeleteAsync(remoteDir, ct))
            leftover++;

        status?.Report($"已提交删除 {files.Count} 个文件");
        return new CloudDeleteResult(files.Count, leftover);
    }

    /// <summary>删目录用:删不掉只当残留在,不影响整体结果。</summary>
    private async Task<bool> TryDeleteAsync(string remotePath, CancellationToken ct)
    {
        try
        {
            await _client.DeleteAsync(remotePath, ct);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
        {
            return false;
        }
    }

    // ---------- 纯函数(无网络,可无头验证) ----------

    /// <summary>递归算出本地存档目录的文件数与最新修改时间;目录不存在或为空时 FileCount=0。</summary>
    public static SaveStamp ComputeLocalStamp(string? saveDir)
    {
        var files = EnumerateLocalFiles(saveDir);
        if (files.Count == 0)
            return new SaveStamp(0, null);

        var max = files[0].MtimeUtc;
        foreach (var file in files)
        {
            if (file.MtimeUtc > max)
                max = file.MtimeUtc;
        }
        return new SaveStamp(files.Count, max);
    }

    /// <summary>递归列出本地存档文件。个别子目录读不动就跳过,不让整个同步失败。</summary>
    public static List<LocalFile> EnumerateLocalFiles(string? saveDir)
    {
        var files = new List<LocalFile>();
        if (string.IsNullOrWhiteSpace(saveDir) || !Directory.Exists(saveDir))
            return files;

        var pending = new Stack<string>();
        pending.Push(saveDir);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    var relative = Path.GetRelativePath(saveDir, file).Replace('\\', '/');
                    files.Add(new LocalFile(relative, file, File.GetLastWriteTimeUtc(file)));
                }
                foreach (var sub in Directory.EnumerateDirectories(dir))
                    pending.Push(sub);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // 跳过读不了的子目录
            }
        }
        return files;
    }

    /// <summary>
    /// 把云端条目映射到本地文件路径。云端返回的路径不可信,逐段校验,
    /// 免得一个畸形文件名把文件写到存档目录外面去。
    /// </summary>
    public static string ResolveLocalPath(string saveDir, string remoteDir, string remotePath)
    {
        var prefix = remoteDir.TrimEnd('/') + "/";
        if (!remotePath.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException($"云端返回了不在该游戏目录下的文件:{remotePath}");

        var segments = remotePath[prefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new InvalidDataException($"云端返回了无效的文件路径:{remotePath}");

        foreach (var segment in segments)
        {
            if (segment is "." or ".." || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException($"云端文件名不安全,已拒绝下载:{remotePath}");
        }

        var root = Path.GetFullPath(saveDir);
        var combined = Path.GetFullPath(Path.Combine([root, .. segments]));
        if (!combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"云端文件路径超出存档目录,已拒绝下载:{remotePath}");

        return combined;
    }

    /// <summary>
    /// 是否是收尾标记。除 _meta.json 外也认被云端改名过的变体(如 _meta(1).json):
    /// 这种文件绝不能当成存档数据下载进用户的存档目录。
    /// </summary>
    public static bool IsMeta(string remotePath)
    {
        var name = Path.GetFileName(remotePath);
        return name.StartsWith("_meta", StringComparison.OrdinalIgnoreCase)
               && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>按关键字筛选云端目录名,不区分大小写;关键字为空则全给。</summary>
    public static List<CloudGameDir> FilterCloudDirs(IEnumerable<CloudGameDir> dirs, string? query)
    {
        var keyword = query?.Trim() ?? string.Empty;
        return dirs
            .Where(d => keyword.Length == 0
                        || d.DirName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>云端目录的一行说明(文件数 / 大小 / 最新时间 / 传完没有),列表与选择器共用。</summary>
    public static string DescribeCloudDir(CloudGameDir dir)
    {
        var text = $"{dir.FileCount} 个文件 · {FormatSize(dir.TotalBytes)}";
        if (dir.MaxMtimeUtc is { } mtime)
            text += $" · 最新 {mtime.ToLocalTime():yyyy-MM-dd HH:mm}";
        return dir.IsComplete ? text : text + " · 上次没传完";
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):0.0} MB",
        >= 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes} B",
    };

    // ---------- 内部 ----------

    /// <summary>
    /// 递归列出目录下的条目。includeDirs=false 只要文件(比对与下载要的是文件);
    /// 删除时得连目录一起拿到,才能按路径深度自底向上清理。目录本身不含在结果里。
    /// </summary>
    private async Task<List<CloudEntry>> ListRecursiveAsync(
        string remoteDir, CancellationToken ct, bool includeDirs = false)
    {
        var result = new List<CloudEntry>();
        var pending = new Queue<string>();
        pending.Enqueue(remoteDir);

        while (pending.Count > 0)
        {
            var dir = pending.Dequeue();
            foreach (var entry in await _client.ListAsync(dir, ct))
            {
                if (entry.IsDir)
                {
                    if (includeDirs)
                        result.Add(entry);
                    pending.Enqueue(entry.Path);
                }
                else
                {
                    result.Add(entry);
                }
            }
        }
        return result;
    }

    private string WriteMetaFile(int gameId, SaveStamp stamp, int uploadedFiles)
    {
        Directory.CreateDirectory(_stagingDir);
        var path = Path.Combine(_stagingDir, $"{gameId}.meta.json");
        var json = JsonSerializer.Serialize(new
        {
            gameId,
            fileCount = uploadedFiles,
            maxMtimeUtc = stamp.MaxMtimeUtc?.ToString("O"),
            syncedAtUtc = DateTime.UtcNow.ToString("O"),
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>
    /// 只要求配了存档目录,不要求目录真的存在。下载得自己建目录:
    /// 从云端恢复到一台还没玩过这个游戏的新机器时,存档目录本来就不存在。
    /// </summary>
    private static string RequireSavePath(Game game)
    {
        if (string.IsNullOrWhiteSpace(game.SavePath))
            throw new InvalidOperationException($"「{game.Name}」还没有设置存档目录,请先在编辑游戏里指定。");
        return game.SavePath;
    }

    /// <summary>上传要求目录确实存在,否则没什么可传的。</summary>
    private static string RequireSaveDir(Game game)
    {
        var dir = RequireSavePath(game);
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"存档目录不存在:{dir}");
        return dir;
    }
}
