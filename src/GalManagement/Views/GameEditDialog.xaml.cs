using System.Windows;
using GalManagement.ViewModels;

namespace GalManagement.Views;

public partial class GameEditDialog : Window
{
    public GameEditDialog(GameEditViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose += result => DialogResult = result;
    }
}
