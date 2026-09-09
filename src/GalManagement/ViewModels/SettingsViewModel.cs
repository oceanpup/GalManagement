using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalManagement.Models;
using GalManagement.Services;

namespace GalManagement.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly SettingsService _settingsService;
    private readonly IconService _iconService;

    [ObservableProperty]
    private string _appTitle;

    [ObservableProperty]
    private string? _iconPreviewPath;

    public RelayCommand PickIconCommand { get; }
    public RelayCommand ResetIconCommand { get; }

    public SettingsViewModel(AppSettings settings, SettingsService settingsService, IconService iconService)
    {
        _settings = settings;
        _settingsService = settingsService;
        _iconService = iconService;
        _appTitle = settings.AppTitle;
        _iconPreviewPath = iconService.GetFullPath(settings.IconFileName);

        PickIconCommand = new RelayCommand(PickIcon);
        ResetIconCommand = new RelayCommand(ResetIcon, () => settings.IconFileName is not null);
    }

    partial void OnAppTitleChanged(string value)
    {
        if (_settings.AppTitle == value)
            return;
        _settings.AppTitle = value;
        _settingsService.Save();
    }

    private void PickIcon()
    {
        var name = _iconService.PickAndCopy();
        if (name is null)
            return;

        var old = _settings.IconFileName;
        _settings.IconFileName = name;
        _settingsService.Save();
        _iconService.Delete(old);
        IconPreviewPath = _iconService.GetFullPath(name);
        ResetIconCommand.NotifyCanExecuteChanged();
    }

    private void ResetIcon()
    {
        var old = _settings.IconFileName;
        if (old is null)
            return;

        _settings.IconFileName = null;
        _settingsService.Save();
        _iconService.Delete(old);
        IconPreviewPath = null;
        ResetIconCommand.NotifyCanExecuteChanged();
    }
}
