using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GalManagement.Models;

/// <summary>游戏记录实体。</summary>
public partial class Game : ObservableObject
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _coverPath;

    [ObservableProperty]
    private double _rating;

    [ObservableProperty]
    private string? _summary;

    /// <summary>启动程序路径(本地文件,仅记录不随应用复制)。</summary>
    [ObservableProperty]
    private string? _launchPath;

    /// <summary>存档目录(本地目录,云存档同步的本地端;留空则不参与同步)。</summary>
    [ObservableProperty]
    private string? _savePath;

    /// <summary>
    /// 云端目录名。首次保存时钉死,之后改名不再改变它,否则云端会另开一个目录、
    /// 旧存档就被甩掉了。留空则回落到当前游戏名。
    /// </summary>
    [ObservableProperty]
    private string? _cloudDir;

    [ObservableProperty]
    private GameStatus _status;

    [ObservableProperty]
    private string? _developer;

    [ObservableProperty]
    private DateTime? _completedDate;

    [ObservableProperty]
    private double? _playTimeHours;

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    private DateTime _updatedAt;

    /// <summary>缩略图水平焦点(0–1,0.5=居中)。</summary>
    [ObservableProperty]
    private double _thumbOffsetX = 0.5;

    /// <summary>缩略图垂直焦点(0–1,0.5=居中)。</summary>
    [ObservableProperty]
    private double _thumbOffsetY = 0.5;

    /// <summary>封面的完整路径(仅 UI 用,不存数据库)。</summary>
    [ObservableProperty]
    private string? _coverFullPath;

    /// <summary>总体评分:有分项评价时为分项平均分,否则为手动评分(仅 UI 用,不存数据库)。</summary>
    [ObservableProperty]
    private double _effectiveRating;

    /// <summary>标签(经 TagRepository 装载/保存,不存 Games 表)。</summary>
    [ObservableProperty]
    private ObservableCollection<string> _tags = new();

    /// <summary>是否正在计时中(仅 UI 用,不存数据库)。</summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>本次运行的已用时长文本,如 "1:23:45"(仅 UI 用,不存数据库)。</summary>
    [ObservableProperty]
    private string _sessionElapsedText = string.Empty;

    /// <summary>云存档状态文本,如 "上传中 3/12"(仅 UI 用,不存数据库)。</summary>
    [ObservableProperty]
    private string _saveSyncText = string.Empty;

    /// <summary>云存档同步进行中(仅 UI 用,不存数据库)。</summary>
    [ObservableProperty]
    private bool _isSaveSyncing;
}
