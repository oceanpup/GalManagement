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
    private readonly CoverImageService _coverService;
    private readonly GameLauncherService _launcher;
    private readonly string? _originalCoverPath;

    public Game Current { get; }

    public bool IsNew => Current.Id == 0;

    public string Title => IsNew ? "新增游戏" : "编辑游戏";

    [ObservableProperty]
    private string? _coverPreviewPath;

    public IReadOnlyList<GameStatus> StatusOptions { get; } = Enum.GetValues<GameStatus>();

    public RelayCommand PickCoverCommand { get; }
    public RelayCommand PickLaunchCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public event Action<bool>? RequestClose;

    public GameEditViewModel(Game? game, GameRepository repo, CoverImageService coverService, GameLauncherService launcher)
    {
        _repo = repo;
        _coverService = coverService;
        _launcher = launcher;
        Current = CloneOrNew(game);
        _originalCoverPath = Current.CoverPath;
        _coverPreviewPath = coverService.GetFullPath(Current.CoverPath);

        PickCoverCommand = new RelayCommand(PickCover);
        PickLaunchCommand = new RelayCommand(PickLaunch);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
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

        RequestClose?.Invoke(true);
    }

    private void Cancel()
    {
        if (Current.CoverPath is not null && Current.CoverPath != _originalCoverPath)
            _coverService.Delete(Current.CoverPath);

        RequestClose?.Invoke(false);
    }
}
