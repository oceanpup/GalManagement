using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalManagement.Data;
using GalManagement.Models;
using GalManagement.Services;

namespace GalManagement.ViewModels;

/// <summary>同步档位的下拉项:枚举配一句中文。</summary>
public sealed record SyncModeOption(SaveSyncMode Mode, string Text)
{
    // 自绘的 GlassComboBox 认不出 DisplayMemberPath,要落回 ToString 才显示中文;
    // 库页那几个下拉项(StatusFilterOption 等)也是这么干的。
    public override string ToString() => Text;
}

/// <summary>云端列表的一行:一个云端游戏目录,配上它在本地对应的游戏(找不到就是孤儿目录)。</summary>
public sealed record CloudGameRow(
    string DirName,
    string DirPath,
    string GameName,
    string SizeText,
    string MtimeText,
    string? Note,
    bool IsOrphan);

/// <summary>
/// 云存档页:账号与档位设置 + 「云端都存了哪些游戏」一览。原来这几项挤在设置页里,
/// 单独成页后列表才有地方摆。
/// </summary>
public partial class CloudSaveViewModel : ObservableObject
{
    private readonly GameRepository _repo;
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly CloudSaveService _cloudSave;

    [ObservableProperty]
    private bool _isBound;

    [ObservableProperty]
    private string _boundAccountText = string.Empty;

    [ObservableProperty]
    private string? _baiduAppKey;

    [ObservableProperty]
    private string? _baiduSecretKey;

    [ObservableProperty]
    private SaveSyncMode _beforeLaunchMode;

    [ObservableProperty]
    private SaveSyncMode _afterPlayMode;

    /// <summary>账号区块下的状态提示。</summary>
    [ObservableProperty]
    private string? _accountStatus;

    /// <summary>云端列表区块下的状态提示。</summary>
    [ObservableProperty]
    private string? _listStatus;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<CloudGameRow> CloudGames { get; } = new();

    public IReadOnlyList<SyncModeOption> SyncModeOptions { get; } =
    [
        new(SaveSyncMode.Ask, "每次询问"),
        new(SaveSyncMode.Auto, "自动同步"),
        new(SaveSyncMode.Off, "不同步"),
    ];

    public RelayCommand BindAccountCommand { get; }
    public RelayCommand UnbindAccountCommand { get; }
    public RelayCommand SaveCredentialsCommand { get; }
    public RelayCommand ShowHelpCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<CloudGameRow> DeleteCloudCommand { get; }

    /// <summary>需要弹出绑定窗口;由 MainWindow 接住并开窗(与编辑游戏弹窗同一套路)。</summary>
    public event Action? BindAccountRequested;

    /// <summary>需要弹出使用说明;同样交给 MainWindow 开窗。</summary>
    public event Action? HelpRequested;

    public CloudSaveViewModel(
        GameRepository repo, SettingsService settingsService, CloudSaveService cloudSave)
    {
        _repo = repo;
        _settings = settingsService.Current;
        _settingsService = settingsService;
        _cloudSave = cloudSave;

        _baiduAppKey = _settings.BaiduAppKey;
        _baiduSecretKey = _settings.BaiduSecretKey;
        _beforeLaunchMode = CloudSaveService.ParseMode(_settings.SyncModeBeforeLaunch);
        _afterPlayMode = CloudSaveService.ParseMode(_settings.SyncModeAfterPlay);
        RefreshCloudAccount();

        BindAccountCommand = new RelayCommand(() => BindAccountRequested?.Invoke(),
            () => _cloudSave.HasCredentials);
        UnbindAccountCommand = new RelayCommand(UnbindAccount);
        SaveCredentialsCommand = new RelayCommand(SaveCredentials);
        ShowHelpCommand = new RelayCommand(() => HelpRequested?.Invoke());
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsLoading);
        DeleteCloudCommand = new AsyncRelayCommand<CloudGameRow>(DeleteCloudAsync);
    }

    /// <summary>切到本页时调用:账号状态每次都刷新,云端列表每次都重拉(刚上传完切过来要能看见)。</summary>
    public void RefreshOnEnter()
    {
        RefreshCloudAccount();
        if (!RefreshCommand.CanExecute(null))
            return;      // 上一次还在拉,等它拉完
        RefreshCommand.Execute(null);
    }

    /// <summary>重新读取账号绑定状态(绑定弹窗关掉后调用)。</summary>
    public void RefreshCloudAccount()
    {
        IsBound = _cloudSave.IsBound;
        BoundAccountText = IsBound
            ? $"已绑定:{_cloudSave.BoundAccountName}"
            : "尚未绑定百度网盘账号。";
    }

    private async Task LoadAsync()
    {
        if (!IsBound)
        {
            CloudGames.Clear();
            ListStatus = "绑定账号后这里会列出云端已经存过存档的游戏。";
            return;
        }

        IsLoading = true;
        ListStatus = "正在读取云端存档列表…";
        try
        {
            var dirs = await _cloudSave.ListCloudGamesAsync();

            // 本地游戏按「实际用的云端目录名」建索引;CloudDir 是钉死的,所以游戏改过名也认得出来
            var localByDir = new Dictionary<string, Game>(StringComparer.Ordinal);
            foreach (var game in _repo.GetAll())
                localByDir[CloudSaveService.CloudDirOf(game)] = game;

            CloudGames.Clear();
            foreach (var dir in dirs.OrderBy(d => d.DirName, StringComparer.Ordinal))
            {
                localByDir.TryGetValue(dir.DirName, out var local);
                CloudGames.Add(new CloudGameRow(
                    dir.DirName,
                    dir.Path,
                    local?.Name ?? "—",
                    $"{dir.FileCount} 个文件 · {CloudSaveService.FormatSize(dir.TotalBytes)}",
                    dir.MaxMtimeUtc is { } mtime ? mtime.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "未知",
                    local is null ? "本地没有对应游戏" : dir.IsComplete ? null : "上次没传完",
                    local is null));
            }

            ListStatus = CloudGames.Count == 0
                ? "云端还没有任何游戏的存档。"
                : $"共 {CloudGames.Count} 个游戏在云端有存档。";
        }
        catch (Exception ex)
        {
            ListStatus = $"读取云端列表失败:{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnIsLoadingChanged(bool value) => RefreshCommand.NotifyCanExecuteChanged();

    partial void OnBaiduAppKeyChanged(string? value)
    {
        if (_settings.BaiduAppKey == value)
            return;
        _settings.BaiduAppKey = value;
        _settingsService.Save();
        BindAccountCommand.NotifyCanExecuteChanged();
    }

    partial void OnBaiduSecretKeyChanged(string? value)
    {
        if (_settings.BaiduSecretKey == value)
            return;
        _settings.BaiduSecretKey = value;
        _settingsService.Save();
        BindAccountCommand.NotifyCanExecuteChanged();
    }

    partial void OnBeforeLaunchModeChanged(SaveSyncMode value)
    {
        if (_settings.SyncModeBeforeLaunch == value.ToString())
            return;
        _settings.SyncModeBeforeLaunch = value.ToString();
        _settingsService.Save();
    }

    partial void OnAfterPlayModeChanged(SaveSyncMode value)
    {
        if (_settings.SyncModeAfterPlay == value.ToString())
            return;
        _settings.SyncModeAfterPlay = value.ToString();
        _settingsService.Save();
    }

    private void SaveCredentials()
    {
        _settings.BaiduAppKey = BaiduAppKey;
        _settings.BaiduSecretKey = BaiduSecretKey;
        _settingsService.Save();
        AccountStatus = _cloudSave.HasCredentials
            ? "凭证已保存。"
            : "已保存,但 AppKey 仍为空:需要先在开放平台创建应用。";
    }

    private void UnbindAccount()
    {
        if (!IsBound)
            return;

        var ask = MessageBox.Show(
            "解绑后需要重新授权才能继续使用云存档,云端已上传的存档不会被删除。\n\n确定解绑?",
            "解绑百度网盘账号", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes)
            return;

        _cloudSave.Unbind();
        RefreshCloudAccount();
        AccountStatus = "已解绑百度网盘账号。";

        // 解绑后列表没意义了,顺手清掉
        CloudGames.Clear();
        ListStatus = "绑定账号后这里会列出云端已经存过存档的游戏。";
    }

    /// <summary>
    /// 删掉这个云端目录(含子目录),只删云端、不碰本地存档。
    /// 百度删除是异步入队的,提交成功后网盘上可能还看得见,所以提示里让用户稍后自己点刷新确认,
    /// 不在这里假装那一行已经没了。
    /// </summary>
    private async Task DeleteCloudAsync(CloudGameRow? row)
    {
        if (row is null)
            return;

        var ask = MessageBox.Show(
            "将从百度网盘删除这个游戏的整个云端存档目录,删除后无法恢复。\n\n" +
            $"游戏:{row.GameName}\n" +
            $"云端目录:{row.DirPath}\n" +
            $"内容:{row.SizeText}\n\n" +
            "本地存档不会被删除。确定删除?",
            "删除云端存档", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (ask != MessageBoxResult.Yes)
            return;

        ListStatus = $"正在删除云端存档「{row.DirName}」…";
        try
        {
            var status = new Progress<string>(text => ListStatus = text);
            var result = await _cloudSave.DeleteCloudSaveAsync(row.DirPath, status);
            var detail = result.LeftoverDirs == 0
                ? $"已删除「{row.DirName}」的云端存档({result.FileCount} 个文件)。"
                : $"已删除 {result.FileCount} 个文件,另有 {result.LeftoverDirs} 个空目录没能删掉。";
            ListStatus = detail + "百度网盘是异步删除,这一行可能还看得见,稍等片刻点「刷新」确认。";
        }
        catch (Exception ex)
        {
            ListStatus = $"删除云端存档失败:{ex.Message}";
            MessageBox.Show(ListStatus, "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
