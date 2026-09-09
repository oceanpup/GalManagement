using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalManagement.Data;
using GalManagement.Models;
using GalManagement.Services;

namespace GalManagement.ViewModels;

public record StatusFilterOption(string Display, GameStatus? Value)
{
    public override string ToString() => Display;
}

public record PlatformOption(string Display, string? Value)
{
    public override string ToString() => Display;
}

public record SortOption(string Display, string SortBy, bool Ascending)
{
    public override string ToString() => Display;
}

public partial class GameLibraryViewModel : ObservableObject
{
    private readonly GameRepository _repo;
    private readonly CoverImageService _coverService;
    private readonly GameLauncherService _launcher;

    public ObservableCollection<Game> Games { get; } = new();

    [ObservableProperty]
    private Game? _selectedGame;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public IReadOnlyList<StatusFilterOption> StatusFilterOptions { get; } =
    [
        new StatusFilterOption("全部状态", null),
        new StatusFilterOption("玩过", GameStatus.Completed),
        new StatusFilterOption("在玩", GameStatus.Playing),
        new StatusFilterOption("想玩", GameStatus.WantToPlay),
        new StatusFilterOption("搁置", GameStatus.Shelved),
    ];

    [ObservableProperty]
    private StatusFilterOption _selectedStatus;

    public ObservableCollection<PlatformOption> Platforms { get; } = new();

    [ObservableProperty]
    private PlatformOption _selectedPlatform;

    public IReadOnlyList<SortOption> SortOptions { get; } =
    [
        new SortOption("最近更新", "UpdatedAt", false),
        new SortOption("名称 A-Z", "Name", true),
        new SortOption("名称 Z-A", "Name", false),
        new SortOption("评分从高到低", "Rating", false),
        new SortOption("评分从低到高", "Rating", true),
        new SortOption("时长从高到低", "PlayTimeHours", false),
        new SortOption("通关日期从新到旧", "CompletedDate", false),
    ];

    [ObservableProperty]
    private SortOption _selectedSort;

    [ObservableProperty]
    private bool _isFilterOpen;

    public RelayCommand AddCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ToggleFilterCommand { get; }
    public RelayCommand<Game> LaunchCommand { get; }

    public event Action<Game?>? EditRequested;
    public event Action<Game>? DetailRequested;

    public GameLibraryViewModel(GameRepository repo, CoverImageService coverService, GameLauncherService launcher)
    {
        _repo = repo;
        _coverService = coverService;
        _launcher = launcher;
        _selectedStatus = StatusFilterOptions[0];
        _selectedPlatform = new PlatformOption("全部平台", null);
        _selectedSort = SortOptions[0];

        AddCommand = new RelayCommand(() => EditRequested?.Invoke(null));
        EditCommand = new RelayCommand(() => EditRequested?.Invoke(SelectedGame), () => SelectedGame is not null);
        DeleteCommand = new RelayCommand(Delete, () => SelectedGame is not null);
        ToggleFilterCommand = new RelayCommand(() => IsFilterOpen = !IsFilterOpen);
        LaunchCommand = new RelayCommand<Game>(LaunchGame);

        Reload();
    }

    public void OpenDetail()
    {
        if (SelectedGame is not null)
            DetailRequested?.Invoke(SelectedGame);
    }

    partial void OnSearchTextChanged(string value) => Reload();
    partial void OnSelectedStatusChanged(StatusFilterOption value) => Reload();
    partial void OnSelectedPlatformChanged(PlatformOption value) => Reload();
    partial void OnSelectedSortChanged(SortOption value) => Reload();
    partial void OnSelectedGameChanged(Game? value)
    {
        EditCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    public void Reload()
    {
        var filter = new GameFilter
        {
            NameKeyword = SearchText,
            Status = SelectedStatus.Value,
            Platform = SelectedPlatform.Value,
            SortBy = SelectedSort.SortBy,
            Ascending = SelectedSort.Ascending,
        };

        var list = _repo.Search(filter);
        Games.Clear();
        foreach (var g in list)
        {
            g.CoverFullPath = _coverService.GetFullPath(g.CoverPath);
            Games.Add(g);
        }

        RefreshPlatforms();
    }

    private void LaunchGame(Game? game)
    {
        if (game is null)
            return;

        var error = _launcher.Launch(game.LaunchPath);
        if (error is not null)
        {
            System.Windows.MessageBox.Show(error, "无法启动",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private void Delete()
    {
        if (SelectedGame is null)
            return;

        var result = System.Windows.MessageBox.Show(
            $"确定要删除「{SelectedGame.Name}」吗?",
            "删除确认",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
            return;

        _coverService.Delete(SelectedGame.CoverPath);
        _repo.Delete(SelectedGame.Id);
        SelectedGame = null;
        Reload();
    }

    private void RefreshPlatforms()
    {
        var current = SelectedPlatform.Value;
        var all = new List<PlatformOption> { new("全部平台", null) };
        all.AddRange(_repo.GetDistinctPlatforms().Select(p => new PlatformOption(p, p)));

        Platforms.Clear();
        foreach (var o in all)
            Platforms.Add(o);

        SelectedPlatform = Platforms.FirstOrDefault(o => o.Value == current) ?? Platforms[0];
    }
}
