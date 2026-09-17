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

public record DeveloperOption(string Display, string? Value)
{
    public override string ToString() => Display;
}

public record SortOption(string Display, string SortBy, bool Ascending)
{
    public override string ToString() => Display;
}

/// <summary>标签筛选芯片:勾选变化时回调(用于重建筛选)。</summary>
public partial class TagFilterItem : ObservableObject
{
    private readonly Action _onChanged;

    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;

    public TagFilterItem(string name, Action onChanged)
    {
        Name = name;
        _onChanged = onChanged;
    }

    partial void OnIsSelectedChanged(bool value) => _onChanged();
}

public partial class GameLibraryViewModel : ObservableObject
{
    private readonly GameRepository _repo;
    private readonly TagRepository _tagRepo;
    private readonly CoverImageService _coverService;
    private readonly PlayTimeTracker _tracker;
    private readonly CloudSaveService _cloudSave;
    private bool _suppressTagReload;

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

    public ObservableCollection<DeveloperOption> Developers { get; } = new();

    [ObservableProperty]
    private DeveloperOption _selectedDeveloper;

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

    public ObservableCollection<TagFilterItem> TagFilters { get; } = new();

    public RelayCommand AddCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand ToggleFilterCommand { get; }
    public IAsyncRelayCommand<Game> LaunchCommand { get; }
    public IAsyncRelayCommand<Game> SaveSyncCommand { get; }

    public event Action<Game?>? EditRequested;
    public event Action<Game>? DetailRequested;

    /// <summary>需要弹出云存档同步窗口;由 MainWindow 接住并 ShowDialog(会阻塞到用户选完)。</summary>
    public event Action<Game, SaveSyncScenario, SaveCompare>? SaveSyncRequested;

    public GameLibraryViewModel(GameRepository repo, TagRepository tagRepo, CoverImageService coverService,
        PlayTimeTracker tracker, CloudSaveService cloudSave)
    {
        _repo = repo;
        _tagRepo = tagRepo;
        _coverService = coverService;
        _tracker = tracker;
        _cloudSave = cloudSave;
        _tracker.Tick += ApplyLiveState;
        _tracker.SessionEnded += OnSessionEnded;
        _selectedStatus = StatusFilterOptions[0];
        _selectedDeveloper = new DeveloperOption("全部开发商", null);
        _selectedSort = SortOptions[0];

        AddCommand = new RelayCommand(() => EditRequested?.Invoke(null));
        EditCommand = new RelayCommand(() => EditRequested?.Invoke(SelectedGame), () => SelectedGame is not null);
        DeleteCommand = new RelayCommand(Delete, () => SelectedGame is not null);
        ToggleFilterCommand = new RelayCommand(() => IsFilterOpen = !IsFilterOpen);
        LaunchCommand = new AsyncRelayCommand<Game>(LaunchGameAsync);
        SaveSyncCommand = new AsyncRelayCommand<Game>(SaveSyncAsync, CanSaveSync);

        Reload();
    }

    public void OpenDetail()
    {
        if (SelectedGame is not null)
            DetailRequested?.Invoke(SelectedGame);
    }

    partial void OnSearchTextChanged(string value) => Reload();
    partial void OnSelectedStatusChanged(StatusFilterOption value) => Reload();
    partial void OnSelectedDeveloperChanged(DeveloperOption value) => Reload();
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
            Keyword = SearchText,
            Status = SelectedStatus.Value,
            Developer = SelectedDeveloper.Value,
            Tags = TagFilters.Where(t => t.IsSelected).Select(t => t.Name).ToList(),
            SortBy = SelectedSort.SortBy,
            Ascending = SelectedSort.Ascending,
        };

        var list = _repo.Search(filter);
        var tagMap = _tagRepo.GetForGames(list.Select(g => g.Id));

        Games.Clear();
        foreach (var g in list)
        {
            g.CoverFullPath = _coverService.GetFullPath(g.CoverPath);
            if (tagMap.TryGetValue(g.Id, out var tags))
            {
                foreach (var t in tags)
                    g.Tags.Add(t);
            }
            Games.Add(g);
        }

        RefreshTagFilters();
        RefreshDevelopers();
        ApplyLiveState();
    }

    private void OnTagFilterChanged()
    {
        if (!_suppressTagReload)
            Reload();
    }

    private void RefreshTagFilters()
    {
        _suppressTagReload = true;
        try
        {
            var selected = TagFilters.Where(t => t.IsSelected)
                .Select(t => t.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            TagFilters.Clear();
            foreach (var name in _tagRepo.GetAllOrdered())
                TagFilters.Add(new TagFilterItem(name, OnTagFilterChanged) { IsSelected = selected.Contains(name) });
        }
        finally
        {
            _suppressTagReload = false;
        }
    }

    private async Task LaunchGameAsync(Game? game)
    {
        if (game is null)
            return;

        await SyncBeforeLaunchAsync(game);

        var message = _tracker.Start(game);
        ApplyLiveState();

        if (message is not null)
        {
            System.Windows.MessageBox.Show(message, "启动",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>云存档前置条件:已绑定 + 该游戏设了存档目录 + 档位没关。</summary>
    private bool CloudEnabledFor(Game game, SaveSyncMode mode) =>
        mode != SaveSyncMode.Off && _cloudSave.IsBound && !string.IsNullOrWhiteSpace(game.SavePath);

    /// <summary>
    /// 启动前的同步。任何云端异常都只提示一句,绝不拦住启动——
    /// 云盘挂了也得让人玩得上游戏。
    /// </summary>
    private async Task SyncBeforeLaunchAsync(Game game)
    {
        var mode = _cloudSave.BeforeLaunchMode;
        if (!CloudEnabledFor(game, mode))
            return;

        try
        {
            var compare = await _cloudSave.CompareAsync(game);

            if (mode == SaveSyncMode.Auto)
            {
                // 启动前只关心「云端更新了得先拉下来」,本地更新的那份交给游玩后上传
                if (compare.State == SaveSyncState.RemoteNewer)
                    await _cloudSave.DownloadAsync(game);
                return;
            }

            // 只有真的存在新旧差异才打扰用户
            if (compare.State is not (SaveSyncState.LocalNewer or SaveSyncState.RemoteNewer or SaveSyncState.LocalMissing))
                return;

            SaveSyncRequested?.Invoke(game, SaveSyncScenario.BeforeLaunch, compare);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"云存档同步失败,已直接启动游戏。\n{ex.Message}",
                "云存档", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    private bool CanSaveSync(Game? game) =>
        game is not null && _cloudSave.IsBound && !string.IsNullOrWhiteSpace(game.SavePath);

    /// <summary>列表上手动触发的同步。</summary>
    private async Task SaveSyncAsync(Game? game)
    {
        if (game is null || !CanSaveSync(game))
            return;

        game.IsSaveSyncing = true;
        try
        {
            var compare = await _cloudSave.CompareAsync(game);
            SaveSyncRequested?.Invoke(game, SaveSyncScenario.Manual, compare);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"无法比对云存档:{ex.Message}",
                "云存档", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            game.IsSaveSyncing = false;
        }
    }

    /// <summary>游戏退出后的同步。计时器回调里发起,异常必须全部兜住,不能冒到计时器调用栈上。</summary>
    private async Task RunAfterPlaySyncAsync(Game game)
    {
        var mode = _cloudSave.AfterPlayMode;
        if (!CloudEnabledFor(game, mode))
            return;

        try
        {
            var compare = await _cloudSave.CompareAsync(game);
            if (compare.Local.FileCount == 0)   // 没存档可传,不必打扰
                return;

            if (mode == SaveSyncMode.Auto)
            {
                await _cloudSave.UploadAsync(game, new Progress<string>(s => game.SaveSyncText = s));
                game.SaveSyncText = string.Empty;
                return;
            }

            SaveSyncRequested?.Invoke(game, SaveSyncScenario.AfterPlay, compare);
        }
        catch (Exception ex)
        {
            game.SaveSyncText = string.Empty;
            System.Windows.MessageBox.Show(
                $"云存档上传失败:{ex.Message}\n本次存档仍完整保存在本地。",
                "云存档", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>把计时中的游戏的实时时长写回列表对象(界面因此会随时间增长)。</summary>
    private void ApplyLiveState()
    {
        foreach (var game in Games)
        {
            var total = _tracker.CurrentTotal(game.Id);
            if (total is null)
                continue;

            game.IsRunning = true;
            game.PlayTimeHours = Math.Round(total.Value, 2);

            var elapsed = _tracker.Elapsed(game.Id);
            game.SessionElapsedText = $"本次已运行 {(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        }
    }

    private void OnSessionEnded(int gameId, double total)
    {
        var game = Games.FirstOrDefault(g => g.Id == gameId);
        if (game is null)
            return;

        game.PlayTimeHours = total;
        game.IsRunning = false;
        game.SessionElapsedText = string.Empty;

        _ = RunAfterPlaySyncAsync(game);
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

        _tracker.Stop(SelectedGame.Id);
        _coverService.Delete(SelectedGame.CoverPath);
        _tagRepo.DeleteForGame(SelectedGame.Id);
        _repo.Delete(SelectedGame.Id);
        SelectedGame = null;
        Reload();
    }

    private void RefreshDevelopers()
    {
        var current = SelectedDeveloper.Value;
        var all = new List<DeveloperOption> { new("全部开发商", null) };
        all.AddRange(_repo.GetDistinctDevelopers().Select(p => new DeveloperOption(p, p)));

        Developers.Clear();
        foreach (var o in all)
            Developers.Add(o);

        SelectedDeveloper = Developers.FirstOrDefault(o => o.Value == current) ?? Developers[0];
    }
}
