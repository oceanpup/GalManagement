using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalManagement.Models;
using GalManagement.Services;

namespace GalManagement.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly IconService _iconService;
    private readonly UpdateService _updateService;

    [ObservableProperty]
    private string _appTitle;

    [ObservableProperty]
    private string? _iconPreviewPath;

    [ObservableProperty]
    private string _currentVersionText = string.Empty;

    [ObservableProperty]
    private string? _updateStatus;

    [ObservableProperty]
    private bool _isUpdateBusy;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private double _updateProgress;

    public RelayCommand PickIconCommand { get; }
    public RelayCommand ResetIconCommand { get; }
    public IAsyncRelayCommand CheckUpdateCommand { get; }

    public SettingsViewModel(
        AppSettings settings,
        SettingsService settingsService,
        IconService iconService,
        UpdateService updateService)
    {
        _settings = settings;
        _settingsService = settingsService;
        _iconService = iconService;
        _updateService = updateService;
        _appTitle = settings.AppTitle;
        _iconPreviewPath = iconService.GetFullPath(settings.IconFileName);
        _currentVersionText = $"当前版本 v{_updateService.CurrentVersion}";

        PickIconCommand = new RelayCommand(PickIcon);
        ResetIconCommand = new RelayCommand(ResetIcon, () => settings.IconFileName is not null);
        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync, () => !IsUpdateBusy);
    }

    partial void OnAppTitleChanged(string value)
    {
        if (_settings.AppTitle == value)
            return;
        _settings.AppTitle = value;
        _settingsService.Save();
    }

    private void PickIcon()
    {
        var name = _iconService.PickAndCopy();
        if (name is null)
            return;

        var old = _settings.IconFileName;
        _settings.IconFileName = name;
        _settingsService.Save();
        _iconService.Delete(old);
        IconPreviewPath = _iconService.GetFullPath(name);
        ResetIconCommand.NotifyCanExecuteChanged();
    }

    private void ResetIcon()
    {
        var old = _settings.IconFileName;
        if (old is null)
            return;

        _settings.IconFileName = null;
        _settingsService.Save();
        _iconService.Delete(old);
        IconPreviewPath = null;
        ResetIconCommand.NotifyCanExecuteChanged();
    }

    private async Task CheckUpdateAsync()
    {
        IsUpdateBusy = true;
        UpdateProgress = 0;
        UpdateStatus = "正在检查更新…";
        try
        {
            var result = await _updateService.CheckForUpdateAsync();
            switch (result.Outcome)
            {
                case UpdateCheckOutcome.UpToDate:
                    UpdateStatus = $"已是最新版本 v{_updateService.CurrentVersion}。";
                    MessageBox.Show(UpdateStatus, "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;

                case UpdateCheckOutcome.Available:
                    await PromptAndInstallAsync(result.Update!);
                    break;

                case UpdateCheckOutcome.Error:
                    UpdateStatus = FriendlyError(result.ErrorKind);
                    MessageBox.Show(UpdateStatus, "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = $"检查更新出错:{ex.Message}";
            MessageBox.Show(UpdateStatus, "检查更新", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsDownloading = false;
            IsUpdateBusy = false;
        }
    }

    private async Task PromptAndInstallAsync(UpdateInfo info)
    {
        var sizeMb = info.AssetSize / (1024.0 * 1024.0);
        var ask = MessageBox.Show(
            $"发现新版本 {info.TagName}(当前 v{_updateService.CurrentVersion})\n" +
            $"安装包大小:约 {sizeMb:0.0} MB\n\n是否下载并安装?",
            "发现新版本", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes)
        {
            UpdateStatus = "已取消更新。";
            return;
        }

        IsDownloading = true;
        UpdateStatus = "正在下载更新… 0%";
        var lastPercent = -1d;
        try
        {
            await _updateService.DownloadAsync(info,
                new Progress<double>(p =>
                {
                    UpdateProgress = p;
                    var whole = Math.Floor(p);
                    if (whole != lastPercent)
                    {
                        lastPercent = whole;
                        UpdateStatus = $"正在下载更新… {whole:0}%";
                    }
                }),
                CancellationToken.None);

            UpdateProgress = 100;
            IsDownloading = false;
            var restart = MessageBox.Show(
                "更新已就绪。是否立即重启应用以完成更新?",
                "更新就绪", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (restart != MessageBoxResult.Yes)
            {
                UpdateStatus = "更新文件已下载到本地;下次应用启动时会自动清理,重新点击“检查更新”可再次升级。";
                return;
            }

            if (_updateService.TryLaunchUpdater(out var err))
            {
                Application.Current.Shutdown();
            }
            else
            {
                UpdateStatus = $"无法自动更新:{err}。";
                MessageBox.Show(
                    "无法自动完成更新。可手动把更新目录中的 GalManagement.exe 覆盖到当前程序后重启。",
                    "更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            IsDownloading = false;
            UpdateStatus = $"下载更新失败:{ex.Message}";
            MessageBox.Show(UpdateStatus, "更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FriendlyError(UpdateErrorKind kind) => kind switch
    {
        UpdateErrorKind.Network => "无法连接更新服务器,请检查网络或代理后重试。",
        UpdateErrorKind.RateLimited => "请求过于频繁(超出 GitHub 限流),请稍后再试。",
        UpdateErrorKind.NoRelease => "仓库中暂无可用发布版本。",
        UpdateErrorKind.NoAsset => "发布中未找到更新程序(GalManagement.exe)。",
        UpdateErrorKind.BadVersion => "发布版本号无法解析,暂不更新。",
        _ => "服务器返回异常,请稍后再试。",
    };

    partial void OnIsUpdateBusyChanged(bool value) =>
        CheckUpdateCommand.NotifyCanExecuteChanged();
}
