using System.Windows;
using GalManagement.ViewModels;

namespace GalManagement.Views;

public partial class GameReviewDialog : Window
{
    public GameReviewDialog(GameReviewViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose += result => DialogResult = result;
    }
}
