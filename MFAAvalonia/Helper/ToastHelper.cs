using Avalonia.Controls.Notifications;
using SukiUI.Toasts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MFAAvalonia.Helper;

public static class ToastHelper
{
    /// <summary>
    /// 相同标题+内容的提示在窗口期内只弹一次。
    /// 启动时每个懒加载的实例都会入队一次连接任务，失败提示一模一样地重复弹出
    /// （用户反馈「打开程序弹出两条未连接模拟器」），这里统一收敛，避免刷屏。
    /// </summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(5);
    private static readonly Dictionary<string, DateTime> RecentToasts = new();

    private static bool IsDuplicate(string title, object? content)
    {
        var key = $"{title}\u0000{content}";
        var now = DateTime.UtcNow;
        lock (RecentToasts)
        {
            if (RecentToasts.Count > 64)
            {
                foreach (var stale in RecentToasts
                             .Where(pair => now - pair.Value > DuplicateWindow)
                             .Select(pair => pair.Key)
                             .ToList())
                {
                    RecentToasts.Remove(stale);
                }
            }

            if (RecentToasts.TryGetValue(key, out var last) && now - last < DuplicateWindow)
                return true;

            RecentToasts[key] = now;
            return false;
        }
    }
    public static SukiToastBuilder CreateToastByType(NotificationType toastType, string title = "", object? content = null, int duration = 3)
    {
        if (duration <= 0)
        {
            return Instances.ToastManager.CreateToast()
           .WithTitle(title)
           .WithContent(
               content)
           .OfType(toastType).Dismiss().ByClicking();
        }
        return Instances.ToastManager.CreateToast()
            .WithTitle(title)
            .WithContent(
                content)
            .OfType(toastType).Dismiss().After(TimeSpan.FromSeconds(duration))
            .Dismiss().ByClicking();
    }

    public static void Success(string title = "", object? content = null, int duration = 3)
    {
        if (IsDuplicate(title, content)) return;
        DispatcherHelper.RunOnMainThread(() => CreateToastByType(NotificationType.Success, title, content, duration).Queue());
    }

    public static void Info(string title = "", object? content = null, int duration = 3)
    {
        if (IsDuplicate(title, content)) return;
        DispatcherHelper.RunOnMainThread(() => CreateToastByType(NotificationType.Information, title, content, duration).Queue());
    }

    public static void Warn(string title = "", object? content = null, int duration = 3)
    {
        if (IsDuplicate(title, content)) return;
        DispatcherHelper.RunOnMainThread(() => CreateToastByType(NotificationType.Warning, title, content, duration).Queue());
    }

    public static void Error(string title = "", object? content = null, int duration = 3)
    {
        if (IsDuplicate(title, content)) return;
        DispatcherHelper.RunOnMainThread(() => CreateToastByType(NotificationType.Error, title, content, duration).Queue());
    }
}
