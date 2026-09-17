using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalManagement.Models;
using GalManagement.Services;

namespace GalManagement.ViewModels;

/// <summary>同一个弹窗服务三种时机。</summary>
public enum SaveSyncScenario
{
    /// <summary>列表上手动点的「存档」。</summary>
    Manual,

    /// <summary>启动游戏之前。</summary>
    BeforeLaunch,

    /// <summary>游戏退出之后。</summary>
    AfterPlay,
}

/// <summary>
/// 云存档同步弹窗。上传 / 下载都在本弹窗里完成,做完才关窗,
/// 这样「启动前同步」只要挡住启动流程即可,调用方不用再操心顺序。
/// </summary>
public partial class SaveSyncViewModel : ObservableObject
{
    private readonly CloudSaveService _cloud;
    private readonly Game _game;

    public string Title { get; }

    public string GameName { get; }

    public string LocalText { get; }

    public string RemoteText { get; }

    public string StateText { get; }

    public bool ShowUpload { get; }

    public bool ShowDownload { get; }

    public bool ShowSkip { get; }

    public bool ShowClose { get; }

    public string UploadLabel { get; }

    public string DownloadLabel { get; }

    public string SkipLabel { get; }

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _isBusy;

    public IAsyncRelayCommand UploadCommand { get; }

    public IAsyncRelayCommand DownloadCommand { get; }

    public RelayCommand SkipCommand { get; }

    public RelayCommand CloseCommand { get; }

    public event Action? RequestClose;

    public SaveSyncViewModel(CloudSaveService cloud, Game game, SaveSyncScenario scenario, SaveCompare compare)
    {
        _cloud = cloud;
        _game = game;

        Title = scenario switch
        {
            SaveSyncScenario.BeforeLaunch => "启动前同步存档",
            SaveSyncScenario.AfterPlay => "上传游玩后的存档",
            _ => "云存档",
        };

        GameName = game.Name;
        LocalText = Describe(compare.Local);
        RemoteText = Describe(compare.Remote);
        StateText = DescribeState(compare.State);

        var localNewer = compare.State == SaveSyncState.LocalNewer;
        // 本地空目录 + 云端有档,也算「可以拉下来」
        var canPull = compare.State is SaveSyncState.RemoteNewer or SaveSyncState.LocalMissing;

        // 启动前只推「较新的一方」,避免用户在两个危险按钮之间瞎点
        ShowUpload = scenario switch
        {
            SaveSyncScenario.BeforeLaunch => localNewer,
            SaveSyncScenario.AfterPlay => true,
            _ => true,
        };
        ShowDownload = scenario switch
        {
            SaveSyncScenario.BeforeLaunch => canPull,
            SaveSyncScenario.AfterPlay => false,
            _ => true,
        };
        ShowSkip = scenario != SaveSyncScenario.Manual;
        ShowClose = scenario == SaveSyncScenario.Manual;

        UploadLabel = scenario == SaveSyncScenario.BeforeLaunch ? "上传后启动" : "上传到云端";
        DownloadLabel = scenario == SaveSyncScenario.BeforeLaunch ? "下载并启动" : "从云端覆盖本地";
        SkipLabel = scenario == SaveSyncScenario.AfterPlay ? "暂不上传" : "先不同步,直接启动";

        UploadCommand = new AsyncRelayCommand(UploadAsync, () => !IsBusy);
        DownloadCommand = new AsyncRelayCommand(DownloadAsync, () => !IsBusy);
        SkipCommand = new RelayCommand(() => RequestClose?.Invoke());
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
    }

    private static string Describe(SaveStamp stamp) =>
        stamp.MaxMtimeUtc is { } mtime
            ? $"{stamp.FileCount} 个文件,最新 {mtime.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : "无存档";

    private static string DescribeState(SaveSyncState state) => state switch
    {
        SaveSyncState.LocalMissing => "本地还没有这个游戏的存档。",
        SaveSyncState.CloudMissing => "云端还没有这个游戏的存档。",
        SaveSyncState.CloudIncomplete => "云端存档不完整(上次没传完),建议重新上传覆盖。",
        SaveSyncState.LocalNewer => "本地存档较新。",
        SaveSyncState.RemoteNewer => "云端存档较新。",
        _ => "两侧存档一致。",
    };

    private async Task UploadAsync()
    {
        IsBusy = true;
        Status = "正在上传…";
        try
        {
            await _cloud.UploadAsync(_game, new Progress<string>(s => Status = s));
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            Status = $"上传失败:{ex.Message}";
            IsBusy = false;
        }
    }

    private async Task DownloadAsync()
    {
        var ask = System.Windows.MessageBox.Show(
            $"「{_game.Name}」的本地存档将被云端存档覆盖,本地原有的同名文件会被替换。\n\n确定继续?",
            "覆盖本地存档", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
        if (ask != System.Windows.MessageBoxResult.Yes)
            return;

        IsBusy = true;
        Status = "正在下载…";
        try
        {
            await _cloud.DownloadAsync(_game, new Progress<string>(s => Status = s));
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            Status = $"下载失败:{ex.Message}";
            IsBusy = false;
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        UploadCommand.NotifyCanExecuteChanged();
        DownloadCommand.NotifyCanExecuteChanged();
    }
}
