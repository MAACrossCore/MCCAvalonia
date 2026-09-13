using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Other;

/// <summary>
/// Mirrors MFA timers into Windows Task Scheduler so they still fire while MFA is closed.
/// The registered task starts MFA with its existing single-instance/autostart command line.
/// </summary>
internal static class SystemScheduledTaskManager
{
    internal sealed record TimerDefinition(
        int TimerId,
        bool IsEnabled,
        TimeSpan Time,
        string Schedule,
        string InstanceId);

    private static readonly SemaphoreSlim SyncGate = new(1, 1);
    private static long _requestedRevision;

    private static readonly string InstallId = Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Path.GetFullPath(AppContext.BaseDirectory).ToUpperInvariant())))[..12];

    private static string TaskPrefix => $"MFA-LAA-{InstallId}-Timer-";

    public static async Task ReplaceAllAsync(IReadOnlyCollection<TimerDefinition> definitions)
    {
        if (!OperatingSystem.IsWindows()) return;

        var revision = Interlocked.Increment(ref _requestedRevision);
        await SyncGate.WaitAsync();
        try
        {
            // Rapid UI changes are coalesced; the newest snapshot will run after this call.
            if (revision != Volatile.Read(ref _requestedRevision)) return;
            await Task.Run(() => ReplaceAllCore(definitions));
        }
        catch (Exception ex)
        {
            LoggerHelper.Warning($"同步 Windows 定时任务失败：{ex.Message}");
        }
        finally
        {
            SyncGate.Release();
        }
    }

    private static void ReplaceAllCore(IReadOnlyCollection<TimerDefinition> definitions)
    {
        var serviceType = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new PlatformNotSupportedException("Windows Task Scheduler service is unavailable.");
        dynamic service = Activator.CreateInstance(serviceType)
            ?? throw new InvalidOperationException("Unable to create Windows Task Scheduler service.");
        service.Connect();
        dynamic root = service.GetFolder("\\");

        var enabled = definitions
            .Where(IsValid)
            .ToDictionary(item => TaskName(item.TimerId));

        foreach (dynamic registeredTask in root.GetTasks(1))
        {
            string name = registeredTask.Name;
            if (name.StartsWith(TaskPrefix, StringComparison.OrdinalIgnoreCase)
                && !enabled.ContainsKey(name))
            {
                root.DeleteTask(name, 0);
            }
        }

        foreach (var pair in enabled)
            RegisterOrUpdate(root, service, pair.Key, pair.Value);
    }

    private static bool IsValid(TimerDefinition timer)
    {
        if (!timer.IsEnabled || string.IsNullOrWhiteSpace(timer.InstanceId)) return false;
        var schedule = new TimerScheduleConfig(timer.Schedule);
        return schedule.ScheduleType switch
        {
            TimerScheduleType.Daily => true,
            TimerScheduleType.Weekly => schedule.SelectedDaysOfWeek.Count > 0,
            TimerScheduleType.Monthly => schedule.SelectedDaysOfMonth.Count > 0,
            _ => false
        };
    }

    private static void RegisterOrUpdate(dynamic root, dynamic service, string name, TimerDefinition timer)
    {
        var schedule = new TimerScheduleConfig(timer.Schedule);
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "MFAA 洛瑟兰战境自动任务";
        definition.Principal.LogonType = 3; // TASK_LOGON_INTERACTIVE_TOKEN
        definition.Principal.RunLevel = 0; // Least privilege

        definition.Settings.Enabled = true;
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.WakeToRun = true;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.MultipleInstances = 2; // Ignore a duplicate trigger
        definition.Settings.ExecutionTimeLimit = "PT0S";

        var triggerType = schedule.ScheduleType switch
        {
            TimerScheduleType.Daily => 2,
            TimerScheduleType.Weekly => 3,
            TimerScheduleType.Monthly => 4,
            _ => 2
        };
        dynamic trigger = definition.Triggers.Create(triggerType);
        trigger.Enabled = true;
        trigger.StartBoundary = DateTime.Today.Add(timer.Time).ToString("yyyy-MM-dd'T'HH:mm:ss");

        if (schedule.ScheduleType == TimerScheduleType.Daily)
        {
            trigger.DaysInterval = (short)1;
        }
        else if (schedule.ScheduleType == TimerScheduleType.Weekly)
        {
            trigger.WeeksInterval = (short)1;
            trigger.DaysOfWeek = (short)schedule.SelectedDaysOfWeek.Aggregate(
                0, (mask, day) => mask | (1 << (int)day));
        }
        else
        {
            trigger.MonthsOfYear = (short)0x0FFF;
            trigger.DaysOfMonth = schedule.SelectedDaysOfMonth.Aggregate(
                0, (mask, day) => mask | (1 << (day - 1)));
        }

        var executable = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "MFAAvalonia.exe");
        dynamic action = definition.Actions.Create(0); // TASK_ACTION_EXEC
        action.Path = executable;
        action.WorkingDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        action.Arguments = $"--autostart --instance \"{timer.InstanceId.Replace("\"", "\\\"")}\"";

        // TASK_CREATE_OR_UPDATE + interactive-token logon; no password is stored.
        root.RegisterTaskDefinition(name, definition, 6, null, null, 3, null);
    }

    private static string TaskName(int timerId) => $"{TaskPrefix}{timerId + 1}";
}

