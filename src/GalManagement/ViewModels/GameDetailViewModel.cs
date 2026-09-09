using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalManagement.Data;
using GalManagement.Models;
using GalManagement.Services;

namespace GalManagement.ViewModels;

public partial class GameDetailViewModel : ObservableObject
{
    private readonly ReviewRepository _reviewRepo;
    private readonly CoverImageService _coverService;

    public Game Game { get; }

    public ObservableCollection<GameReview> Reviews { get; } = new();

    [ObservableProperty]
    private double _overallRating;

    [ObservableProperty]
    private bool _hasReviews;

    public RelayCommand AddReviewCommand { get; }
    public RelayCommand<GameReview> EditReviewCommand { get; }
    public RelayCommand<GameReview> DeleteReviewCommand { get; }

    public event Action<GameReview?>? ReviewEditRequested;

    public GameDetailViewModel(Game game, ReviewRepository reviewRepo, CoverImageService coverService)
    {
        Game = game;
        _reviewRepo = reviewRepo;
        _coverService = coverService;

        AddReviewCommand = new RelayCommand(() => ReviewEditRequested?.Invoke(null));
        EditReviewCommand = new RelayCommand<GameReview>(r => ReviewEditRequested?.Invoke(r));
        DeleteReviewCommand = new RelayCommand<GameReview>(DeleteReview);

        Reload();
    }

    public void Reload()
    {
        Reviews.Clear();
        foreach (var r in _reviewRepo.GetByGame(Game.Id))
        {
            r.CoverFullPath = _coverService.GetFullPath(r.CoverPath);
            Reviews.Add(r);
        }

        HasReviews = Reviews.Count > 0;
        OverallRating = HasReviews ? Reviews.Average(r => r.Rating) : Game.Rating;
    }

    private void DeleteReview(GameReview? review)
    {
        if (review is null)
            return;

        var result = MessageBox.Show(
            $"确定要删除对「{review.Title}」的评价吗?",
            "删除确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        _coverService.Delete(review.CoverPath);
        _reviewRepo.Delete(review.Id);
        Reload();
    }
}
