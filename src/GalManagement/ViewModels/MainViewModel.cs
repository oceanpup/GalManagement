using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalManagement.Data;
using GalManagement.Models;
using GalManagement.Services;

namespace GalManagement.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly BackgroundService _bgService;
    private readonly ThemeService _themeService;
    private readonly SettingsService _settingsService;
    private readonly IconService _iconService;

    public GameLibraryViewModel Library { get; }
    public StatsViewModel Stats { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>共享设置实例(标题栏等直接绑定此对象)。</summary>
    public AppSettings AppSettings => _settingsService.Current;

    [ObservableProperty]
    private object _currentView;

    [ObservableProperty]
    private string? _backgroundPath;

    /// <summary>应用图标的完整路径(标题栏与任务栏显示用)。</summary>
    [ObservableProperty]
    private string? _iconFullPath;

    [ObservableProperty]
    private bool _isDarkTheme;

    public RelayCommand ChangeBackgroundCommand { get; }
    public RelayCommand ResetBackgroundCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public RelayCommand ShowLibraryCommand { get; }
    public RelayCommand ShowStatsCommand { get; }
    public RelayCommand ShowSettingsCommand { get; }

    public MainViewModel(
        GameRepository repo,
        CoverImageService coverService,
        BackgroundService bgService,
        ThemeService themeService,
        SettingsService settingsService,
        GameLauncherService launcher,
        IconService iconService,
        UpdateService updateService)
    {
        _bgService = bgService;
        _themeService = themeService;
        _settingsService = settingsService;
        _iconService = iconService;

        Library = new GameLibraryViewModel(repo, coverService, launcher);
        Stats = new StatsViewModel(repo);
        Settings = new SettingsViewModel(settingsService.Current, settingsService, iconService, updateService);

        _currentView = Library;
        _backgroundPath = bgService.CurrentBackgroundPath;
        _iconFullPath = iconService.GetFullPath(settingsService.Current.IconFileName);
        _isDarkTheme = settingsService.Current.Theme != "Light";

        settingsService.Current.PropertyChanged += OnAppSettingsChanged;

        ChangeBackgroundCommand = new RelayCommand(ChangeBackground);
        ResetBackgroundCommand = new RelayCommand(ResetBackground);
        ToggleThemeCommand = new RelayCommand(() => IsDarkTheme = !IsDarkTheme);
        ShowLibraryCommand = new RelayCommand(() => CurrentView = Library);
        ShowStatsCommand = new RelayCommand(() =>
        {
            Stats.Refresh();
            CurrentView = Stats;
        });
        ShowSettingsCommand = new RelayCommand(() => CurrentView = Settings);
    }

    private void ChangeBackground()
    {
        var path = _bgService.PickAndSet();
        if (path is not null)
            BackgroundPath = path;
    }

    private void ResetBackground()
    {
        _bgService.ResetToDefault();
        BackgroundPath = null;
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        var theme = value ? "Dark" : "Light";
        _themeService.Apply(theme);
        _settingsService.Current.Theme = theme;
        _settingsService.Save();
    }

    private void OnAppSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.IconFileName))
            IconFullPath = _iconService.GetFullPath(_settingsService.Current.IconFileName);
    }
}
