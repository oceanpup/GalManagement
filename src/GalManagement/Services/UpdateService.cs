using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GalManagement.Services;

public enum UpdateCheckOutcome { UpToDate, Available, Error }

public enum UpdateErrorKind { Network, RateLimited, NoRelease, NoAsset, BadVersion, Http }

/// <summary>GitHub 上找到的一个可用版本。</summary>
public sealed record UpdateInfo(
    Version Version,
    string TagName,
    string Name,
    string Body,
    string DownloadUrl,
    long AssetSize,
    string? Sha256Hex);

/// <summary>一次检查更新的结果。</summary>
public sealed record UpdateCheckResult(
    UpdateCheckOutcome Outcome,
    UpdateInfo? Update,
    UpdateErrorKind ErrorKind);

/// <summary>
/// 从 GitHub Releases 检查更新、下载并自替换正在运行的单文件 exe。
/// 网络走 .NET 默认系统代理(HttpClient.DefaultProxy,自动适配本机代理),请勿禁用。
/// </summary>
public sealed class UpdateService
{
    private readonly string _updateDir;
    private readonly string _assetName;
    private readonly string _apiLatestUrl;
    private readonly HttpClient _http;

    /// <summary>当前运行版本的 3 段版本号(例如 1.0.0)。</summary>
    public Version CurrentVersion { get; }

    /// <summary>下载得到的待安装 exe 文件路径(data\updates\GalManagement.exe)。</summary>
    public string NewUpdateFilePath => Path.Combine(_updateDir, _assetName);

    public UpdateService(
        string dataDirectory,
        string repoOwner = "oceanpup",
        string repoName = "GalManagement",
        string assetName = "GalManagement.exe")
    {
        _assetName = assetName;
        _updateDir = Path.Combine(dataDirectory, "updates");
        _apiLatestUrl = $"https://api.github.com/repos/{repoOwner}/{repoName}/releases/latest";

        var assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
        CurrentVersion = NormalizeTo3Parts(assemblyVersion);

        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"GalManagement/{CurrentVersion}");
    }

    /// <summary>查询 GitHub 最新发布并与当前版本比较;不抛异常,结果分类返回。</summary>
    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            using var resp = await _http.GetAsync(_apiLatestUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (resp.StatusCode == HttpStatusCode.Forbidden || resp.StatusCode == HttpStatusCode.TooManyRequests)
                return new(UpdateCheckOutcome.Error, null, UpdateErrorKind.RateLimited);
            if (!resp.IsSuccessStatusCode)
                return new(UpdateCheckOutcome.Error, null, UpdateErrorKind.Http);

            var dto = await JsonSerializer.DeserializeAsync<ReleaseDto>(
                await resp.Content.ReadAsStreamAsync(cts.Token),
                cancellationToken: cts.Token);

            if (dto is null || string.IsNullOrWhiteSpace(dto.TagName))
                return new(UpdateCheckOutcome.Error, null, UpdateErrorKind.NoRelease);
            if (!TryParseRemoteVersion(dto.TagName, out var remote))
                return new(UpdateCheckOutcome.Error, null, UpdateErrorKind.BadVersion);

            var asset = dto.Assets?.FirstOrDefault(a =>
                !string.IsNullOrEmpty(a.Name) && a.Name.Equals(_assetName, StringComparison.OrdinalIgnoreCase));
            if (asset is null || string.IsNullOrWhiteSpace(asset.DownloadUrl))
                return new(UpdateCheckOutcome.Error, null, UpdateErrorKind.NoAsset);

            var info = new UpdateInfo(
                remote,
                dto.TagName,
                dto.Name ?? string.Empty,
                dto.Body ?? string.Empty,
                asset.DownloadUrl,
                asset.Size,
                StripShaPrefix(asset.Digest));

            return remote > CurrentVersion
                ? new(UpdateCheckOutcome.Available, info, default)
                : new(UpdateCheckOutcome.UpToDate, info, default);
        }
        catch (OperationCanceledException)
        {
            return new(UpdateCheckOutcome.Error, null, UpdateErrorKind.Network);
        }
        catch (HttpRequestException)
        {
            return new(UpdateCheckOutcome.Error, null, UpdateErrorKind.Network);
        }
        catch (JsonException)
        {
            return new(UpdateCheckOutcome.Error, null, UpdateErrorKind.Http);
        }
    }

    /// <summary>流式下载安装包到 data\updates,完成后做 SHA256 校验;失败会清理 .part。</summary>
    public async Task DownloadAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMinutes(15));
        var token = cts.Token;

        var finalPath = NewUpdateFilePath;
        var partPath = finalPath + ".part";
        Directory.CreateDirectory(_updateDir);

        try
        {
            using var resp = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;

            await using var src = await resp.Content.ReadAsStreamAsync(token);
            await using var dst = File.Create(partPath);
            var buffer = new byte[64 * 1024];
            long copied = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, token)) > 0)
            {
                await dst.WriteAsync(buffer, 0, read, token);
                copied += read;
                if (total is > 0)
                    progress?.Report(copied * 100.0 / total.Value);
            }
            await dst.FlushAsync(token);
            dst.Close();

            if (File.Exists(finalPath))
                File.Delete(finalPath);
            File.Move(partPath, finalPath);

            if (!string.IsNullOrWhiteSpace(update.Sha256Hex))
            {
                var actual = ComputeSha256Hex(finalPath);
                if (!actual.Equals(update.Sha256Hex, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(finalPath);
                    throw new InvalidDataException("下载文件校验失败,请重试。");
                }
            }
        }
        catch
        {
            try { if (File.Exists(partPath)) File.Delete(partPath); } catch { /* 尽力清理 */ }
            throw;
        }
    }

    /// <summary>
    /// 启动隐藏的 update.cmd:等待当前进程退出后,把 data\updates 的新 exe 覆盖到当前 exe 位置并重启。
    /// 调用方在成功后应立即退出应用。
    /// </summary>
    public bool TryLaunchUpdater(out string? error)
    {
        error = null;
        var target = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(target))
        {
            error = "无法确定当前程序文件位置。";
            return false;
        }

        var newExe = NewUpdateFilePath;
        if (!File.Exists(newExe))
        {
            error = "未找到已下载的更新文件,请重新下载。";
            return false;
        }

        var scriptPath = Path.Combine(_updateDir, "update.cmd");
        try
        {
            Directory.CreateDirectory(_updateDir);
            File.WriteAllText(scriptPath, BuildUpdaterScript(), new ASCIIEncoding());
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        var arguments = $"/d /s /c \"\"{scriptPath}\" \"{target}\" \"{newExe}\" \"{Environment.ProcessId}\"\"";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>应用启动时清理上次更新/中断留下的残留文件(.old、.part、临时新 exe、旧脚本)。</summary>
    public void InitCleanup()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                var old = exe + ".old";
                if (File.Exists(old))
                    File.Delete(old);
            }
        }
        catch { /* best effort */ }

        try
        {
            if (!Directory.Exists(_updateDir))
                return;
            foreach (var f in Directory.EnumerateFiles(_updateDir, "*.part"))
                File.Delete(f);
            var script = Path.Combine(_updateDir, "update.cmd");
            if (File.Exists(script))
                File.Delete(script);
            var newExe = NewUpdateFilePath;
            if (File.Exists(newExe))
                File.Delete(newExe);
        }
        catch { /* best effort */ }
    }

    private static Version NormalizeTo3Parts(Version v) =>
        new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    private static bool TryParseRemoteVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;
        var s = tag.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
            s = s[1..];

        var parts = s.Split('.');
        if (parts.Length is < 2 or > 4)
            return false;
        if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return false;
        var build = 0;
        if (parts.Length >= 3 && !int.TryParse(parts[2], out build))
            return false;
        if (major < 0 || minor < 0 || build < 0)
            return false;

        version = new Version(major, minor, build);
        return true;
    }

    /// <summary>GitHub 返回的 digest 形如 "sha256:&lt;hex&gt;",剥掉前缀只留 hex。</summary>
    private static string? StripShaPrefix(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
            return null;
        var s = digest.Trim();
        var i = s.IndexOf(':');
        return i >= 0 ? s[(i + 1)..] : s;
    }

    private static string ComputeSha256Hex(string path)
    {
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    // 脚本须为纯 ASCII(中文会因 GBK 代码页乱码)。采用 ping 计时(timeout 在无控制台时可能失败)。
    private static string BuildUpdaterScript() => """
        @echo off
        setlocal EnableExtensions

        set "TARGET=%~1"
        set "NEWEXE=%~2"
        set "OLDPID=%~3"

        if not defined TARGET exit /b 10
        if not defined NEWEXE exit /b 10
        if not exist "%NEWEXE%" exit /b 11

        set /a N=0
        :WAIT
        set /a N+=1
        if %N% gtr 40 goto FORCE
        tasklist /FI "PID eq %OLDPID%" 2>nul | findstr /C:"%OLDPID%" >nul
        if not errorlevel 1 (
          ping -n 2 127.0.0.1 >nul
          goto WAIT
        )
        goto SWAP

        :FORCE
        taskkill /PID %OLDPID% /F >nul 2>nul
        ping -n 3 127.0.0.1 >nul

        :SWAP
        ping -n 3 127.0.0.1 >nul
        move /y "%TARGET%" "%TARGET%.old" >nul 2>nul
        if errorlevel 1 exit /b 12
        copy /y "%NEWEXE%" "%TARGET%" >nul 2>nul
        if errorlevel 1 (
          move /y "%TARGET%.old" "%TARGET%" >nul 2>nul
          exit /b 13
        )
        del /q "%NEWEXE%" >nul 2>nul
        start "" /D "%~dp1" "%TARGET%"
        exit /b 0
        """;

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("assets")] public List<AssetDto>? Assets { get; set; }
    }

    private sealed class AssetDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? DownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }
}
