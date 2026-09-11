using CommunityToolkit.Mvvm.ComponentModel;

namespace GalManagement.Models;

/// <summary>游戏某个具体部分的评价与打分。</summary>
public partial class GameReview : ObservableObject
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private int _gameId;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private double _rating;

    [ObservableProperty]
    private string? _comment;

    [ObservableProperty]
    private string? _coverPath;

    /// <summary>封面的完整路径(仅 UI 用,不存数据库)。</summary>
    [ObservableProperty]
    private string? _coverFullPath;

    /// <summary>缩略图水平焦点(0–1,0.5=居中)。</summary>
    [ObservableProperty]
    private double _thumbOffsetX = 0.5;

    /// <summary>缩略图垂直焦点(0–1,0.5=居中)。</summary>
    [ObservableProperty]
    private double _thumbOffsetY = 0.5;

    /// <summary>手动排序用的顺序号(越小越靠前)。</summary>
    [ObservableProperty]
    private int _sortOrder;

    /// <summary>正在被拖动(仅 UI 用,不存数据库)。</summary>
    [ObservableProperty]
    private bool _isDragging;

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    private DateTime _updatedAt;
}
