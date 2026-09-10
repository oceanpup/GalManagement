using System.Windows;
using System.Windows.Input;
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

    /// <summary>标签输入框回车即添加(InputBindings 不继承 DataContext,故走 code-behind)。</summary>
    private void TagInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (DataContext is GameEditViewModel vm && vm.AddTagCommand.CanExecute(null))
            vm.AddTagCommand.Execute(null);

        e.Handled = true;
    }
}
