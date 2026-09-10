using GalManagement.Models;

namespace GalManagement.Data;

/// <summary>游戏列表的搜索/筛选/排序参数。</summary>
public class GameFilter
{
    /// <summary>关键词:匹配游戏名称、开发商或标签名(模糊)。</summary>
    public string? Keyword { get; set; }

    public GameStatus? Status { get; set; }

    public string? Developer { get; set; }

    /// <summary>标签筛选(AND:游戏须同时包含全部所选标签)。</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>排序字段:Name / Rating / PlayTimeHours / CompletedDate / UpdatedAt。</summary>
    public string SortBy { get; set; } = "UpdatedAt";

    public bool Ascending { get; set; } = false;
}
