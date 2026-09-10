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

public class DeveloperBar
{
    public string Developer { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Ratio { get; set; }
}

public class TagBar
{
    public string Tag { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Ratio { get; set; }
}

public partial class StatsViewModel : ObservableObject
{
    private readonly GameRepository _repo;
    private readonly TagRepository _tagRepo;

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
    public ObservableCollection<DeveloperBar> DeveloperDistribution { get; } = new();
    public ObservableCollection<TagBar> TagDistribution { get; } = new();

    public StatsViewModel(GameRepository repo, TagRepository tagRepo)
    {
        _repo = repo;
        _tagRepo = tagRepo;
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

        // 各项柱长一律 = 该项游戏数 / 游戏总数(占整个库的比例)
        RatingDistribution.Clear();
        for (var i = 1; i <= 10; i++)
        {
            RatingDistribution.Add(new RatingBar
            {
                Score = i,
                Count = buckets[i],
                Ratio = TotalCount > 0 ? (double)buckets[i] / TotalCount : 0,
            });
        }

        var developerCounts = games
            .Where(g => !string.IsNullOrWhiteSpace(g.Developer))
            .GroupBy(g => g.Developer!)
            .Select(g => new { Developer = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .ToList();

        DeveloperDistribution.Clear();
        foreach (var d in developerCounts)
        {
            DeveloperDistribution.Add(new DeveloperBar
            {
                Developer = d.Developer,
                Count = d.Count,
                Ratio = TotalCount > 0 ? (double)d.Count / TotalCount : 0,
            });
        }

        var tagCounts = _tagRepo.GetUsageCounts();
        TagDistribution.Clear();
        foreach (var (name, count) in tagCounts)
        {
            TagDistribution.Add(new TagBar
            {
                Tag = name,
                Count = count,
                Ratio = TotalCount > 0 ? (double)count / TotalCount : 0,
            });
        }
    }
}
