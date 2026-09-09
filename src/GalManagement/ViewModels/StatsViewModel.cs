using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GalManagement.Data;
using GalManagement.Models;

namespace GalManagement.ViewModels;

public class RatingBar
{
    public int Score { get; set; }
    public int Count { get; set; }
    public double Ratio { get; set; }
}

public class PlatformBar
{
    public string Platform { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Ratio { get; set; }
}

public partial class StatsViewModel : ObservableObject
{
    private readonly GameRepository _repo;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private double _averageRating;

    [ObservableProperty]
    private int _playingCount;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _wantToPlayCount;

    [ObservableProperty]
    private int _shelvedCount;

    public ObservableCollection<RatingBar> RatingDistribution { get; } = new();
    public ObservableCollection<PlatformBar> PlatformDistribution { get; } = new();

    public StatsViewModel(GameRepository repo)
    {
        _repo = repo;
        Refresh();
    }

    public void Refresh()
    {
        var games = _repo.GetAll();

        TotalCount = games.Count;
        AverageRating = games.Where(g => g.EffectiveRating > 0)
            .Select(g => g.EffectiveRating)
            .DefaultIfEmpty(0)
            .Average();

        PlayingCount = games.Count(g => g.Status == GameStatus.Playing);
        CompletedCount = games.Count(g => g.Status == GameStatus.Completed);
        WantToPlayCount = games.Count(g => g.Status == GameStatus.WantToPlay);
        ShelvedCount = games.Count(g => g.Status == GameStatus.Shelved);

        var buckets = new int[11];
        foreach (var g in games)
        {
            if (g.EffectiveRating <= 0)
                continue;
            var b = Math.Clamp((int)Math.Floor(g.EffectiveRating), 1, 10);
            buckets[b]++;
        }

        var max = buckets.Max();
        RatingDistribution.Clear();
        for (var i = 1; i <= 10; i++)
        {
            RatingDistribution.Add(new RatingBar
            {
                Score = i,
                Count = buckets[i],
                Ratio = max > 0 ? (double)buckets[i] / max : 0,
            });
        }

        var platformCounts = games
            .Where(g => !string.IsNullOrWhiteSpace(g.Platform))
            .GroupBy(g => g.Platform!)
            .Select(g => new { Platform = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        var maxPlatform = platformCounts.Count > 0 ? platformCounts.Max(x => x.Count) : 0;
        PlatformDistribution.Clear();
        foreach (var p in platformCounts)
        {
            PlatformDistribution.Add(new PlatformBar
            {
                Platform = p.Platform,
                Count = p.Count,
                Ratio = maxPlatform > 0 ? (double)p.Count / maxPlatform : 0,
            });
        }
    }
}
