using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private Point _dragStart;
    private GameReview? _dragged;
    private bool _moved;

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

    // 只有左侧手柄能发起拖动,行内按钮不会被误触
    private void Handle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragged = (sender as FrameworkElement)?.DataContext as GameReview;
    }

    private void Handle_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragged is null)
            return;

        // 超过系统拖动阈值才算拖动,否则会被当成一次点击
        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var dragged = _dragged;
        _dragged = null;
        _moved = false;
        dragged.IsDragging = true;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, dragged, DragDropEffects.Move);
        }
        finally
        {
            dragged.IsDragging = false;

            if (_moved)         // 只有真的挪动过才写库
                _vm.PersistOrder();
        }
    }

    private void ReviewList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(GameReview)) is not GameReview dragged)
            return;

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        var from = _vm.Reviews.IndexOf(dragged);
        if (from < 0)
            return;

        var to = TargetIndex(e);
        if (from == to)
            return;

        _vm.MoveReview(from, to);       // 实时预览:拖到哪一行就挪到哪一行
        _moved = true;
    }

    private void ReviewList_Drop(object sender, DragEventArgs e) => e.Handled = true;

    /// <summary>鼠标下的行下标;落在列表空白处则视为最后一行。</summary>
    private int TargetIndex(DragEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(ReviewList, (DependencyObject)e.OriginalSource) as ListBoxItem;
        return item is null ? _vm.Reviews.Count - 1 : ReviewList.ItemContainerGenerator.IndexFromContainer(item);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
