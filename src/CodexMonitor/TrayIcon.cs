using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodexMonitor;

/// <summary>Documented Shell_NotifyIcon API, fixed GUID, no Explorer hooks or taskbar modification.</summary>
internal sealed class TrayIcon : NativeWindow, IDisposable
{
    private const int Callback = 0x8001;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly Guid guid = new("4d986866-c6a6-4fae-9b69-52c7409a2ef1");
    private Icon? current;
    private string tooltip = "CodexMonitor";
    private bool registered;
    public ContextMenuStrip Menu { get; } = new();
    public event Action? Click;
    public event Action? ShellChanged;
    public bool Registered => registered;
    public int IconSize
    {
        get
        {
            var taskbar = FindWindow("Shell_TrayWnd", null);
            var dpi = taskbar == IntPtr.Zero ? 96 : GetDpiForWindow(taskbar);
            return Math.Max(16, GetSystemMetricsForDpi(49, dpi == 0 ? 96 : dpi));
        }
    }
    public TrayIcon() => CreateHandle(new CreateParams { Caption = "CodexMonitor message host" });
    public void Set(Icon icon, string text)
    {
        var old = current;
        current = icon;
        tooltip = text.Length <= 127 ? text : text[..127];
        var data = Data();
        var success = ShellNotifyIcon(registered ? 1u : 0u, ref data);
        if (!success && registered) { registered = false; success = ShellNotifyIcon(0, ref data); }
        registered = success;
        if (success) { data.Version = 4; ShellNotifyIcon(4, ref data); }
        old?.Dispose();
    }
    public Rectangle? GetRectangle()
    {
        var identifier = new IconIdentifier { Size = (uint)Marshal.SizeOf<IconIdentifier>(), Window = Handle, Id = 1, Guid = guid };
        return ShellNotifyIconGetRect(ref identifier, out var rect) == 0 ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) : null;
    }
    internal void VerifyShellRecovery()
    {
        var data = Data();
        ShellNotifyIcon(2, ref data); // Remove only our own icon, then reproduce Explorer's documented notification.
        PostMessage(Handle, taskbarCreated, IntPtr.Zero, IntPtr.Zero);
    }
    protected override void WndProc(ref Message m)
    {
        if ((uint)m.Msg == taskbarCreated)
        { registered = false; ShellChanged?.Invoke(); }
        else if (m.Msg == 0x7E || m.Msg == 0x2E0) ShellChanged?.Invoke(); // display/DPI changes
        else if (m.Msg == Callback)
        {
            var notification = (int)((long)m.LParam & 0xFFFF);
            if (notification is 0x400 or 0x401 or 0x202) Click?.Invoke(); // NIN_SELECT / KEYSELECT / legacy left up
            if (notification is 0x7B or 0x205) // context menu / legacy right up
            {
                SetForegroundWindow(Handle);
                Menu.Show(Cursor.Position);
                PostMessage(Handle, 0, IntPtr.Zero, IntPtr.Zero);
            }
        }
        base.WndProc(ref m);
    }
    private NotifyData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyData>(), Window = Handle, Id = 1,
        Flags = 1 | 2 | 4 | 0x20 | 0x80, CallbackMessage = Callback, Icon = current?.Handle ?? IntPtr.Zero,
        Tip = tooltip, Guid = guid, Info = "", InfoTitle = ""
    };
    public void Dispose()
    {
        var data = Data(); ShellNotifyIcon(2, ref data);
        registered = false; Menu.Dispose(); current?.Dispose(); DestroyHandle();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NotifyData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, CallbackMessage; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyData data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")] private static extern int ShellNotifyIconGetRect(ref IconIdentifier id, out NativeRect rectangle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
