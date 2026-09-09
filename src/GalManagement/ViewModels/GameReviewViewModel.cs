using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalManagement.Data;
using GalManagement.Models;
using GalManagement.Services;

namespace GalManagement.ViewModels;

public partial class GameReviewViewModel : ObservableObject
{
    private readonly ReviewRepository _repo;
    private readonly CoverImageService _coverService;
    private readonly bool _isNew;
    private readonly string? _originalCoverPath;

    public GameReview Review { get; }

    public string Title => _isNew ? "新增评价" : "编辑评价";

    [ObservableProperty]
    private string? _coverPreviewPath;

    public RelayCommand PickCoverCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }

    public event Action<bool>? RequestClose;

    public GameReviewViewModel(int gameId, GameReview? review, ReviewRepository repo, CoverImageService coverService)
    {
        _repo = repo;
        _coverService = coverService;
        _isNew = review is null;
        Review = review is null
            ? new GameReview { GameId = gameId, Rating = 5.0 }
            : new GameReview
            {
                Id = review.Id,
                GameId = review.GameId,
                Title = review.Title,
                Rating = review.Rating,
                Comment = review.Comment,
                CoverPath = review.CoverPath,
                ThumbOffsetX = review.ThumbOffsetX,
                ThumbOffsetY = review.ThumbOffsetY,
                CreatedAt = review.CreatedAt,
                UpdatedAt = review.UpdatedAt,
            };
        _originalCoverPath = Review.CoverPath;
        _coverPreviewPath = coverService.GetFullPath(Review.CoverPath);

        PickCoverCommand = new RelayCommand(PickCover);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
    }

    private void PickCover()
    {
        var name = _coverService.PickAndCopy();
        if (name is null)
            return;

        Review.CoverPath = name;
        Review.ThumbOffsetX = 0.5;
        Review.ThumbOffsetY = 0.5;
        CoverPreviewPath = _coverService.GetFullPath(name);
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Review.Title))
        {
            MessageBox.Show("请输入部分名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_originalCoverPath is not null && Review.CoverPath != _originalCoverPath)
            _coverService.Delete(_originalCoverPath);

        if (_isNew)
            _repo.Add(Review);
        else
            _repo.Update(Review);

        RequestClose?.Invoke(true);
    }

    private void Cancel()
    {
        if (Review.CoverPath is not null && Review.CoverPath != _originalCoverPath)
            _coverService.Delete(Review.CoverPath);

        RequestClose?.Invoke(false);
    }
}
