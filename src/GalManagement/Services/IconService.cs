using System.IO;
using Microsoft.Win32;

namespace GalManagement.Services;

/// <summary>应用图标的选取与复制管理(图标保存在 data\icons\)。</summary>
public class IconService
{
    private readonly string _iconsDir;

    public IconService(string iconsDirectory) => _iconsDir = iconsDirectory;

    /// <summary>弹窗选择图片并复制到图标目录,返回文件名;取消返回 null。</summary>
    public string? PickAndCopy()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择应用图标",
            Filter = "图片文件|*.png;*.ico;*.jpg;*.jpeg;*.bmp|所有文件|*.*",
        };
        if (dialog.ShowDialog() != true)
            return null;

        var ext = Path.GetExtension(dialog.FileName);
        if (string.IsNullOrEmpty(ext))
            ext = ".png";
        var fileName = Guid.NewGuid().ToString("N") + ext;
        File.Copy(dialog.FileName, Path.Combine(_iconsDir, fileName), true);
        return fileName;
    }

    public void Delete(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return;
        var full = Path.Combine(_iconsDir, Path.GetFileName(fileName));
        if (File.Exists(full))
            File.Delete(full);
    }

    public string? GetFullPath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        var full = Path.Combine(_iconsDir, Path.GetFileName(fileName));
        return File.Exists(full) ? full : null;
    }
}
