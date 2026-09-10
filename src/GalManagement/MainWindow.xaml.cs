using System.Windows;
using System.Windows.Input;
using GalManagement.Data;
using GalManagement.Models;
using GalManagement.Services;
using GalManagement.ViewModels;
using GalManagement.Views;

namespace GalManagement;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly GameRepository _repo;
    private readonly ReviewRepository _reviewRepo;
    private readonly TagRepository _tagRepo;
    private readonly CoverImageService _coverService;
    private readonly GameLauncherService _launcher;

    public MainWindow(MainViewModel vm, GameRepository repo, ReviewRepository reviewRepo, TagRepository tagRepo, CoverImageService coverService, GameLauncherService launcher)
    {
        InitializeComponent();
        _vm = vm;
        _repo = repo;
        _reviewRepo = reviewRepo;
        _tagRepo = tagRepo;
        _coverService = coverService;
        _launcher = launcher;
        DataContext = vm;
        vm.Library.EditRequested += OnEditRequested;
        vm.Library.DetailRequested += OnDetailRequested;
    }

    private void OnEditRequested(Game? game)
    {
        var editVm = new GameEditViewModel(game, _repo, _tagRepo, _coverService, _launcher);
        var dialog = new GameEditDialog(editVm) { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            _vm.Library.Reload();
            _vm.Stats.Refresh();
        }
    }

    private void OnDetailRequested(Game game)
    {
        var detailVm = new GameDetailViewModel(game, _reviewRepo, _coverService);
        var view = new GameDetailView(detailVm, _reviewRepo, _coverService) { Owner = this };

        view.ShowDialog();

        _vm.Library.Reload();
        _vm.Stats.Refresh();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            ToggleMaximize();
        else if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
