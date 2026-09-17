using CommunityToolkit.Mvvm.ComponentModel;

namespace GalManagement.Models;

/// <summary>应用设置,持久化到 data\settings.json。</summary>
public partial class AppSettings : ObservableObject
{
    /// <summary>当前背景图文件名(位于 data\backgrounds\),null 表示默认背景。</summary>
    public string? BackgroundFileName { get; set; }

    /// <summary>主题:Dark / Light。</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>窗口左上角显示的名称。</summary>
    [ObservableProperty]
    private string _appTitle = "旮旯给木管理";

    /// <summary>应用图标文件名(位于 data\icons\),null 表示默认图标。</summary>
    [ObservableProperty]
    private string? _iconFileName;

    // ---------- 云存档:百度网盘 ----------

    /// <summary>开放平台 AppKey,留空则用 BaiduPanClient 里的内置常量。</summary>
    public string? BaiduAppKey { get; set; }

    /// <summary>开放平台 SecretKey,留空则用内置常量。</summary>
    public string? BaiduSecretKey { get; set; }

    /// <summary>access_token,有效期 30 天。</summary>
    public string? BaiduAccessToken { get; set; }

    /// <summary>refresh_token:只能用一次,每次刷新都会换新值,务必覆盖保存。</summary>
    public string? BaiduRefreshToken { get; set; }

    /// <summary>access_token 到期时刻(unix 秒)。</summary>
    public long BaiduTokenExpiresAtUnix { get; set; }

    /// <summary>已绑定账号的昵称,仅用于设置页显示。</summary>
    public string? BaiduUserName { get; set; }

    /// <summary>已绑定账号的 uk,仅用于设置页显示。</summary>
    public string? BaiduUserUk { get; set; }

    /// <summary>启动游戏前的同步档位:Ask / Auto / Off。</summary>
    public string SyncModeBeforeLaunch { get; set; } = "Ask";

    /// <summary>游玩结束后的同步档位:Ask / Auto / Off。</summary>
    public string SyncModeAfterPlay { get; set; } = "Ask";
}
