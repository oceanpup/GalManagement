using System.Windows;
using GalManagement.ViewModels;

namespace GalManagement.Views;

public partial class SaveSyncDialog : Window
{
    public SaveSyncDialog(SaveSyncViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose += () => DialogResult = true;
    }
}
