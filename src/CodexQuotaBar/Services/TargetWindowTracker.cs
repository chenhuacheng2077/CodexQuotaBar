using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexQuotaBar.Services;

public sealed class TargetWindowTracker : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventSystemMinimizeStart = 0x0016;
    private const uint EventSystemMinimizeEnd = 0x0017;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WineventOutofcontext = 0;
    private const uint WineventSkipownprocess = 2;

    private readonly System.Threading.Timer _discoveryTimer;
    private readonly WinEventDelegate _callback;
    private readonly List<IntPtr> _hooks = new();
    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "chromium",
        "chrome_proxy",
        "msedge",
        "msedgewebview2",
        "firefox",
        "brave",
        "opera",
        "vivaldi",
        "electron",
        "browser",
    };
    private int _discoveryBusy;
    private bool _targetVisible;
    private bool _disposed;

    public IntPtr Target { get; private set; }
    public event Action<IntPtr>? TargetChanged;
    public event Action? TargetMoved;
    public event Action<bool>? TargetVisibilityChanged;

    public TargetWindowTracker()
    {
        _callback = OnWindowEvent;
        var flags = WineventOutofcontext | WineventSkipownprocess;
        _hooks.Add(SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, flags));
        _hooks.Add(SetWinEventHook(EventSystemMinimizeStart, EventSystemMinimizeEnd, IntPtr.Zero, _callback, 0, 0, flags));
        _hooks.Add(SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange, IntPtr.Zero, _callback, 0, 0, flags));
        _discoveryTimer = new System.Threading.Timer(_ => Discover(), null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    public void Discover()
    {
        if (_disposed || Interlocked.Exchange(ref _discoveryBusy, 1) == 1) return;
        try
        {
            var foreground = GetForegroundWindow();
            var candidates = new List<WindowCandidate>();
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var processId);
                if (processId == 0 || processId == Environment.ProcessId) return true;
                if (!IsWindowVisible(window) || IsIconic(window)) return true;

                string processName;
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                }
                catch
                {
                    return true;
                }

                if (processName.StartsWith("CodexQuotaBar", StringComparison.OrdinalIgnoreCase)) return true;

                var title = GetTitle(window);
                var primary = IsPrimaryProcess(processName);
                var fallback = !primary && !BrowserProcesses.Contains(processName) &&
                               title.Contains("Codex", StringComparison.OrdinalIgnoreCase);
                if (primary || fallback)
                {
                    candidates.Add(new WindowCandidate(window, primary, window == foreground));
                }

                return true;
            }, IntPtr.Zero);

            var found = SelectTarget(candidates);
            if (found == IntPtr.Zero && Target != IntPtr.Zero && IsWindow(Target) && IsPrimaryWindow(Target))
            {
                // Keep a minimized Codex window as the owner so it can be restored later.
                found = Target;
            }

            if (found != Target)
            {
                Target = found;
                _targetVisible = IsTargetVisible(found);
                TargetChanged?.Invoke(found);
            }
            else
            {
                var visible = IsTargetVisible(found);
                if (visible != _targetVisible)
                {
                    _targetVisible = visible;
                    TargetVisibilityChanged?.Invoke(visible);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _discoveryBusy, 0);
        }
    }

    private void OnWindowEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint milliseconds)
    {
        if (_disposed) return;

        if (eventType == EventSystemForeground)
        {
            // A browser tab can contain “Codex” in its title. Re-evaluate all
            // candidates whenever the foreground window changes so a real
            // ChatGPT/Codex process wins as soon as it appears.
            Discover();
            if (window == Target) TargetMoved?.Invoke();
            return;
        }

        if (window == Target && Target != IntPtr.Zero)
        {
            if (eventType == EventSystemMinimizeStart)
            {
                _targetVisible = false;
                TargetVisibilityChanged?.Invoke(false);
            }
            else if (eventType == EventSystemMinimizeEnd)
            {
                _targetVisible = true;
                TargetVisibilityChanged?.Invoke(true);
            }
            else if (eventType == EventObjectLocationChange || eventType == EventSystemForeground)
            {
                TargetMoved?.Invoke();
            }
            return;
        }

        if (Target == IntPtr.Zero &&
            (eventType == EventSystemForeground || eventType == EventSystemMinimizeEnd))
        {
            Discover();
        }
    }

    private IntPtr SelectTarget(IReadOnlyList<WindowCandidate> candidates)
    {
        var primary = candidates.Where(candidate => candidate.Primary).ToList();
        if (primary.Count > 0)
        {
            var foreground = primary.FirstOrDefault(candidate => candidate.Foreground);
            if (foreground.Handle != IntPtr.Zero) return foreground.Handle;

            var current = primary.FirstOrDefault(candidate => candidate.Handle == Target);
            return current.Handle != IntPtr.Zero ? current.Handle : primary[0].Handle;
        }

        var fallback = candidates.FirstOrDefault(candidate => candidate.Fallback);
        if (fallback.Handle != IntPtr.Zero) return fallback.Handle;

        return IntPtr.Zero;
    }

    private static bool IsPrimaryProcess(string processName) =>
        processName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("codex", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrimaryWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window)) return false;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return false;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return IsPrimaryProcess(process.ProcessName);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTargetVisible(IntPtr window) =>
        window != IntPtr.Zero && IsWindow(window) && IsWindowVisible(window) && !IsIconic(window);

    private static string GetTitle(IntPtr window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0) return string.Empty;
        var buffer = new System.Text.StringBuilder(length + 1);
        GetWindowText(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _discoveryTimer.Dispose();
        foreach (var hook in _hooks.Where(hook => hook != IntPtr.Zero))
        {
            UnhookWinEvent(hook);
        }
        _hooks.Clear();
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint milliseconds);
    private readonly record struct WindowCandidate(IntPtr Handle, bool Primary, bool Foreground)
    {
        public bool Fallback => !Primary;
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
}
