using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalManagement.Services;

namespace GalManagement.ViewModels;

/// <summary>
/// 百度网盘账号绑定弹窗:设备码模式,不用回调地址也不用内嵌浏览器。
/// 打开即取用户码,用户到授权页输入码后本窗口轮询自动完成绑定。
/// </summary>
public partial class BaiduAccountViewModel : ObservableObject
{
    private readonly CloudSaveService _cloud;
    private readonly CancellationTokenSource _cts = new();
    private string? _verificationUrl;

    /// <summary>展示给用户、需要到授权页输入的用户码。</summary>
    [ObservableProperty]
    private string _userCode = string.Empty;

    [ObservableProperty]
    private string _status = "正在向百度申请绑定码…";

    /// <summary>轮询中(设备码倒计时递减)。</summary>
    [ObservableProperty]
    private bool _isWaiting;

    /// <summary>已成功绑定。</summary>
    [ObservableProperty]
    private bool _isBound;

    /// <summary>用户码剩余有效秒数。</summary>
    [ObservableProperty]
    private int _secondsLeft;

    public RelayCommand CopyAndOpenCommand { get; }

    public RelayCommand CloseCommand { get; }

    public event Action<bool>? RequestClose;

    public BaiduAccountViewModel(CloudSaveService cloud)
    {
        _cloud = cloud;
        CopyAndOpenCommand = new RelayCommand(CopyAndOpen, () => _verificationUrl is not null);
        CloseCommand = new RelayCommand(Close);
        _ = BindAsync();
    }

    private async Task BindAsync()
    {
        if (!_cloud.HasCredentials)
        {
            Status = "尚未配置 AppKey / SecretKey,请先在设置页的「云存档」卡片里填写。";
            return;
        }

        IsWaiting = true;
        try
        {
            var device = await _cloud.BeginBindAsync(_cts.Token);
            _verificationUrl = device.VerificationUrl;
            UserCode = device.UserCode;
            SecondsLeft = device.ExpiresInSeconds;
            Status = "请点击下方按钮打开授权页,输入用户码并确认授权。";
            CopyAndOpenCommand.NotifyCanExecuteChanged();

            var name = await _cloud.WaitForAuthorizationAsync(
                device, new Progress<int>(s => SecondsLeft = s), _cts.Token);

            IsBound = true;
            IsWaiting = false;
            Status = $"已绑定:{name}。关闭本窗口后即可使用云存档。";
        }
        catch (OperationCanceledException)
        {
            // 用户主动关窗,无需提示
        }
        catch (Exception ex)
        {
            IsWaiting = false;
            Status = $"绑定失败:{ex.Message}";
        }
    }

    private void CopyAndOpen()
    {
        if (_verificationUrl is null)
            return;

        try
        {
            Clipboard.SetText(UserCode);
        }
        catch
        {
            // 剪贴板偶发被别的进程占用,打不开就只打开授权页,用户码窗口里能看见
        }

        try
        {
            Process.Start(new ProcessStartInfo(_verificationUrl) { UseShellExecute = true });
            Status = $"已复制用户码 {UserCode},请在打开的页面里粘贴并确认授权。";
        }
        catch (Exception ex)
        {
            Status = $"无法打开授权页({ex.Message}),请手动访问:{_verificationUrl}";
        }
    }

    private void Close()
    {
        _cts.Cancel();
        RequestClose?.Invoke(IsBound);
    }
}
