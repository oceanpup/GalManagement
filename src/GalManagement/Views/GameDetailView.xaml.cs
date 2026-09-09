using System.Windows;
using GalManagement.Data;
using GalManagement.Models;
using GalManagement.Services;
using GalManagement.ViewModels;

namespace GalManagement.Views;

public partial class GameDetailView : Window
{
    private readonly GameDetailViewModel _vm;
    private readonly ReviewRepository _reviewRepo;
    private readonly CoverImageService _coverService;

    public GameDetailView(GameDetailViewModel vm, ReviewRepository reviewRepo, CoverImageService coverService)
    {
        InitializeComponent();
        _vm = vm;
        _reviewRepo = reviewRepo;
        _coverService = coverService;
        DataContext = vm;
        vm.ReviewEditRequested += OnReviewEditRequested;
    }

    private void OnReviewEditRequested(GameReview? review)
    {
        var dialogVm = new GameReviewViewModel(_vm.Game.Id, review, _reviewRepo, _coverService);
        var dialog = new GameReviewDialog(dialogVm) { Owner = this };

        if (dialog.ShowDialog() == true)
            _vm.Reload();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
