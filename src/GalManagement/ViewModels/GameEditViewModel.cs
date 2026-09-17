using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalManagement.Data;
using GalManagement.Models;
using GalManagement.Services;

namespace GalManagement.ViewModels;

/// <summary>云端目录选择器里的一行:目录名 + 一句概况。</summary>
public sealed record CloudDirChoice(string DirName, string Detail);

public partial class GameEditViewModel : ObservableObject
{
    private readonly GameRepository _repo;
    private readonly TagRepository _tagRepo;
    private readonly CoverImageService _coverService;
    private readonly GameLauncherService _launcher;
    private readonly PlayTimeTracker _tracker;
    private readonly CloudSaveService _cloudSave;
    private readonly string? _originalCoverPath;

    /// <summary>打开弹窗时的原名,用于首次钉死云端目录名(见 Save)。</summary>
    private readonly string? _originalName;

    private readonly List<string> _allTags;

    /// <summary>云端已有目录的全量缓存;第一次展开选择器时拉一次,之后筛选都走内存。</summary>
    private List<CloudGameDir>? _cloudDirs;

    public Game Current { get; }

    public bool IsNew => Current.Id == 0;

    public string Title => IsNew ? "新增游戏" : "编辑游戏";

    [ObservableProperty]
    private string? _coverPreviewPath;

    [ObservableProperty]
    private string _newTag = string.Empty;

    [ObservableProperty]
    private string? _selectedSuggestion;

    /// <summary>云端目录选择器是否展开。</summary>
    [ObservableProperty]
    private bool _isCloudDirPickerOpen;

    /// <summary>选择器里的搜索关键字,随输入即时筛选。</summary>
    [ObservableProperty]
    private string? _cloudDirQuery;

    /// <summary>选择器的状态提示(正在读取 / 有几个候选 / 出错原因)。</summary>
    [ObservableProperty]
    private string? _cloudDirListStatus;

    public ObservableCollection<string> TagSuggestions { get; } = new();

    public ObservableCollection<CloudDirChoice> CloudDirChoices { get; } = new();

    public IReadOnlyList<GameStatus> StatusOptions { get; } = Enum.GetValues<GameStatus>();

    public RelayCommand PickCoverCommand { get; }
    public RelayCommand PickLaunchCommand { get; }
    public RelayCommand PickSaveFolderCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand AddTagCommand { get; }
    public RelayCommand<string> RemoveTagCommand { get; }
    public IAsyncRelayCommand ToggleCloudDirPickerCommand { get; }
    public RelayCommand<CloudDirChoice> PickCloudDirCommand { get; }

    public event Action<bool>? RequestClose;

    public GameEditViewModel(Game? game, GameRepository repo, TagRepository tagRepo,
        CoverImageService coverService, GameLauncherService launcher, PlayTimeTracker tracker,
        CloudSaveService cloudSave)
    {
        _repo = repo;
        _tagRepo = tagRepo;
        _coverService = coverService;
        _launcher = launcher;
        _tracker = tracker;
        _cloudSave = cloudSave;
        Current = CloneOrNew(game);
        _originalCoverPath = Current.CoverPath;
        _originalName = game?.Name;
        _coverPreviewPath = coverService.GetFullPath(Current.CoverPath);

        if (!IsNew)
        {
            foreach (var t in _tagRepo.GetForGame(Current.Id))
                Current.Tags.Add(t);
        }
        _allTags = _tagRepo.GetAllOrdered();
        RefreshSuggestions();

        PickCoverCommand = new RelayCommand(PickCover);
        PickLaunchCommand = new RelayCommand(PickLaunch);
        PickSaveFolderCommand = new RelayCommand(PickSaveFolder);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
        AddTagCommand = new RelayCommand(() => AddTag(NewTag));
        RemoveTagCommand = new RelayCommand<string>(RemoveTag);
        ToggleCloudDirPickerCommand = new AsyncRelayCommand(ToggleCloudDirPickerAsync);
        PickCloudDirCommand = new RelayCommand<CloudDirChoice>(PickCloudDir);
    }

    /// <summary>
    /// 展开 / 收起云端目录选择器。第一次展开才去拉云端目录,之后都走内存缓存 ——
    /// 每次开编辑弹窗都联网拉一遍列表太慢,而且离线时会让弹窗一打开就卡住。
    /// </summary>
    private async Task ToggleCloudDirPickerAsync()
    {
        IsCloudDirPickerOpen = !IsCloudDirPickerOpen;
        if (!IsCloudDirPickerOpen || _cloudDirs is not null)
            return;

        CloudDirListStatus = "正在读取云端存档目录…";
        try
        {
            _cloudDirs = (await _cloudSave.ListCloudGamesAsync())
                .OrderBy(d => d.DirName, StringComparer.Ordinal)
                .ToList();
            RefreshCloudDirChoices();
        }
        catch (Exception ex)
        {
            CloudDirListStatus = $"读取云端目录失败:{ex.Message}";
        }
    }

    partial void OnCloudDirQueryChanged(string? value) => RefreshCloudDirChoices();

    private void RefreshCloudDirChoices()
    {
        // 拉取失败过就没缓存,别把错误提示盖成「云端还没有存档」
        if (_cloudDirs is null)
            return;

        CloudDirChoices.Clear();
        foreach (var dir in CloudSaveService.FilterCloudDirs(_cloudDirs, CloudDirQuery))
            CloudDirChoices.Add(new CloudDirChoice(dir.DirName, CloudSaveService.DescribeCloudDir(dir)));

        CloudDirListStatus = _cloudDirs.Count == 0
            ? "网盘上还没有任何游戏的云端存档。"
            : CloudDirChoices.Count == 0
                ? "没有匹配的云端目录。"
                : $"共 {CloudDirChoices.Count} 个可选目录,点一下填进上面的输入框。";
    }

    /// <summary>挑中一个已有目录:直接填进去,并收起选择器。</summary>
    private void PickCloudDir(CloudDirChoice? choice)
    {
        if (choice is null)
            return;

        Current.CloudDir = choice.DirName;
        IsCloudDirPickerOpen = false;
    }

    private static Game CloneOrNew(Game? game)
    {
        if (game is null)
            return new Game();

        return new Game
        {
            Id = game.Id,
            Name = game.Name,
            CoverPath = game.CoverPath,
            Rating = game.Rating,
            Summary = game.Summary,
            LaunchPath = game.LaunchPath,
            SavePath = game.SavePath,
            CloudDir = game.CloudDir,
            Status = game.Status,
            Developer = game.Developer,
            CompletedDate = game.CompletedDate,
            PlayTimeHours = game.PlayTimeHours,
            CreatedAt = game.CreatedAt,
            UpdatedAt = game.UpdatedAt,
            ThumbOffsetX = game.ThumbOffsetX,
            ThumbOffsetY = game.ThumbOffsetY,
        };
    }

    private void PickLaunch()
    {
        var path = _launcher.PickExecutable();
        if (path is not null)
            Current.LaunchPath = path;
    }

    private void PickSaveFolder()
    {
        var path = _launcher.PickSaveFolder();
        if (path is not null)
            Current.SavePath = path;
    }

    private void PickCover()
    {
        var name = _coverService.PickAndCopy();
        if (name is null)
            return;

        Current.CoverPath = name;
        Current.ThumbOffsetX = 0.5;
        Current.ThumbOffsetY = 0.5;
        CoverPreviewPath = _coverService.GetFullPath(name);
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Current.Name))
        {
            MessageBox.Show("请输入游戏名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_originalCoverPath is not null && Current.CoverPath != _originalCoverPath)
            _coverService.Delete(_originalCoverPath);

        // 云端目录名只在首次保存时钉死,之后改名不再影响它。已有记录要用「改名前」的老名字:
        // 万一这个游戏早就传过存档,按新名字钉就会指向一个空目录,把云端存档白甩掉。
        // 存清理过的值,免得输入框显示的名字和云端真正建出来的目录对不上。
        Current.CloudDir = BaiduPanClient.SanitizeName(
            string.IsNullOrWhiteSpace(Current.CloudDir)
                ? IsNew ? Current.Name : _originalName
                : Current.CloudDir);

        if (Current.Id == 0)
            _repo.Add(Current);
        else
            _repo.Update(Current);

        _tagRepo.SetForGame(Current.Id, Current.Tags);

        // 中途改了时长:以新值为基值,计时从此刻继续累加
        _tracker.Rebase(Current.Id, Current.PlayTimeHours ?? 0);

        RequestClose?.Invoke(true);
    }

    private void AddTag(string? raw)
    {
        var name = raw?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            NewTag = string.Empty;
            return;
        }

        if (!Current.Tags.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase)))
            Current.Tags.Add(name);

        NewTag = string.Empty;
        RefreshSuggestions();
    }

    private void RemoveTag(string? name)
    {
        if (name is null)
            return;

        var existing = Current.Tags.FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            Current.Tags.Remove(existing);

        RefreshSuggestions();
    }

    private void RefreshSuggestions()
    {
        TagSuggestions.Clear();
        foreach (var t in _allTags)
        {
            if (!Current.Tags.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
                TagSuggestions.Add(t);
        }
    }

    partial void OnSelectedSuggestionChanged(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        AddTag(value);
        SelectedSuggestion = null;
    }

    private void Cancel()
    {
        if (Current.CoverPath is not null && Current.CoverPath != _originalCoverPath)
            _coverService.Delete(Current.CoverPath);

        RequestClose?.Invoke(false);
    }
}
