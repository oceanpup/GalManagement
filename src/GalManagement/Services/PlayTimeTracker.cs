using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using GalManagement.Data;
using GalManagement.Models;

namespace GalManagement.Services;

/// <summary>
/// 通过监视由本应用启动的进程来累计游玩时长;时长并入 Game.PlayTimeHours(手填值即基值,监测值往上加)。
/// 启动的进程若很快退出(游戏自我重启、或引导壳把真正的游戏托起来),会在短时间内**接续**新起的进程继续计时:
/// 先找同名进程,再退一步找同一目录下新起的进程(汉化引导壳常把真正游戏另起一个名字)。
/// </summary>
public sealed class PlayTimeTracker
{
    /// <summary>上一段短于该秒数才算"早退",才尝试接续。</summary>
    private const double ShortHopSeconds = 45;
    /// <summary>启动后多久内允许接续。</summary>
    private const double AdoptWindowMinutes = 10;
    /// <summary>最多接续几次,防止连环套娃。</summary>
    private const int MaxHops = 3;
    /// <summary>等新进程出现的最长时间(交接通常是瞬时的)。</summary>
    private const double AdoptWaitSeconds = 20;

    /// <summary>这些是我们特意用来自跑脚本的宿主,它们退出即脚本结束,不属于交接。</summary>
    private static readonly HashSet<string> ScriptHosts =
        new(StringComparer.OrdinalIgnoreCase) { "cmd", "wscript", "cscript", "powershell", "pwsh" };

    private readonly GameRepository _repo;
    private readonly GameLauncherService _launcher;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<int, Session> _sessions = new();

    private sealed class Session
    {
        public int GameId { get; init; }
        public string ExeName { get; init; } = string.Empty;
        /// <summary>被监测程序所在目录;接续时用来在"引导壳退出、另一个程序接着跑"的场景里找人。</summary>
        public string LaunchDirectory { get; init; } = string.Empty;
        public DateTime LaunchedUtc { get; init; }
        public Process? Process { get; set; }
        public int Hops { get; set; }
        public DateTime? AdoptDeadlineUtc { get; set; }
        /// <summary>下次允许扫描同目录进程的时间(全进程枚举较重,按秒节流)。</summary>
        public DateTime NextDirProbeUtc { get; set; }

        /// <summary>游玩时长的基值(手填值,Rebase 改它)。</summary>
        public double PlayBaseHours { get; set; }
        public DateTime PlayBaselineUtc { get; set; }

        /// <summary>本次会话累计运行时长(仅供界面显示,不受 Rebase 影响)。</summary>
        public double RunHours { get; set; }
        public DateTime RunBaselineUtc { get; set; }

        /// <summary>正在等待接续下一个进程(此期间不计时)。</summary>
        public bool Waiting => Process is null;

        public double CurrentTotal() => PlayBaseHours + (Waiting ? 0 : (DateTime.UtcNow - PlayBaselineUtc).TotalHours);

        public double Elapsed() => RunHours + (Waiting ? 0 : (DateTime.UtcNow - RunBaselineUtc).TotalHours);
    }

    /// <summary>每秒触发一次(仅在有时长会话时),由界面据此刷新显示。</summary>
    public event Action? Tick;

    /// <summary>会话结束:gameId 与已落库的最终总时长。</summary>
    public event Action<int, double>? SessionEnded;

    public PlayTimeTracker(GameRepository repo, GameLauncherService launcher)
    {
        _repo = repo;
        _launcher = launcher;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick();
    }

    public bool IsRunning(int gameId) => _sessions.ContainsKey(gameId);

    /// <summary>当前总时长(基值 + 本次已玩);未在计时返回 null。</summary>
    public double? CurrentTotal(int gameId) =>
        _sessions.TryGetValue(gameId, out var s) ? s.CurrentTotal() : null;

    /// <summary>本次会话已运行的时间;未在计时返回 0。</summary>
    public TimeSpan Elapsed(int gameId) =>
        _sessions.TryGetValue(gameId, out var s) ? TimeSpan.FromHours(s.Elapsed()) : TimeSpan.Zero;

    /// <summary>启动游戏并在拿到进程句柄时开始计时;返回需要提示给用户的信息,null 表示正在正常计时。</summary>
    public string? Start(Game game)
    {
        if (_sessions.ContainsKey(game.Id))
            return "该游戏已在计时中。";

        var outcome = _launcher.Launch(game.LaunchPath);
        if (outcome.Process is null)
            return outcome.Message;

        var now = DateTime.UtcNow;
        var session = new Session
        {
            GameId = game.Id,
            ExeName = SafeProcessName(outcome.Process),
            LaunchDirectory = NormalizeDirectory(outcome.Directory ?? Path.GetDirectoryName(game.LaunchPath)),
            LaunchedUtc = now,
            PlayBaseHours = game.PlayTimeHours ?? 0,
            PlayBaselineUtc = now,
            RunBaselineUtc = now,
        };
        _sessions[game.Id] = session;

        Attach(session, outcome.Process);
        _timer.Start();
        return null;
    }

    /// <summary>用户手动改了时长:以新值为基值,从此刻继续累加(不影响"本次已运行"的显示)。</summary>
    public void Rebase(int gameId, double totalHours)
    {
        if (!_sessions.TryGetValue(gameId, out var session))
            return;

        session.PlayBaseHours = totalHours;
        session.PlayBaselineUtc = DateTime.UtcNow;
    }

    /// <summary>中止计时(如删除游戏),不落库。</summary>
    public void Stop(int gameId)
    {
        if (_sessions.Remove(gameId) && _sessions.Count == 0)
            _timer.Stop();
    }

    /// <summary>退出应用时把进行中的会话结算落库。</summary>
    public void CommitAll()
    {
        foreach (var (id, session) in _sessions.ToList())
            _repo.UpdatePlayTimeHours(id, Math.Round(session.CurrentTotal(), 2));

        _sessions.Clear();
        _timer.Stop();
    }

    private void Attach(Session session, Process process)
    {
        session.Process = process;
        session.AdoptDeadlineUtc = null;
        session.PlayBaselineUtc = DateTime.UtcNow;
        session.RunBaselineUtc = session.PlayBaselineUtc;

        try
        {
            process.Exited += (_, _) => OnHopExited(session);
            process.EnableRaisingEvents = true;
            if (process.HasExited)      // 极短命的进程可能在订阅前就退出,补一次检查
                OnHopExited(session);
        }
        catch
        {
            End(session);
        }
    }

    private void OnTick()
    {
        foreach (var session in _sessions.Values.ToList())
        {
            if (!session.Waiting)
                continue;

            if (session.AdoptDeadlineUtc is DateTime deadline && DateTime.UtcNow <= deadline)
                TryAdopt(session);
            else
                End(session);       // 等不到接续的进程,按已累计的时长结算
        }

        Tick?.Invoke();
    }

    // 刻意不调用 Process.Dispose():Exited 回调线程会被下面的 Invoke 阻塞,
    // 此时在 UI 线程上释放同一个 Process 会死锁,交给 GC 回收即可。
    private void OnHopExited(Session session)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            return;

        if (_dispatcher.CheckAccess())
            HopEnded(session);
        else
            _dispatcher.Invoke(() => HopEnded(session));
    }

    private void HopEnded(Session session)
    {
        if (session.Process is null)
            return;

        if (!_sessions.TryGetValue(session.GameId, out var current) || !ReferenceEquals(current, session))
            return;

        session.Process = null;

        var now = DateTime.UtcNow;
        var hopHours = (now - session.PlayBaselineUtc).TotalHours;
        session.PlayBaseHours += hopHours;
        session.RunHours += hopHours;
        session.PlayBaselineUtc = now;
        session.RunBaselineUtc = now;

        var hopSeconds = hopHours * 3600;

        // 早退 → 多半是自我重启或引导壳转交,短时间内接续新进程继续计时
        if (hopSeconds < ShortHopSeconds
            && !ScriptHosts.Contains(session.ExeName)
            && session.Hops < MaxHops
            && (now - session.LaunchedUtc).TotalMinutes < AdoptWindowMinutes)
        {
            session.Hops++;
            session.AdoptDeadlineUtc = now.AddSeconds(AdoptWaitSeconds);
            TryAdopt(session);
            return;
        }

        End(session);
    }

    /// <summary>先找同名新进程(自我重启),再退一步找同目录里新起的进程(引导壳把真正游戏托起来的常见情形)。</summary>
    private void TryAdopt(Session session)
    {
        if (session.Process is not null)
            return;

        var sameName = FindByName(session);
        if (sameName is not null)
        {
            Attach(session, sameName);
            return;
        }

        // 全进程枚举较重,按秒节流,避免每个 tick 都扫一遍拖卡界面
        if (DateTime.UtcNow < session.NextDirProbeUtc)
            return;
        session.NextDirProbeUtc = DateTime.UtcNow.AddSeconds(2);

        var sameDir = FindByDirectory(session);
        if (sameDir is not null)
            Attach(session, sameDir);
    }

    private Process? FindByName(Session session)
    {
        if (session.ExeName.Length == 0)
            return null;

        foreach (var candidate in Process.GetProcessesByName(session.ExeName))
        {
            if (candidate.Id != Environment.ProcessId && StartedAfter(candidate, session.LaunchedUtc))
                return candidate;

            candidate.Dispose();
        }

        return null;
    }

    private Process? FindByDirectory(Session session)
    {
        if (session.LaunchDirectory.Length == 0)
            return null;

        Process? best = null;
        var bestStarted = DateTime.MinValue;

        foreach (var candidate in Process.GetProcesses())
        {
            bool match;
            DateTime started;
            try
            {
                match = candidate.Id != Environment.ProcessId
                    && StartedAfter(candidate, session.LaunchedUtc, 0.5)   // 同目录放宽得更紧,少认错人
                    && string.Equals(
                        NormalizeDirectory(Path.GetDirectoryName(candidate.MainModule?.FileName)),
                        session.LaunchDirectory, StringComparison.OrdinalIgnoreCase);
                started = match ? candidate.StartTime : default;
            }
            catch
            {
                match = false;      // 系统/提权进程读不到路径,跳过
                started = default;
            }

            if (match && started > bestStarted)
            {
                best?.Dispose();
                best = candidate;
                bestStarted = started;
            }
            else
            {
                candidate.Dispose();
            }
        }

        return best;
    }

    private static string NormalizeDirectory(string? directory) =>
        string.IsNullOrWhiteSpace(directory) ? string.Empty : directory.TrimEnd('\\', '/');

    private static bool StartedAfter(Process process, DateTime sinceUtc, double leewaySeconds = 2)
    {
        try
        {
            return process.StartTime.ToUniversalTime() >= sinceUtc.AddSeconds(-leewaySeconds);
        }
        catch
        {
            return false;
        }
    }

    private void End(Session session)
    {
        if (!_sessions.Remove(session.GameId))
            return;

        if (_sessions.Count == 0)
            _timer.Stop();

        var total = Math.Round(session.CurrentTotal(), 2);
        _repo.UpdatePlayTimeHours(session.GameId, total);
        SessionEnded?.Invoke(session.GameId, total);
    }

    private static string SafeProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
