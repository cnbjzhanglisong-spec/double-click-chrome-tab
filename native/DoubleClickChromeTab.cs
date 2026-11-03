using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("双击关闭 Chrome 标签")]
[assembly: AssemblyDescription("双击 Chrome 原生标签页即可关闭")]
[assembly: AssemblyProduct("双击关闭 Chrome 标签")]
[assembly: AssemblyCopyright("版权归属@阿萌同学")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyVersion("1.1.8.0")]
[assembly: AssemblyFileVersion("1.1.8.0")]

namespace DoubleClickChromeTab
{
    internal static class Program
    {
        private const string MutexName = "Local\\DoubleClickChromeTab.Tray.v9";

        private static bool HasAnotherVersionRunning()
        {
            using (Process current = Process.GetCurrentProcess())
            {
                Process[] processes = Process.GetProcessesByName(current.ProcessName);
                try
                {
                    foreach (Process process in processes)
                    {
                        if (process.Id != current.Id)
                        {
                            return true;
                        }
                    }
                }
                finally
                {
                    foreach (Process process in processes)
                    {
                        process.Dispose();
                    }
                }
            }

            return false;
        }

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "双击关闭 Chrome 标签已经在运行。\n请双击系统托盘中的程序图标打开主窗口。",
                        "双击关闭 Chrome 标签",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                if (HasAnotherVersionRunning())
                {
                    MessageBox.Show(
                        "检测到“双击关闭 Chrome 标签”的另一个版本正在运行。\n\n" +
                        "请先右键旧版托盘图标并选择“退出”，然后重新启动本程序。\n" +
                        "同时运行多个版本可能导致一次双击关闭两个标签。",
                        "双击关闭 Chrome 标签",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

                try
                {
                    Application.Run(new TrayApplicationContext());
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "程序启动失败：\n" + ex.Message,
                        "双击关闭 Chrome 标签",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                GC.KeepAlive(mutex);
            }
        }
    }

    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private const string SettingsKeyPath = @"Software\DoubleClickChromeTab";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "DoubleClickChromeTab";

        private readonly NotifyIcon notifyIcon;
        private readonly ContextMenuStrip trayMenu;
        private readonly ToolStripMenuItem enabledItem;
        private readonly ToolStripMenuItem compatibilityItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly LowLevelMouseHook mouseHook;
        private readonly MainForm mainForm;

        private bool enabled;
        private bool compatibilityMode;
        private bool exiting;
        private DateTime lastClickUp = DateTime.MinValue;
        private Point lastClickPoint;
        private IntPtr lastClickWindow = IntPtr.Zero;
        private int lastClickProcessId;
        private Task<bool> lastClickTabVerification = Task.FromResult(false);
        private int closeScheduled;

        public TrayApplicationContext()
        {
            enabled = ReadBoolSetting("Enabled", true);
            compatibilityMode = ReadBoolSetting("CompatibilityMode", false);

            enabledItem = new ToolStripMenuItem("启用双击关闭")
            {
                CheckOnClick = true,
                Checked = enabled
            };
            enabledItem.CheckedChanged += OnEnabledChanged;

            compatibilityItem = new ToolStripMenuItem("兼容模式（智能识别异常时使用）")
            {
                CheckOnClick = true,
                Checked = compatibilityMode,
                ToolTipText = "扩大标签栏顶部识别范围；默认无需开启。"
            };
            compatibilityItem.CheckedChanged += OnCompatibilityChanged;

            startupItem = new ToolStripMenuItem("开机自动启动")
            {
                CheckOnClick = true,
                Checked = IsStartupEnabled()
            };
            startupItem.CheckedChanged += OnStartupChanged;

            var openWindowItem = new ToolStripMenuItem("打开主窗口");
            openWindowItem.Click += (sender, args) => ShowMainWindow();

            var helpItem = new ToolStripMenuItem("使用说明");
            helpItem.Click += OnHelpClicked;

            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += OnExitClicked;

            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add(openWindowItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(enabledItem);
            trayMenu.Items.Add(compatibilityItem);
            trayMenu.Items.Add(startupItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(helpItem);
            trayMenu.Items.Add(exitItem);

            mainForm = new MainForm();
            mainForm.HiddenToTray += OnMainWindowHidden;
            mainForm.UpdateStatus(enabled, compatibilityMode);
            mainForm.StartupEnabled = IsStartupEnabled();
            mainForm.StartupSettingChanged += OnMainStartupSettingChanged;

            notifyIcon = new NotifyIcon
            {
                Text = "双击关闭 Chrome 标签",
                Visible = true,
                Icon = LoadApplicationIcon()
            };
            notifyIcon.MouseClick += OnTrayMouseClick;
            notifyIcon.MouseUp += OnTrayMouseUp;

            mouseHook = new LowLevelMouseHook(HandleMouseEvent);
            UpdateTrayText();
            mainForm.Show();
            mainForm.Activate();
        }

        private void HandleMouseEvent(int message, Point point)
        {
            if (!enabled || exiting || message != NativeMethods.WM_LBUTTONUP)
            {
                return;
            }

            if (Control.ModifierKeys != Keys.None)
            {
                lastClickUp = DateTime.MinValue;
                return;
            }

            DateTime now = DateTime.UtcNow;
            int doubleClickTime = SystemInformation.DoubleClickTime;
            Size doubleClickSize = SystemInformation.DoubleClickSize;

            bool isSecondClick =
                lastClickUp != DateTime.MinValue &&
                (now - lastClickUp).TotalMilliseconds <= doubleClickTime &&
                Math.Abs(point.X - lastClickPoint.X) <= Math.Max(1, doubleClickSize.Width / 2) &&
                Math.Abs(point.Y - lastClickPoint.Y) <= Math.Max(1, doubleClickSize.Height / 2);

            if (!isSecondClick)
            {
                RememberClick(point);
                return;
            }

            IntPtr currentWindow = NativeMethods.GetForegroundWindow();
            int currentProcessId = NativeMethods.GetWindowProcessId(currentWindow);
            Task<bool> firstClickVerification = lastClickTabVerification;
            bool sameTarget = currentWindow == lastClickWindow && currentProcessId == lastClickProcessId;

            lastClickUp = DateTime.MinValue;

            if (!sameTarget || !IsChromeProcess(currentProcessId))
            {
                RememberClick(point);
                return;
            }

            // Verify both clicks were on Chrome UI. The first-click check prevents a close
            // button click from turning into "close the next tab" after the tab disappears.
            ScheduleClose(currentWindow, currentProcessId, point, firstClickVerification);
        }

        private void RememberClick(Point point)
        {
            IntPtr windowHandle = NativeMethods.GetForegroundWindow();
            int processId = NativeMethods.GetWindowProcessId(windowHandle);

            lastClickUp = DateTime.UtcNow;
            lastClickPoint = point;
            lastClickWindow = windowHandle;
            lastClickProcessId = processId;

            if (IsChromeProcess(processId))
            {
                IntPtr capturedWindow = windowHandle;
                int capturedProcessId = processId;
                Point capturedPoint = point;
                lastClickTabVerification = Task.Run(
                    () => IsChromeTabAtPoint(capturedPoint, capturedWindow, capturedProcessId));
            }
            else
            {
                lastClickTabVerification = Task.FromResult(false);
            }
        }

        private void ScheduleClose(
            IntPtr windowHandle,
            int processId,
            Point point,
            Task<bool> firstClickVerification)
        {
            if (Interlocked.CompareExchange(ref closeScheduled, 1, 0) != 0)
            {
                return;
            }

            Task<bool> firstClickCheck = firstClickVerification ?? Task.FromResult(false);

            Task.Run(async () =>
            {
                try
                {
                    // Let Chrome finish activating the clicked tab before sending Ctrl+W.
                    await Task.Delay(90).ConfigureAwait(false);

                    Task completedCheck = await Task.WhenAny(
                        firstClickCheck,
                        Task.Delay(450)).ConfigureAwait(false);

                    if (completedCheck != firstClickCheck || !await firstClickCheck.ConfigureAwait(false))
                    {
                        return;
                    }

                    if (!enabled || exiting || NativeMethods.GetForegroundWindow() != windowHandle)
                    {
                        return;
                    }

                    Point currentPoint;
                    if (!NativeMethods.GetCursorPos(out currentPoint))
                    {
                        return;
                    }

                    if (Math.Abs(currentPoint.X - point.X) > 5 || Math.Abs(currentPoint.Y - point.Y) > 5)
                    {
                        return;
                    }

                    if (!IsChromeProcess(processId))
                    {
                        return;
                    }

                    if (!IsChromeTabAtPoint(point, windowHandle, processId))
                    {
                        return;
                    }

                    if (NativeMethods.GetForegroundWindow() != windowHandle ||
                        Control.ModifierKeys != Keys.None)
                    {
                        return;
                    }

                    NativeMethods.SendCtrlW();
                }
                catch
                {
                    // A closed Chrome window or an unavailable UI Automation element is ignored.
                }
                finally
                {
                    Interlocked.Exchange(ref closeScheduled, 0);
                }
            });
        }
        private bool IsChromeTabAtPoint(Point point, IntPtr windowHandle, int expectedProcessId)
        {
            AutomationElement element = null;

            try
            {
                element = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            }
            catch
            {
                element = null;
            }

            if (element != null)
            {
                AutomationElement current = element;

                for (int depth = 0; depth < 7 && current != null; depth++)
                {
                    try
                    {
                        if (current.Current.ProcessId != expectedProcessId)
                        {
                            return false;
                        }

                        ControlType controlType = current.Current.ControlType;

                        if (controlType == ControlType.TabItem)
                        {
                            return true;
                        }

                        if (IsBlockingControlType(controlType))
                        {
                            return false;
                        }

                        string className = current.Current.ClassName ?? string.Empty;
                        string automationId = current.Current.AutomationId ?? string.Empty;
                        string identity = (className + " " + automationId).ToLowerInvariant();

                        if (identity.Contains("tab") &&
                            !identity.Contains("tabstrip") &&
                            !identity.Contains("newtab") &&
                            !identity.Contains("close"))
                        {
                            return true;
                        }

                        if (controlType == ControlType.ToolBar || controlType == ControlType.Window)
                        {
                            break;
                        }

                        current = TreeWalker.ControlViewWalker.GetParent(current);
                    }
                    catch
                    {
                        break;
                    }
                }
            }

            return compatibilityMode && IsInsideTabStripBand(point, windowHandle);
        }

        private static bool IsBlockingControlType(ControlType controlType)
        {
            return
                controlType == ControlType.Button ||
                controlType == ControlType.Edit ||
                controlType == ControlType.ComboBox ||
                controlType == ControlType.CheckBox ||
                controlType == ControlType.RadioButton ||
                controlType == ControlType.MenuItem ||
                controlType == ControlType.Hyperlink ||
                controlType == ControlType.Slider ||
                controlType == ControlType.Spinner ||
                controlType == ControlType.ScrollBar ||
                controlType == ControlType.ListItem ||
                controlType == ControlType.TreeItem ||
                controlType == ControlType.DataItem;
        }

        private static bool IsInsideTabStripBand(Point point, IntPtr windowHandle)
        {
            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(windowHandle, out rect))
            {
                return false;
            }

            int bandHeight = 64;
            try
            {
                uint dpi = NativeMethods.GetDpiForWindow(windowHandle);
                if (dpi > 0)
                {
                    bandHeight = (int)Math.Round(52.0 * dpi / 96.0);
                }
            }
            catch
            {
                // Older Windows versions fall back to 64 logical pixels.
            }

            return point.X >= rect.Left &&
                   point.X <= rect.Right &&
                   point.Y >= rect.Top &&
                   point.Y <= rect.Top + bandHeight;
        }

        private static bool IsChromeProcess(int processId)
        {
            if (processId <= 0)
            {
                return false;
            }

            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    return string.Equals(process.ProcessName, "chrome", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        private void OnEnabledChanged(object sender, EventArgs args)
        {
            enabled = enabledItem.Checked;
            WriteBoolSetting("Enabled", enabled);
            UpdateTrayText();

            if (!enabled)
            {
                lastClickUp = DateTime.MinValue;
            }
        }

        private void OnCompatibilityChanged(object sender, EventArgs args)
        {
            compatibilityMode = compatibilityItem.Checked;
            WriteBoolSetting("CompatibilityMode", compatibilityMode);
            ShowBalloon(
                "兼容模式",
                compatibilityMode ? "已开启。仅在默认模式无法识别时使用。" : "已关闭。");
        }

        private void OnStartupChanged(object sender, EventArgs args)
        {
            ApplyStartupSetting(startupItem.Checked);
        }

        private void OnMainStartupSettingChanged(object sender, EventArgs args)
        {
            ApplyStartupSetting(mainForm.StartupEnabled);
        }

        private void ApplyStartupSetting(bool requested)
        {
            if (!SetStartup(requested))
            {
                startupItem.CheckedChanged -= OnStartupChanged;
                startupItem.Checked = !requested;
                startupItem.CheckedChanged += OnStartupChanged;
                mainForm.StartupEnabled = !requested;

                MessageBox.Show(
                    "无法修改开机启动设置。",
                    "双击关闭 Chrome 标签",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            startupItem.CheckedChanged -= OnStartupChanged;
            startupItem.Checked = requested;
            startupItem.CheckedChanged += OnStartupChanged;
            mainForm.StartupEnabled = requested;
        }

        private void OnHelpClicked(object sender, EventArgs args)
        {
            MessageBox.Show(
                "使用方法：\n\n" +
                "1. 双击 Chrome 顶部任意标签页即可关闭。\n" +
                "2. 点击主窗口关闭按钮只会隐藏到系统托盘。\n" +
                "3. 双击系统托盘中的程序图标，可重新打开主窗口。\n" +
                "4. 如需完全退出，请右键托盘图标选择“退出”。\n\n" +
                "如双击标签没有反应，可右键托盘图标，临时开启“兼容模式”。\n\n" +
                "本程序只读取鼠标位置和 Chrome 标签的 UI Automation 信息，不记录、上传任何数据。",
                "双击关闭 Chrome 标签 - 使用说明",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
        }

        private void OnTrayMouseUp(object sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Right)
            {
                trayMenu.Show(Cursor.Position);
            }
        }

        private void ShowMainWindow()
        {
            if (exiting)
            {
                return;
            }

            mainForm.ShowMainWindow();
        }

        private void OnMainWindowHidden(object sender, EventArgs args)
        {
            ShowBalloon("程序仍在托盘运行", "双击托盘图标可重新打开主窗口。");
        }

        private void OnExitClicked(object sender, EventArgs args)
        {
            ExitApplication();
        }

        private void ExitApplication()
        {
            if (exiting)
            {
                return;
            }

            exiting = true;
            mainForm.PrepareForExit();
            mainForm.Close();
            mainForm.Dispose();
            notifyIcon.Visible = false;
            mouseHook.Dispose();
            notifyIcon.Dispose();
            trayMenu.Dispose();
            ExitThread();
        }

        private void UpdateTrayText()
        {
            notifyIcon.Text = enabled
                ? "双击关闭 Chrome 标签 v1.1.8（已启用）"
                : "双击关闭 Chrome 标签 v1.1.8（已暂停）";
        }

        private void ShowBalloon(string title, string text)
        {
            try
            {
                notifyIcon.BalloonTipTitle = title;
                notifyIcon.BalloonTipText = text;
                notifyIcon.ShowBalloonTip(2200);
            }
            catch
            {
                // Notifications may be disabled by Windows settings.
            }
        }

        private static Icon LoadApplicationIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                {
                    return icon;
                }
            }
            catch
            {
                // Use the system fallback icon below.
            }

            return (Icon)SystemIcons.Application.Clone();
        }

        private static bool ReadBoolSetting(string name, bool fallback)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath))
                {
                    if (key != null)
                    {
                        object value = key.GetValue(name);
                        if (value is int)
                        {
                            return (int)value != 0;
                        }
                    }
                }
            }
            catch
            {
                // Registry access failure uses the safe default.
            }

            return fallback;
        }

        private static void WriteBoolSetting(string name, bool value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch
            {
                // Settings persistence is optional.
            }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    string value = key == null ? null : key.GetValue(RunValueName) as string;
                    string expected = "\"" + Application.ExecutablePath + "\"";
                    return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool SetStartup(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    if (enabled)
                    {
                        key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue(RunValueName, false);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !exiting)
            {
                exiting = true;
                if (mouseHook != null)
                {
                    mouseHook.Dispose();
                }
                if (notifyIcon != null)
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                }
                if (trayMenu != null)
                {
                    trayMenu.Dispose();
                }
                if (mainForm != null)
                {
                    mainForm.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }

    internal sealed class LowLevelMouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;

        private readonly NativeMethods.LowLevelMouseProc hookCallback;
        private readonly Action<int, Point> onMouseEvent;
        private IntPtr hookHandle;
        private bool disposed;

        public LowLevelMouseHook(Action<int, Point> onMouseEvent)
        {
            this.onMouseEvent = onMouseEvent;
            hookCallback = HookCallback;

            hookHandle = NativeMethods.SetWindowsHookEx(
                WH_MOUSE_LL,
                hookCallback,
                NativeMethods.GetModuleHandle(null),
                0);

            if (hookHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException("安装鼠标钩子失败，错误代码：" + error);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();

                if (message == NativeMethods.WM_LBUTTONDOWN ||
                    message == NativeMethods.WM_LBUTTONUP ||
                    message == NativeMethods.WM_MOUSEMOVE)
                {
                    try
                    {
                        NativeMethods.MSLLHOOKSTRUCT data =
                            (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                                lParam,
                                typeof(NativeMethods.MSLLHOOKSTRUCT));

                        onMouseEvent(message, new Point(data.pt.X, data.pt.Y));
                    }
                    catch
                    {
                        // A mouse callback must never break the Windows hook chain.
                    }
                }
            }

            return NativeMethods.CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(hookHandle);
                hookHandle = IntPtr.Zero;
            }
        }
    }

    internal static class NativeMethods
    {
        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_CONTROL = 0x11;
        private const byte VK_W = 0x57;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelMouseProc callback,
            IntPtr moduleHandle,
            uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(
            IntPtr hookHandle,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr windowHandle, uint flags);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr windowHandle, out RECT rect);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        public static IntPtr GetTopLevelWindowAtPoint(Point point)
        {
            var nativePoint = new POINT { X = point.X, Y = point.Y };
            IntPtr window = WindowFromPoint(nativePoint);
            return window == IntPtr.Zero ? IntPtr.Zero : GetAncestor(window, 2);
        }

        public static int GetWindowProcessId(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero)
            {
                return 0;
            }

            uint processId;
            GetWindowThreadProcessId(windowHandle, out processId);
            return unchecked((int)processId);
        }

        public static void SendCtrlW()
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_W, 0, 0, UIntPtr.Zero);
            keybd_event(VK_W, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
    }
}

