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
}
