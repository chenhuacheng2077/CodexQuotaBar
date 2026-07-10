using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexQuotaBar.Services;

public sealed class TargetWindowTracker : IDisposable
{
    private const uint EventObjectLocationChange = 0x800B;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private readonly System.Threading.Timer _discoveryTimer;
    private readonly WinEventDelegate _callback;
    private readonly IntPtr _hook;
    public IntPtr Target { get; private set; }
    public event Action<IntPtr>? TargetChanged;
    public event Action? TargetMoved;
    public event Action<bool>? TargetVisibilityChanged;

    public TargetWindowTracker()
    {
        _callback = OnWindowEvent;
        _hook = SetWinEventHook(EventSystemMinimizeStart, EventObjectLocationChange, IntPtr.Zero, _callback, 0, 0, 0);
        _discoveryTimer = new System.Threading.Timer(_ => Discover(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public void Discover()
    {
        if (Target != IntPtr.Zero && IsWindow(Target)) return;
        var found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var processId);
            try
            {
                var process = Process.GetProcessById((int)processId);
                var title = GetTitle(window);
                if (process.Id != Environment.ProcessId && IsWindowVisible(window) &&
                    (process.ProcessName.Contains("codex", StringComparison.OrdinalIgnoreCase) ||
                     process.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) ||
                     title.Contains("Codex", StringComparison.OrdinalIgnoreCase)))
                {
                    found = window;
                    return false;
                }
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        if (found != Target) { Target = found; TargetChanged?.Invoke(found); }
    }

    private void OnWindowEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint milliseconds)
    {
        if (window == Target)
        {
            if (eventType == EventSystemMinimizeStart) TargetVisibilityChanged?.Invoke(false);
            else if (eventType == EventSystemMinimizeEnd) TargetVisibilityChanged?.Invoke(true);
            else TargetMoved?.Invoke();
        }
        else if (Target == IntPtr.Zero) Discover();
    }

    private static string GetTitle(IntPtr window)
    {
        var length = GetWindowTextLength(window);
        var buffer = new System.Text.StringBuilder(length + 1);
        GetWindowText(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public void Dispose()
    {
        _discoveryTimer.Dispose();
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint milliseconds);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
}
