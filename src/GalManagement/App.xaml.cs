using System.Windows;
using GalManagement.Data;
using GalManagement.Services;
using GalManagement.ViewModels;

namespace GalManagement;

public partial class App : Application
{
    private PlayTimeTracker? _tracker;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var db = new Database();
        var repo = new GameRepository(db);
        var reviewRepo = new ReviewRepository(db);
        var tagRepo = new TagRepository(db);
        var coverService = new CoverImageService(db.CoversDirectory);
        var settings = new SettingsService(db.DataDirectory);
        var bgService = new BackgroundService(db.BackgroundsDirectory, settings);
        var themeService = new ThemeService();
        var launcher = new GameLauncherService();
        var tracker = new PlayTimeTracker(repo, launcher);
        var iconService = new IconService(db.IconsDirectory);
        var updater = new UpdateService(db.DataDirectory);
        var cloudSave = new CloudSaveService(settings, db.DataDirectory);
        themeService.Apply(settings.Current.Theme);
        updater.InitCleanup();
        _tracker = tracker;

        var mainVm = new MainViewModel(repo, tagRepo, coverService, bgService, themeService, settings, launcher, tracker, iconService, updater, cloudSave);
        var window = new MainWindow(mainVm, repo, reviewRepo, tagRepo, coverService, launcher, tracker, cloudSave);

        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 关闭应用时把仍在计时的会话结算落库,避免丢已玩时长
        _tracker?.CommitAll();
        base.OnExit(e);
    }
}
