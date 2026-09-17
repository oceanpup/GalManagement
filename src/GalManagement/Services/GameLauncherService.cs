using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace GalManagement.Services;

/// <summary>启动结果:拿到进程句柄表示可以计时;Message 为需要提示给用户的信息;Directory 为实际启动的工作目录。</summary>
public sealed record LaunchOutcome(Process? Process, string? Message, string? Directory = null);

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

    /// <summary>弹窗选择存档目录(云存档同步的本地端),仅记录路径不复制文件;取消返回 null。</summary>
    public string? PickSaveFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择该游戏的存档目录",
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <summary>启动指定路径;能拿到进程句柄时返回 Process 以便监视游玩时长。</summary>
    public LaunchOutcome Launch(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new LaunchOutcome(null, "尚未设置该游戏的启动路径,可在编辑游戏时指定。");

        if (!File.Exists(path))
            return new LaunchOutcome(null, $"启动路径不存在:\n{path}");

        var target = path;
        var arguments = string.Empty;
        var workDir = Path.GetDirectoryName(path) ?? string.Empty;

        // .lnk 必须先解析出真实目标:ShellExecute 启动拿不到游戏本体进程
        if (HasExtension(path, ".lnk"))
        {
            if (!TryResolveShortcut(path, out var lnkTarget, out var lnkArgs, out var lnkWorkDir)
                || string.IsNullOrWhiteSpace(lnkTarget) || !File.Exists(lnkTarget))
                return ShellLaunch(path, "无法解析该快捷方式的目标,已用普通方式启动,本次不计时。");

            target = lnkTarget;
            arguments = lnkArgs;
            workDir = string.IsNullOrWhiteSpace(lnkWorkDir) ? Path.GetDirectoryName(lnkTarget) ?? string.Empty : lnkWorkDir;
        }

        // 批处理无法被 CreateProcess 直接启动,交给 cmd.exe 托管(句柄即 cmd,批处理结束它就退出)
        string fileName;
        string fileArgs;
        if (HasExtension(target, ".bat") || HasExtension(target, ".cmd"))
        {
            fileName = "cmd.exe";
            fileArgs = string.IsNullOrWhiteSpace(arguments) ? $"/c \"{target}\"" : $"/c \"{target}\" {arguments}";
        }
        else
        {
            fileName = target;
            fileArgs = arguments;
        }

        var isCmdHost = fileName.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);

        // 同名进程已在运行:多半是启动器(如 Steam)或重复实例,盯它会一直计到它退出为止
        if (!isCmdHost)
        {
            var processName = Path.GetFileNameWithoutExtension(fileName);
            var running = processName.Length == 0 ? [] : Process.GetProcessesByName(processName);
            try
            {
                if (running.Length > 0)
                    return ShellLaunch(path, $"「{processName}」已在运行(可能是启动器或重复实例),已用普通方式启动,本次不计时。");
            }
            finally
            {
                foreach (var p in running)
                    p.Dispose();
            }
        }

        try
        {
            // 工作目录必须设为程序所在目录,否则依赖相对路径资源的游戏/启动器会找不到文件
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                WorkingDirectory = workDir,
                Arguments = fileArgs,
            };
            var process = Process.Start(psi);
            return process is null
                ? ShellLaunch(path, "已启动,但未能取得进程句柄,本次不计时。")
                : new LaunchOutcome(process, null, workDir);
        }
        catch (Exception ex)
        {
            // 需要管理员权限(错误码 740)或不是可执行文件等:退回 ShellExecute,保证原本能启动的仍能启动
            return ShellLaunch(path, $"已启动,但该程序无法被监测(如需要管理员权限),本次不计时。\n原因:{ex.Message}");
        }
    }

    /// <summary>用系统 shell 启动(不监测);启动失败时返回失败信息。</summary>
    private static LaunchOutcome ShellLaunch(string path, string message)
    {
        try
        {
            var psi = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
            };
            Process.Start(psi);
            return new LaunchOutcome(null, message);
        }
        catch (Exception ex)
        {
            return new LaunchOutcome(null, $"启动失败:{ex.Message}");
        }
    }

    /// <summary>解析 .lnk 的真实目标(WScript.Shell COM;走反射以免引入 dynamic 依赖)。</summary>
    private static bool TryResolveShortcut(string lnkPath, out string target, out string arguments, out string workDir)
    {
        target = string.Empty;
        arguments = string.Empty;
        workDir = string.Empty;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                return false;

            var shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return false;

            var link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [lnkPath]);
            if (link is null)
                return false;

            var linkType = link.GetType();
            target = linkType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null) as string ?? string.Empty;
            arguments = linkType.InvokeMember("Arguments", BindingFlags.GetProperty, null, link, null) as string ?? string.Empty;
            workDir = linkType.InvokeMember("WorkingDirectory", BindingFlags.GetProperty, null, link, null) as string ?? string.Empty;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasExtension(string path, string extension) =>
        Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase);
}
