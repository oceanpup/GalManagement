using System.IO;
using Microsoft.Win32;

namespace GalManagement.Services;

/// <summary>游戏封面的选择与复制管理。</summary>
public class CoverImageService
{
    private readonly string _coversDir;

    public CoverImageService(string coversDirectory) => _coversDir = coversDirectory;

    /// <summary>弹窗选择图片并复制到封面目录,返回文件名;取消返回 null。</summary>
    public string? PickAndCopy()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择游戏封面",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp|所有文件|*.*",
        };
        return dialog.ShowDialog() == true ? Copy(dialog.FileName) : null;
    }

    public string? Copy(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrEmpty(ext))
            ext = ".jpg";
        var fileName = Guid.NewGuid().ToString("N") + ext;
        File.Copy(sourcePath, Path.Combine(_coversDir, fileName), true);
        return fileName;
    }

    public void Delete(string? coverPath)
    {
        if (string.IsNullOrWhiteSpace(coverPath))
            return;
        var full = Path.Combine(_coversDir, Path.GetFileName(coverPath));
        if (File.Exists(full))
            File.Delete(full);
    }

    public string? GetFullPath(string? coverPath)
    {
        if (string.IsNullOrWhiteSpace(coverPath))
            return null;
        var full = Path.Combine(_coversDir, Path.GetFileName(coverPath));
        return File.Exists(full) ? full : null;
    }
}
