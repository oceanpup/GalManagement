using System.Windows;
using GalManagement.ViewModels;

namespace GalManagement.Views;

public partial class BaiduAccountDialog : Window
{
    public BaiduAccountDialog(BaiduAccountViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose += result => DialogResult = result;
    }
}
