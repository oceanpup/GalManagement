using System.Windows.Controls;
using System.Windows.Input;
using GalManagement.ViewModels;

namespace GalManagement.Views;

public partial class GameLibraryView : UserControl
{
    public GameLibraryView()
    {
        InitializeComponent();
    }

    private void GameList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is GameLibraryViewModel vm)
            vm.OpenDetail();
    }
}
