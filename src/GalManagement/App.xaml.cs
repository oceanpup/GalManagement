using System.Windows;
using GalManagement.Data;
using GalManagement.Services;
using GalManagement.ViewModels;

namespace GalManagement;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var db = new Database();
        var repo = new GameRepository(db);
        var reviewRepo = new ReviewRepository(db);
        var coverService = new CoverImageService(db.CoversDirectory);
        var settings = new SettingsService(db.DataDirectory);
        var bgService = new BackgroundService(db.BackgroundsDirectory, settings);
        var themeService = new ThemeService();
        var launcher = new GameLauncherService();
        var iconService = new IconService(db.IconsDirectory);
        var updater = new UpdateService(db.DataDirectory);
        themeService.Apply(settings.Current.Theme);
        updater.InitCleanup();

        var mainVm = new MainViewModel(repo, coverService, bgService, themeService, settings, launcher, iconService, updater);
        var window = new MainWindow(mainVm, repo, reviewRepo, coverService, launcher);

        MainWindow = window;
        window.Show();
    }
}
