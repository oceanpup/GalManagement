using System.IO;
using Microsoft.Win32;

namespace GalManagement.Services;

/// <summary>窗口背景图的选择、切换与持久化。</summary>
public class BackgroundService
{
    private readonly string _backgroundsDir;
    private readonly SettingsService _settings;

    public BackgroundService(string backgroundsDirectory, SettingsService settings)
    {
        _backgroundsDir = backgroundsDirectory;
        _settings = settings;
    }

    /// <summary>当前背景图的完整路径;未设置返回 null。</summary>
    public string? CurrentBackgroundPath
    {
        get
        {
            var name = _settings.Current.BackgroundFileName;
            if (string.IsNullOrWhiteSpace(name))
                return null;
            var full = Path.Combine(_backgroundsDir, Path.GetFileName(name));
            return File.Exists(full) ? full : null;
        }
    }

    /// <summary>弹窗选择图片并设为背景,返回新路径;取消返回 null。</summary>
    public string? PickAndSet()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择背景图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.webp|所有文件|*.*",
        };
        return dialog.ShowDialog() == true ? Set(dialog.FileName) : null;
    }

    public string? Set(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrEmpty(ext))
            ext = ".jpg";
        var fileName = "bg" + Guid.NewGuid().ToString("N") + ext;
        File.Copy(sourcePath, Path.Combine(_backgroundsDir, fileName), true);

        var old = _settings.Current.BackgroundFileName;
        _settings.Current.BackgroundFileName = fileName;
        _settings.Save();

        DeleteOld(old);
        return Path.Combine(_backgroundsDir, fileName);
    }

    public void ResetToDefault()
    {
        var old = _settings.Current.BackgroundFileName;
        _settings.Current.BackgroundFileName = null;
        _settings.Save();
        DeleteOld(old);
    }

    private void DeleteOld(string? old)
    {
        if (string.IsNullOrWhiteSpace(old))
            return;
        var oldPath = Path.Combine(_backgroundsDir, Path.GetFileName(old));
        if (File.Exists(oldPath))
            File.Delete(oldPath);
    }
}
