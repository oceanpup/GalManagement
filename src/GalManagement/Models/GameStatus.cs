namespace GalManagement.Models;

/// <summary>游戏游玩状态。</summary>
public enum GameStatus
{
    /// <summary>玩过</summary>
    Completed,

    /// <summary>在玩</summary>
    Playing,

    /// <summary>想玩</summary>
    WantToPlay,

    /// <summary>搁置</summary>
    Shelved,
}
