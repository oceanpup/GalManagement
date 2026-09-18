using System.Windows;

namespace GalManagement.Views;

/// <summary>
/// 云存档使用说明。纯静态文案,没有任何数据绑定,所以不配 ViewModel —— 开窗的代价只是这一句 DialogResult。
/// </summary>
public partial class CloudSaveHelpDialog : Window
{
    public CloudSaveHelpDialog()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
