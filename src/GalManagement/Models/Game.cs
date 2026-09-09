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

    [ObservableProperty]
    private GameStatus _status;

    [ObservableProperty]
    private string? _platform;

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
}
