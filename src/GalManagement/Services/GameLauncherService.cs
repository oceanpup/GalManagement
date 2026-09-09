using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace GalManagement.Services;

/// <summary>游戏启动程序的选取与启动。</summary>
public class GameLauncherService
{
    /// <summary>弹窗选择启动程序路径,仅记录路径不复制文件;取消返回 null。</summary>
    public string? PickExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择游戏启动程序",
            Filter = "程序文件|*.exe;*.lnk;*.bat;*.cmd|所有文件|*.*",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>启动指定路径;返回 null 表示成功,否则返回错误信息。</summary>
    public string? Launch(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "尚未设置该游戏的启动路径,可在编辑游戏时指定。";

        if (!File.Exists(path))
            return $"启动路径不存在:\n{path}";

        try
        {
            // 工作目录必须设为程序所在目录,否则依赖相对路径资源的游戏/启动器会找不到文件
            var psi = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
            };
            Process.Start(psi);
            return null;
        }
        catch (Exception ex)
        {
            return $"启动失败:{ex.Message}";
        }
    }
}
