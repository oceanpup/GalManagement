using GalManagement.Models;

namespace GalManagement.Data;

/// <summary>游戏列表的搜索/筛选/排序参数。</summary>
public class GameFilter
{
    public string? NameKeyword { get; set; }

    public GameStatus? Status { get; set; }

    public string? Developer { get; set; }

    /// <summary>排序字段:Name / Rating / PlayTimeHours / CompletedDate / UpdatedAt。</summary>
    public string SortBy { get; set; } = "UpdatedAt";

    public bool Ascending { get; set; } = false;
}
