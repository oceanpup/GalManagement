using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalManagement.Data;
using GalManagement.Models;
using GalManagement.Services;

namespace GalManagement.ViewModels;

public partial class GameEditViewModel : ObservableObject
{
    private readonly GameRepository _repo;
    private readonly TagRepository _tagRepo;
    private readonly CoverImageService _coverService;
    private readonly GameLauncherService _launcher;
    private readonly PlayTimeTracker _tracker;
    private readonly string? _originalCoverPath;
    private readonly List<string> _allTags;

    public Game Current { get; }

    public bool IsNew => Current.Id == 0;

    public string Title => IsNew ? "新增游戏" : "编辑游戏";

    [ObservableProperty]
    private string? _coverPreviewPath;

    [ObservableProperty]
    private string _newTag = string.Empty;

    [ObservableProperty]
    private string? _selectedSuggestion;

    public ObservableCollection<string> TagSuggestions { get; } = new();

    public IReadOnlyList<GameStatus> StatusOptions { get; } = Enum.GetValues<GameStatus>();

    public RelayCommand PickCoverCommand { get; }
    public RelayCommand PickLaunchCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand AddTagCommand { get; }
    public RelayCommand<string> RemoveTagCommand { get; }

    public event Action<bool>? RequestClose;

    public GameEditViewModel(Game? game, GameRepository repo, TagRepository tagRepo,
        CoverImageService coverService, GameLauncherService launcher, PlayTimeTracker tracker)
    {
        _repo = repo;
        _tagRepo = tagRepo;
        _coverService = coverService;
        _launcher = launcher;
        _tracker = tracker;
        Current = CloneOrNew(game);
        _originalCoverPath = Current.CoverPath;
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
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
        AddTagCommand = new RelayCommand(() => AddTag(NewTag));
        RemoveTagCommand = new RelayCommand<string>(RemoveTag);
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
