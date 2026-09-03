using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[assembly: AssemblyTitle("Wallpaper Control")]
[assembly: AssemblyDescription("Lively Wallpaper automation and safe MPV color control")]
[assembly: AssemblyProduct("Wallpaper Control")]
[assembly: AssemblyCompany("Wallpaper Control Community")]
[assembly: AssemblyVersion("2.6.2.0")]
[assembly: AssemblyFileVersion("2.6.2.0")]

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(7, 9, 14);
    public static readonly Color Surface = Color.FromArgb(15, 18, 26);
    public static readonly Color Card = Color.FromArgb(21, 25, 35);
    public static readonly Color CardAlt = Color.FromArgb(26, 30, 42);
    public static readonly Color Border = Color.FromArgb(50, 55, 72);
    public static readonly Color Text = Color.FromArgb(242, 244, 250);
    public static readonly Color Muted = Color.FromArgb(149, 157, 177);
    public static readonly Color Red = Color.FromArgb(230, 0, 70);
    public static readonly Color Magenta = Color.FromArgb(255, 36, 119);
    public static readonly Color Cyan = Color.FromArgb(0, 205, 255);
    public static readonly Color Green = Color.FromArgb(53, 219, 137);
}

internal sealed class WallpaperSettings
{
    public bool AutoEnabled = true;
    public string ColorMode = "Off";
    public int Intensity = 50;
    public int Saturation = 100;
}

internal sealed class RogCard : Panel
{
    public Color AccentColor = Theme.Red;
    public RogCard()
    {
        DoubleBuffered = true;
        BackColor = Theme.Card;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var border = new Pen(Theme.Border))
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        using (var accent = new SolidBrush(AccentColor))
            e.Graphics.FillRectangle(accent, 0, 0, 4, Height);
    }
}

internal sealed class RogButton : Button
{
    private Color normal;
    private Color hover;

    public RogButton(Color background, Color hoverBackground)
    {
        normal = background;
        hover = hoverBackground;
        BackColor = normal;
        ForeColor = Color.White;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI Semibold", 9.5F);
        MouseEnter += delegate { BackColor = hover; };
        MouseLeave += delegate { BackColor = normal; };
    }

    public void SetPalette(Color background, Color hoverBackground)
    {
        normal = background;
        hover = hoverBackground;
        BackColor = normal;
    }
}

internal sealed class RogSlider : Control
{
    private int sliderValue = 50;
    private bool dragging;
    public event EventHandler ValueChanged;

    public int Value
    {
        get { return sliderValue; }
        set
        {
            int next = Math.Max(0, Math.Min(100, value));
            if (next == sliderValue) return;
            sliderValue = next;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }

    public RogSlider()
    {
        DoubleBuffered = true;
        Height = 40;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.Selectable, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int left = 8;
        int right = Width - 8;
        int centerY = Height / 2;
        int fillRight = left + (int)((right - left) * (sliderValue / 100.0));
        using (var track = new Pen(Enabled ? Color.FromArgb(66, 72, 91) : Color.FromArgb(43, 46, 57), 6F))
            e.Graphics.DrawLine(track, left, centerY, right, centerY);
        using (var fill = new Pen(Enabled ? Theme.Magenta : Color.FromArgb(88, 67, 77), 6F))
            e.Graphics.DrawLine(fill, left, centerY, fillRight, centerY);
        using (var glow = new SolidBrush(Enabled ? Color.FromArgb(75, 255, 36, 119) : Color.Transparent))
            e.Graphics.FillEllipse(glow, fillRight - 10, centerY - 10, 20, 20);
        using (var thumb = new SolidBrush(Enabled ? Color.White : Color.FromArgb(105, 108, 118)))
            e.Graphics.FillEllipse(thumb, fillRight - 6, centerY - 6, 12, 12);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left) return;
        dragging = true;
        Capture = true;
        UpdateFromMouse(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging) UpdateFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        dragging = false;
        Capture = false;
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    private void UpdateFromMouse(int x)
    {
        int usable = Math.Max(1, Width - 16);
        Value = (int)Math.Round((Math.Max(8, Math.Min(Width - 8, x)) - 8) * 100.0 / usable);
    }
}

internal sealed class RogColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground { get { return Theme.CardAlt; } }
    public override Color ImageMarginGradientBegin { get { return Theme.CardAlt; } }
    public override Color ImageMarginGradientMiddle { get { return Theme.CardAlt; } }
    public override Color ImageMarginGradientEnd { get { return Theme.CardAlt; } }
    public override Color MenuItemSelected { get { return Color.FromArgb(58, 26, 43); } }
    public override Color MenuItemBorder { get { return Theme.Red; } }
    public override Color MenuBorder { get { return Theme.Border; } }
    public override Color SeparatorDark { get { return Theme.Border; } }
    public override Color SeparatorLight { get { return Theme.Border; } }
}

internal static class NativeMethods
{
    public const int WM_NCLBUTTONDOWN = 0xA1;
    public const int WM_NCHITTEST = 0x84;
    public const int HTCAPTION = 0x2;
    public const int HTCLIENT = 0x1;
    public const int HTLEFT = 10;
    public const int HTRIGHT = 11;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15;
    public const int HTBOTTOMLEFT = 16;
    public const int HTBOTTOMRIGHT = 17;

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    [DllImport("ole32.dll")]
    public static extern int PropVariantClear(ref PropVariant value);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetFileInformationByHandle(SafeFileHandle fileHandle, out ByHandleFileInformation fileInformation);
}

internal sealed class ResponsiveLayoutSnapshot
{
    public Rectangle Bounds;
    public string FontFamily;
    public float FontSize;
    public FontStyle FontStyle;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ByHandleFileInformation
{
    public uint FileAttributes;
    public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
    public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
    public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
    public uint VolumeSerialNumber;
    public uint FileSizeHigh;
    public uint FileSizeLow;
    public uint NumberOfLinks;
    public uint FileIndexHigh;
    public uint FileIndexLow;
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
    public PropertyKey(Guid formatId, uint propertyId) { FormatId = formatId; PropertyId = propertyId; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort VariantType;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr PointerValue;
    public int Padding;

    public static PropVariant FromString(string value)
    {
        return new PropVariant { VariantType = 31, PointerValue = Marshal.StringToCoTaskMemUni(value) };
    }
}

[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLinkCom { }

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
    void GetPath(IntPtr file, int maxPath, IntPtr findData, uint flags);
    void GetIDList(out IntPtr itemIdList);
    void SetIDList(IntPtr itemIdList);
    void GetDescription(IntPtr name, int maxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
    void GetWorkingDirectory(IntPtr directory, int maxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
    void GetArguments(IntPtr arguments, int maxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
    void GetHotkey(out short hotkey);
    void SetHotkey(short hotkey);
    void GetShowCmd(out int showCommand);
    void SetShowCmd(int showCommand);
    void GetIconLocation(IntPtr iconPath, int iconPathLength, out int iconIndex);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
    void Resolve(IntPtr window, uint flags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
}

[ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
internal interface IPropertyStore
{
    void GetCount(out uint propertyCount);
    void GetAt(uint propertyIndex, out PropertyKey key);
    void GetValue(ref PropertyKey key, out PropVariant value);
    void SetValue(ref PropertyKey key, ref PropVariant value);
    void Commit();
}

internal static class ShellIntegration
{
    public const string AppUserModelId = "WallpaperControl.Gaming.Desktop";
    private const string StartupShortcutName = "Wallpaper Control.lnk";

    public static string StartupShortcutPath
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupShortcutName); }
    }

    public static bool IsStartupEnabled()
    {
        try { return File.Exists(StartupShortcutPath); }
        catch { return false; }
    }

    public static void SetStartupEnabled(bool enabled)
    {
        if (enabled)
        {
            CreateShortcut(StartupShortcutPath, Application.ExecutablePath, "--startup");
            return;
        }
        if (File.Exists(StartupShortcutPath)) File.Delete(StartupShortcutPath);
    }

    public static void EnsureInstalled()
    {
        try
        {
            string exe = Application.ExecutablePath;
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Wallpaper Control.lnk");
            string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Wallpaper Control.lnk");
            CreateShortcut(startMenu, exe, null);
            CreateShortcut(desktop, exe, null);

            using (var appPath = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\WallpaperControl.exe"))
            {
                appPath.SetValue("", exe);
                appPath.SetValue("Path", Path.GetDirectoryName(exe));
            }
            using (var app = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\WallpaperControl.exe"))
            {
                app.SetValue("FriendlyAppName", "Wallpaper Control");
                app.SetValue("ApplicationCompany", "Wallpaper Control");
                app.SetValue("AppUserModelID", AppUserModelId);
            }
            NativeMethods.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }

    private static void CreateShortcut(string shortcutPath, string exe, string arguments)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
        var link = (IShellLinkW)new ShellLinkCom();
        link.SetPath(exe);
        link.SetWorkingDirectory(Path.GetDirectoryName(exe));
        link.SetDescription("Điều khiển hình nền động Lively");
        link.SetIconLocation(exe, 0);
        link.SetShowCmd(1);
        if (!string.IsNullOrEmpty(arguments)) link.SetArguments(arguments);

        var key = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
        var value = PropVariant.FromString(AppUserModelId);
        try
        {
            var store = (IPropertyStore)link;
            store.SetValue(ref key, ref value);
            store.Commit();
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            NativeMethods.PropVariantClear(ref value);
            Marshal.FinalReleaseComObject(link);
        }
    }
}

internal sealed class WallpaperControlForm : Form
{
    private const int DesignWidth = 980;
    private const int DesignHeight = 760;
    private const int ResizeGrip = 9;
    private static readonly string ApplicationRoot = Path.GetDirectoryName(Application.ExecutablePath);
    internal static readonly string AutomationRoot = Path.Combine(ApplicationRoot, "Automation");
    private static readonly string SettingsPath = Path.Combine(AutomationRoot, "WallpaperControl.ini");
    private static readonly string DisabledMarkerPath = Path.Combine(AutomationRoot, "wallpaper-auto.disabled");
    private static readonly string ServicePidPath = Path.Combine(AutomationRoot, "lively-shuffle.pid");
    private static readonly string LauncherPath = Path.Combine(AutomationRoot, "LivelyShuffleLauncher.exe");
    private static readonly string ApplyHelperPath = Path.Combine(AutomationRoot, "ApplyWallpaperControl.ps1");
    private static readonly string LogoPath = Path.Combine(AutomationRoot, "Assets", "WallpaperControl.png");
    private readonly string activationSignalPath;

    private readonly Label autoState = new Label();
    private readonly Label autoDetail = new Label();
    private readonly Label colorState = new Label();
    private readonly Label intensityValue = new Label();
    private readonly Label saturationValue = new Label();
    private readonly Label colorPreview = new Label();
    private readonly Label profileState = new Label();
    private readonly Label currentVideoName = new Label();
    private readonly Label currentVideoConfig = new Label();
    private readonly Label storageStatus = new Label();
    private readonly Label topStatus = new Label();
    private readonly RogButton autoButton = new RogButton(Theme.Red, Theme.Magenta);
    private readonly RogButton applyButton = new RogButton(Theme.Red, Theme.Magenta);
    private readonly RogButton resetButton = new RogButton(Color.FromArgb(43, 47, 60), Color.FromArgb(61, 66, 83));
    private readonly RogButton saveProfileButton = new RogButton(Color.FromArgb(0, 112, 152), Theme.Cyan);
    private readonly ComboBox modeCombo = new ComboBox();
    private readonly RogSlider intensitySlider = new RogSlider();
    private readonly RogSlider saturationSlider = new RogSlider();
    private readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer();
    private readonly NotifyIcon trayIcon = new NotifyIcon();
    private readonly CheckBox startupCheck = new CheckBox();
    private readonly Label startupState = new Label();
    private readonly Dictionary<Control, ResponsiveLayoutSnapshot> responsiveLayout = new Dictionary<Control, ResponsiveLayoutSnapshot>();
    private readonly Dictionary<string, Font> responsiveFonts = new Dictionary<string, Font>();
    private readonly bool startHidden;
    private ContextMenuStrip trayMenu;
    private Button maximizeButton;
    private WallpaperSettings settings;
    private bool exitRequested;
    private volatile bool shutdownStarted;
    private bool disposableResourcesReleased;
    private bool initialVisibilityHandled;
    private bool updatingStartupControl;
    private volatile bool showRequested;
    private DateTime nextServiceRestartUtc = DateTime.MinValue;
    private DateTime nextStorageSyncUtc = DateTime.MinValue;
    private volatile bool storageSyncRunning;
    private string lastStorageSummary = "Đang kiểm tra hard-link và dung lượng...";
    private string lastObservedVideoName;
    private bool responsiveLayoutCaptured;

    public WallpaperControlForm(string userActivationSignalPath, bool launchAtStartup)
    {
        activationSignalPath = userActivationSignalPath;
        startHidden = launchAtStartup;
        settings = LoadSettings();
        BuildInterface();
        LoadSettingsIntoControls();
        RefreshStatus();
        UpdateStartupStatus();
        Shown += delegate { ShellIntegration.EnsureInstalled(); };
        if (startHidden) ShellIntegration.EnsureInstalled();
        statusTimer.Interval = 1000;
        statusTimer.Tick += delegate {
            if (!CanInteractWithForm) return;
            if (DateTime.UtcNow >= nextStorageSyncUtc) StartStorageSync();
            if (showRequested || File.Exists(activationSignalPath))
            {
                showRequested = false;
                TryDelete(activationSignalPath);
                ShowFromTray();
            }
            else RefreshStatus();
        };
        statusTimer.Start();
        StartStorageSync();
    }

    private bool CanInteractWithForm
    {
        get { return !shutdownStarted && !exitRequested && !Disposing && !IsDisposed; }
    }

    protected override void Dispose(bool disposing)
    {
        shutdownStarted = true;
        if (disposing && !disposableResourcesReleased)
        {
            disposableResourcesReleased = true;
            statusTimer.Stop();
            statusTimer.Dispose();
            showRequested = false;
            try { trayIcon.Visible = false; } catch { }
            try { trayIcon.ContextMenuStrip = null; } catch { }
            try { trayIcon.Dispose(); } catch { }
            if (trayMenu != null)
            {
                try { trayMenu.Close(); } catch { }
                trayMenu.Dispose();
                trayMenu = null;
            }
            foreach (Font scaledFont in responsiveFonts.Values) scaledFont.Dispose();
            responsiveFonts.Clear();
        }
        base.Dispose(disposing);
    }

    protected override void SetVisibleCore(bool value)
    {
        if (startHidden && !initialVisibilityHandled && value)
        {
            initialVisibilityHandled = true;
            base.SetVisibleCore(false);
            return;
        }
        base.SetVisibleCore(value);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg != NativeMethods.WM_NCHITTEST || WindowState != FormWindowState.Normal ||
            (int)message.Result != NativeMethods.HTCLIENT) return;

        long position = message.LParam.ToInt64();
        int screenX = unchecked((short)(position & 0xFFFF));
        int screenY = unchecked((short)((position >> 16) & 0xFFFF));
        Point clientPoint = PointToClient(new Point(screenX, screenY));
        bool left = clientPoint.X <= ResizeGrip;
        bool right = clientPoint.X >= ClientSize.Width - ResizeGrip;
        bool top = clientPoint.Y <= ResizeGrip;
        bool bottom = clientPoint.Y >= ClientSize.Height - ResizeGrip;

        if (left && top) message.Result = (IntPtr)NativeMethods.HTTOPLEFT;
        else if (right && top) message.Result = (IntPtr)NativeMethods.HTTOPRIGHT;
        else if (left && bottom) message.Result = (IntPtr)NativeMethods.HTBOTTOMLEFT;
        else if (right && bottom) message.Result = (IntPtr)NativeMethods.HTBOTTOMRIGHT;
        else if (left) message.Result = (IntPtr)NativeMethods.HTLEFT;
        else if (right) message.Result = (IntPtr)NativeMethods.HTRIGHT;
        else if (top) message.Result = (IntPtr)NativeMethods.HTTOP;
        else if (bottom) message.Result = (IntPtr)NativeMethods.HTBOTTOM;
    }

    private void BuildInterface()
    {
        Text = "Wallpaper Control";
        ClientSize = new Size(DesignWidth, DesignHeight);
        MinimumSize = new Size(760, 590);
        MaximumSize = Size.Empty;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        Icon = LoadAppIcon();
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        var titleBar = new Panel { Location = new Point(0, 0), Size = new Size(980, 62), BackColor = Theme.Surface };
        titleBar.MouseDown += DragWindow;
        var titleIcon = new PictureBox { Location = new Point(20, 14), Size = new Size(34, 34), SizeMode = PictureBoxSizeMode.Zoom, Image = LoadLogo() };
        titleBar.Controls.Add(titleIcon);
        var brand = MakeLabel("WALLPAPER CONTROL", 62, 13, 260, 23, Theme.Text, 12F, FontStyle.Bold);
        brand.MouseDown += DragWindow;
        titleBar.Controls.Add(brand);
        var subtitle = MakeLabel("LIVELY DESKTOP SYSTEM", 63, 36, 240, 18, Theme.Muted, 8F, FontStyle.Bold);
        subtitle.MouseDown += DragWindow;
        titleBar.Controls.Add(subtitle);
        topStatus.TextAlign = ContentAlignment.MiddleCenter;
        topStatus.Location = new Point(660, 18);
        topStatus.Size = new Size(160, 27);
        topStatus.BackColor = Color.FromArgb(31, 39, 47);
        topStatus.ForeColor = Theme.Green;
        topStatus.Font = new Font("Segoe UI Semibold", 8.5F);
        titleBar.Controls.Add(topStatus);

        var minimize = MakeWindowButton("—", 836);
        minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
        titleBar.Controls.Add(minimize);
        maximizeButton = MakeWindowButton("□", 884);
        maximizeButton.Font = new Font("Segoe UI Symbol", 11F);
        maximizeButton.Click += delegate { ToggleMaximize(); };
        titleBar.Controls.Add(maximizeButton);
        var close = MakeWindowButton("×", 932);
        close.Font = new Font("Segoe UI", 17F);
        close.Click += delegate { HideToTray(); };
        titleBar.Controls.Add(close);
        Controls.Add(titleBar);

        var sidebar = new Panel { Location = new Point(0, 62), Size = new Size(218, 698), BackColor = Color.FromArgb(10, 12, 18) };
        var logo = new PictureBox { Location = new Point(50, 28), Size = new Size(118, 118), SizeMode = PictureBoxSizeMode.Zoom, Image = LoadLogo() };
        sidebar.Controls.Add(logo);
        sidebar.Controls.Add(MakeLabel("COMMAND CENTER", 36, 153, 150, 24, Theme.Text, 10F, FontStyle.Bold));
        sidebar.Controls.Add(MakeLabel("DESKTOP PROFILE 01", 44, 178, 140, 20, Theme.Muted, 8F, FontStyle.Bold));

        var navActive = new Panel { Location = new Point(0, 230), Size = new Size(218, 54), BackColor = Color.FromArgb(36, 20, 31) };
        navActive.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 4, BackColor = Theme.Red });
        navActive.Controls.Add(MakeLabel("◈   TỔNG QUAN", 24, 16, 170, 24, Theme.Text, 9.5F, FontStyle.Bold));
        sidebar.Controls.Add(navActive);
        sidebar.Controls.Add(MakeLabel("▣   AUTO WALLPAPER", 25, 305, 175, 24, Theme.Muted, 9F, FontStyle.Bold));
        sidebar.Controls.Add(MakeLabel("◆   MÀU HÌNH NỀN", 25, 350, 175, 24, Theme.Muted, 9F, FontStyle.Bold));

        sidebar.Controls.Add(MakeLabel("WINDOWS", 24, 408, 170, 20, Theme.Muted, 8F, FontStyle.Bold));
        startupCheck.Text = "Khởi động cùng Windows";
        startupCheck.Location = new Point(24, 435);
        startupCheck.Size = new Size(174, 26);
        startupCheck.ForeColor = Theme.Text;
        startupCheck.BackColor = Color.Transparent;
        startupCheck.FlatStyle = FlatStyle.Flat;
        startupCheck.Cursor = Cursors.Hand;
        startupCheck.CheckedChanged += delegate { ChangeStartupSetting(); };
        sidebar.Controls.Add(startupCheck);
        startupState.Location = new Point(44, 463);
        startupState.Size = new Size(154, 42);
        startupState.Font = new Font("Segoe UI", 8F);
        sidebar.Controls.Add(startupState);

        var lockPanel = new Panel { Location = new Point(18, 570), Size = new Size(182, 72), BackColor = Color.FromArgb(17, 22, 28) };
        lockPanel.Controls.Add(MakeLabel("NVIDIA DRIVER", 14, 10, 150, 18, Theme.Muted, 8F, FontStyle.Bold));
        lockPanel.Controls.Add(MakeLabel("●  USER CONTROLLED", 14, 33, 160, 23, Theme.Green, 8.5F, FontStyle.Bold));
        sidebar.Controls.Add(lockPanel);
        sidebar.Controls.Add(MakeLabel("v2.6.2  ·  LIVE STATUS", 31, 662, 170, 18, Color.FromArgb(91, 98, 116), 7.5F, FontStyle.Bold));
        Controls.Add(sidebar);

        Controls.Add(MakeLabel("SYSTEM DASHBOARD", 252, 91, 350, 34, Theme.Text, 19F, FontStyle.Bold));
        Controls.Add(MakeLabel("Điều khiển hình nền, chuyển cảnh và màu video an toàn", 254, 126, 520, 24, Theme.Muted, 9.5F, FontStyle.Regular));

        var autoCard = new RogCard { Location = new Point(252, 168), Size = new Size(692, 130), AccentColor = Theme.Red };
        autoCard.Controls.Add(MakeLabel("AUTO WALLPAPER", 24, 13, 220, 24, Theme.Text, 11F, FontStyle.Bold));
        autoCard.Controls.Add(MakeLabel("LIVELY SHUFFLE SERVICE", 24, 38, 210, 18, Theme.Muted, 7.8F, FontStyle.Bold));
        autoState.Location = new Point(24, 65);
        autoState.Size = new Size(220, 25);
        autoState.Font = new Font("Segoe UI Semibold", 11F);
        autoCard.Controls.Add(autoState);
        autoDetail.Location = new Point(24, 94);
        autoDetail.Size = new Size(420, 22);
        autoDetail.ForeColor = Theme.Muted;
        autoCard.Controls.Add(autoDetail);
        autoButton.Location = new Point(492, 38);
        autoButton.Size = new Size(166, 54);
        autoButton.Click += delegate { ToggleAutoWallpaper(); };
        autoCard.Controls.Add(autoButton);
        Controls.Add(autoCard);

        var colorCard = new RogCard { Location = new Point(252, 310), Size = new Size(692, 310), AccentColor = Theme.Magenta };
        colorCard.Controls.Add(MakeLabel("WALLPAPER COLOR BOOST", 24, 17, 280, 24, Theme.Text, 11F, FontStyle.Bold));
        var safeBadge = MakeLabel("MPV ONLY  ·  SAFE", 500, 15, 158, 28, Theme.Cyan, 8.5F, FontStyle.Bold);
        safeBadge.BackColor = Color.FromArgb(16, 42, 51);
        safeBadge.TextAlign = ContentAlignment.MiddleCenter;
        colorCard.Controls.Add(safeBadge);
        colorCard.Controls.Add(MakeLabel("Chế độ", 24, 61, 80, 22, Theme.Muted, 9F, FontStyle.Bold));

        modeCombo.Items.AddRange(new object[] { "Tắt tăng màu", "Thủ công", "Dùng hồ sơ folder đã tạo" });
        modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        modeCombo.FlatStyle = FlatStyle.Flat;
        modeCombo.DrawMode = DrawMode.OwnerDrawFixed;
        modeCombo.ItemHeight = 24;
        modeCombo.BackColor = Theme.CardAlt;
        modeCombo.ForeColor = Theme.Text;
        modeCombo.Location = new Point(112, 57);
        modeCombo.Size = new Size(255, 30);
        modeCombo.DrawItem += DrawComboItem;
        modeCombo.SelectedIndexChanged += delegate { UpdateColorControls(); };
        colorCard.Controls.Add(modeCombo);

        resetButton.Text = "ĐẶT LẠI";
        resetButton.Location = new Point(382, 57);
        resetButton.Size = new Size(112, 31);
        resetButton.Click += delegate { ResetColor(); };
        colorCard.Controls.Add(resetButton);
        applyButton.Text = "ÁP DỤNG";
        applyButton.Location = new Point(510, 57);
        applyButton.Size = new Size(148, 31);
        applyButton.Click += delegate { ApplyColorSettings(); };
        colorCard.Controls.Add(applyButton);

        colorCard.Controls.Add(MakeLabel("Intensity", 24, 102, 80, 22, Theme.Muted, 9F, FontStyle.Bold));
        intensitySlider.Location = new Point(111, 94);
        intensitySlider.Size = new Size(405, 40);
        intensitySlider.ValueChanged += delegate { UpdateColorPreview(); };
        colorCard.Controls.Add(intensitySlider);
        intensityValue.Location = new Point(532, 101);
        intensityValue.Size = new Size(126, 25);
        intensityValue.TextAlign = ContentAlignment.MiddleRight;
        intensityValue.ForeColor = Theme.Magenta;
        intensityValue.Font = new Font("Segoe UI Semibold", 10F);
        colorCard.Controls.Add(intensityValue);

        colorCard.Controls.Add(MakeLabel("Saturation", 24, 140, 80, 22, Theme.Muted, 9F, FontStyle.Bold));
        saturationSlider.Location = new Point(111, 132);
        saturationSlider.Size = new Size(405, 40);
        saturationSlider.ValueChanged += delegate { UpdateColorPreview(); };
        colorCard.Controls.Add(saturationSlider);
        saturationValue.Location = new Point(532, 139);
        saturationValue.Size = new Size(126, 25);
        saturationValue.TextAlign = ContentAlignment.MiddleRight;
        saturationValue.ForeColor = Theme.Magenta;
        saturationValue.Font = new Font("Segoe UI Semibold", 10F);
        colorCard.Controls.Add(saturationValue);

        colorPreview.Location = new Point(24, 169);
        colorPreview.Size = new Size(634, 24);
        colorPreview.ForeColor = Theme.Cyan;
        colorPreview.Font = new Font("Segoe UI Semibold", 8.8F);
        colorPreview.TextAlign = ContentAlignment.MiddleCenter;
        colorCard.Controls.Add(colorPreview);

        saveProfileButton.Text = "LƯU & TẠO FOLDER";
        saveProfileButton.Location = new Point(24, 199);
        saveProfileButton.Size = new Size(210, 39);
        saveProfileButton.Click += delegate { SaveFolderProfile(); };
        colorCard.Controls.Add(saveProfileButton);
        profileState.Location = new Point(250, 198);
        profileState.Size = new Size(408, 42);
        profileState.ForeColor = Theme.Muted;
        profileState.Font = new Font("Segoe UI", 8.5F);
        profileState.TextAlign = ContentAlignment.MiddleLeft;
        colorCard.Controls.Add(profileState);

        colorState.Location = new Point(24, 244);
        colorState.Size = new Size(634, 22);
        colorState.ForeColor = Theme.Text;
        colorState.Font = new Font("Segoe UI Semibold", 9F);
        colorCard.Controls.Add(colorState);
        var safetyLine = MakeLabel("Videos = nguồn phát · folder phụ = nhãn hard-link · NVIDIA/game không bị thay đổi", 24, 273, 634, 25, Theme.Muted, 8.5F, FontStyle.Regular);
        safetyLine.BackColor = Color.FromArgb(17, 22, 30);
        safetyLine.TextAlign = ContentAlignment.MiddleCenter;
        colorCard.Controls.Add(safetyLine);
        Controls.Add(colorCard);

        var currentCard = new RogCard { Location = new Point(252, 632), Size = new Size(692, 112), AccentColor = Theme.Cyan };
        currentCard.Controls.Add(MakeLabel("ĐANG PHÁT  ·  CẤU HÌNH THỰC TẾ", 24, 12, 330, 22, Theme.Text, 10F, FontStyle.Bold));
        currentVideoName.Location = new Point(24, 38);
        currentVideoName.Size = new Size(634, 22);
        currentVideoName.ForeColor = Theme.Text;
        currentVideoName.Font = new Font("Segoe UI Semibold", 9F);
        currentCard.Controls.Add(currentVideoName);
        currentVideoConfig.Location = new Point(24, 61);
        currentVideoConfig.Size = new Size(634, 22);
        currentVideoConfig.ForeColor = Theme.Cyan;
        currentVideoConfig.Font = new Font("Segoe UI Semibold", 8.8F);
        currentCard.Controls.Add(currentVideoConfig);
        storageStatus.Location = new Point(24, 85);
        storageStatus.Size = new Size(634, 21);
        storageStatus.ForeColor = Theme.Muted;
        storageStatus.Font = new Font("Segoe UI", 8.2F);
        currentCard.Controls.Add(storageStatus);
        Controls.Add(currentCard);


        BuildTrayMenu();
        FormClosing += OnFormClosing;
        CaptureResponsiveLayout(this);
        responsiveLayoutCaptured = true;
        Resize += delegate { ApplyResponsiveLayout(); };
        ApplyResponsiveLayout();
    }

    private void CaptureResponsiveLayout(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            responsiveLayout[child] = new ResponsiveLayoutSnapshot {
                Bounds = child.Bounds,
                FontFamily = child.Font.FontFamily.Name,
                FontSize = child.Font.Size,
                FontStyle = child.Font.Style
            };
            CaptureResponsiveLayout(child);
        }
    }

    private void ApplyResponsiveLayout()
    {
        if (!responsiveLayoutCaptured || WindowState == FormWindowState.Minimized || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        float scale = Math.Min(ClientSize.Width / (float)DesignWidth, ClientSize.Height / (float)DesignHeight);
        scale = Math.Max(0.70F, Math.Min(1.15F, scale));
        int contentWidth = (int)Math.Round(DesignWidth * scale);
        int contentHeight = (int)Math.Round(DesignHeight * scale);
        int offsetX = Math.Max(0, (ClientSize.Width - contentWidth) / 2);
        int offsetY = Math.Max(0, (ClientSize.Height - contentHeight) / 2);

        SuspendLayout();
        ScaleControlTree(this, scale, offsetX, offsetY);
        if (maximizeButton != null) maximizeButton.Text = WindowState == FormWindowState.Maximized ? "❐" : "□";
        ResumeLayout(true);
    }

    private void ScaleControlTree(Control parent, float scale, int rootOffsetX, int rootOffsetY)
    {
        foreach (Control child in parent.Controls)
        {
            ResponsiveLayoutSnapshot snapshot;
            if (!responsiveLayout.TryGetValue(child, out snapshot)) continue;
            int offsetX = parent == this ? rootOffsetX : 0;
            int offsetY = parent == this ? rootOffsetY : 0;
            int x = offsetX + (int)Math.Round(snapshot.Bounds.X * scale);
            int y = offsetY + (int)Math.Round(snapshot.Bounds.Y * scale);
            int width = Math.Max(1, (int)Math.Round(snapshot.Bounds.Width * scale));
            int height = Math.Max(1, (int)Math.Round(snapshot.Bounds.Height * scale));
            if (child.Dock == DockStyle.None) child.Bounds = new Rectangle(x, y, width, height);
            else if (child.Dock == DockStyle.Left || child.Dock == DockStyle.Right) child.Width = width;
            else if (child.Dock == DockStyle.Top || child.Dock == DockStyle.Bottom) child.Height = height;

            float fontSize = (float)(Math.Round(Math.Max(6F, snapshot.FontSize * scale) * 4.0) / 4.0);
            if (Math.Abs(child.Font.Size - fontSize) > 0.15F)
            {
                string fontKey = snapshot.FontFamily + "|" + fontSize.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "|" + (int)snapshot.FontStyle;
                Font scaledFont;
                if (!responsiveFonts.TryGetValue(fontKey, out scaledFont))
                {
                    scaledFont = new Font(snapshot.FontFamily, fontSize, snapshot.FontStyle, GraphicsUnit.Point);
                    responsiveFonts[fontKey] = scaledFont;
                }
                child.Font = scaledFont;
            }
            ScaleControlTree(child, scale, 0, 0);
        }
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        ApplyResponsiveLayout();
    }

    private void BuildTrayMenu()
    {
        trayMenu = new ContextMenuStrip();
        trayMenu.BackColor = Theme.CardAlt;
        trayMenu.ForeColor = Theme.Text;
        trayMenu.Renderer = new ToolStripProfessionalRenderer(new RogColorTable());
        trayMenu.Items.Add("Mở Wallpaper Control", null, delegate { ShowFromTray(); });
        trayMenu.Items.Add("Bật / tắt Auto Wallpaper", null, delegate { RunTrayAction(ToggleAutoWallpaper); });
        trayMenu.Items.Add("Đặt lại màu hình nền", null, delegate { RunTrayAction(ResetColor); });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Thoát ứng dụng", null, delegate { ExitFromTray(); });
        trayIcon.Icon = LoadAppIcon();
        trayIcon.Text = "Wallpaper Control · MPV color only";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += delegate { ShowFromTray(); };
    }

    private void RunTrayAction(Action action)
    {
        if (!CanInteractWithForm) return;
        try { action(); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { if (CanInteractWithForm) throw; }
    }

    private void ExitFromTray()
    {
        if (!CanInteractWithForm) return;
        exitRequested = true;
        PrepareForShutdown();
        Close();
    }

    private static Label MakeLabel(string text, int x, int y, int width, int height, Color color, float size, FontStyle style)
    {
        return new Label {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(width, height),
            ForeColor = color,
            Font = new Font("Segoe UI", size, style),
            BackColor = Color.Transparent
        };
    }

    private static Button MakeWindowButton(string text, int x)
    {
        var button = new Button {
            Text = text,
            Location = new Point(x, 0),
            Size = new Size(48, 62),
            BackColor = Theme.Surface,
            ForeColor = Theme.Muted,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 12F),
            TabStop = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 20, 32);
        return button;
    }

    private void DragWindow(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (e.Clicks >= 2)
        {
            ToggleMaximize();
            return;
        }
        if (WindowState == FormWindowState.Maximized) return;
        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(Handle, NativeMethods.WM_NCLBUTTONDOWN, NativeMethods.HTCAPTION, 0);
    }

    private void DrawComboItem(object sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        e.Graphics.FillRectangle(new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(65, 26, 44) : Theme.CardAlt), e.Bounds);
        if (e.Index >= 0)
        {
            using (var brush = new SolidBrush(Theme.Text))
                e.Graphics.DrawString(modeCombo.Items[e.Index].ToString(), Font, brush, e.Bounds.X + 8, e.Bounds.Y + 3);
        }
    }

    private void LoadSettingsIntoControls()
    {
        modeCombo.SelectedIndex = settings.ColorMode == "Profiles" || settings.ColorMode == "PerFolder" ? 2 : settings.ColorMode == "Manual" ? 1 : 0;
        intensitySlider.Value = Math.Max(0, Math.Min(100, settings.Intensity));
        saturationSlider.Value = Math.Max(0, Math.Min(100, settings.Saturation));
        UpdateColorPreview();
        UpdateColorControls();
    }

    private void UpdateStartupStatus()
    {
        bool enabled = ShellIntegration.IsStartupEnabled();
        updatingStartupControl = true;
        startupCheck.Checked = enabled;
        updatingStartupControl = false;
        startupState.Text = enabled ? "Sẽ tự chạy ẩn ở khay hệ thống" : "App không tự chạy khi đăng nhập";
        startupState.ForeColor = enabled ? Theme.Green : Theme.Muted;
    }

    private void ChangeStartupSetting()
    {
        if (updatingStartupControl) return;
        try
        {
            ShellIntegration.SetStartupEnabled(startupCheck.Checked);
            UpdateStartupStatus();
        }
        catch (Exception ex)
        {
            UpdateStartupStatus();
            MessageBox.Show("Không thể thay đổi thiết lập khởi động cùng Windows:\n" + ex.Message,
                "Wallpaper Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static int CalculateMpvBoost(int intensity, int saturation)
    {
        return (int)Math.Round(Math.Max(0, Math.Min(100, intensity)) * 0.8 * (Math.Max(0, Math.Min(100, saturation)) / 100.0));
    }

    private void UpdateColorPreview()
    {
        intensityValue.Text = intensitySlider.Value + " / 100";
        saturationValue.Text = saturationSlider.Value + " / 100";
        colorPreview.Text = "Đầu ra MPV dự kiến: +" + CalculateMpvBoost(intensitySlider.Value, saturationSlider.Value) +
            "  ·  folder: RTX DYNAMIC VIBRANCE " + intensitySlider.Value + "-" + saturationSlider.Value;
    }

    private void UpdateColorControls()
    {
        bool manual = modeCombo.SelectedIndex == 1;
        intensitySlider.Enabled = manual;
        intensityValue.Enabled = manual;
        saturationSlider.Enabled = manual;
        saturationValue.Enabled = manual;
        saveProfileButton.Enabled = manual;
        if (manual) UpdateColorPreview();
        else if (modeCombo.SelectedIndex == 2) colorPreview.Text = "Hồ sơ folder đang điều khiển màu · xem cấu hình thực tế bên dưới";
        else colorPreview.Text = "Tăng màu đang tắt · đầu ra MPV +0";
    }

    private void RefreshStatus()
    {
        settings = LoadSettings();
        bool enabled = settings.AutoEnabled && !File.Exists(DisabledMarkerPath);
        bool running = Process.GetProcessesByName("LivelyShuffleLauncher").Length > 0;
        if (enabled && !running && DateTime.UtcNow >= nextServiceRestartUtc)
        {
            nextServiceRestartUtc = DateTime.UtcNow.AddSeconds(10);
            StartShuffle();
            running = Process.GetProcessesByName("LivelyShuffleLauncher").Length > 0;
        }
        autoState.Text = enabled ? "●  ĐANG BẬT" : "●  ĐANG TẮT";
        autoState.ForeColor = enabled ? Theme.Green : Theme.Red;
        autoDetail.Text = running ? "Dịch vụ đổi hình nền đang chạy" : "Dịch vụ đổi hình nền đã dừng";
        autoButton.Text = enabled ? "TẮT AUTO" : "BẬT AUTO";
        autoButton.SetPalette(enabled ? Theme.Red : Color.FromArgb(18, 155, 92), enabled ? Theme.Magenta : Theme.Green);
        colorState.Text = "Cấu hình: " + DescribeColor(settings);
        RefreshProfileState();
        RefreshCurrentVideoStatus();
        topStatus.Text = enabled ? "●  SYSTEM ONLINE" : "●  SYSTEM STANDBY";
        topStatus.ForeColor = enabled ? Theme.Green : Theme.Muted;
    }

    private void ToggleAutoWallpaper()
    {
        settings = LoadSettings();
        bool currentlyEnabled = settings.AutoEnabled && !File.Exists(DisabledMarkerPath);
        settings.AutoEnabled = !currentlyEnabled;
        SaveSettings(settings);
        if (settings.AutoEnabled)
        {
            TryDelete(DisabledMarkerPath);
            StartShuffle();
        }
        else
        {
            File.WriteAllText(DisabledMarkerPath, "Disabled by Wallpaper Control\r\n", new UTF8Encoding(false));
            StopShuffle();
        }
        RefreshStatus();
    }

    private void ApplyColorSettings()
    {
        settings = LoadSettings();
        settings.ColorMode = modeCombo.SelectedIndex == 1 ? "Manual" : modeCombo.SelectedIndex == 2 ? "Profiles" : "Off";
        settings.Intensity = intensitySlider.Value;
        settings.Saturation = saturationSlider.Value;
        SaveSettings(settings);
        RunApplyHelper();
        RefreshStatus();
    }

    private void ResetColor()
    {
        settings = LoadSettings();
        settings.ColorMode = "Off";
        settings.Intensity = 50;
        settings.Saturation = 100;
        SaveSettings(settings);
        modeCombo.SelectedIndex = 0;
        intensitySlider.Value = 50;
        saturationSlider.Value = 100;
        RunApplyHelper();
        RefreshStatus();
    }

    private static WallpaperSettings LoadSettings()
    {
        var result = new WallpaperSettings();
        if (!File.Exists(SettingsPath)) return result;
        string section = "";
        bool foundColorMode = false;
        foreach (string raw in File.ReadAllLines(SettingsPath))
        {
            string line = raw.Trim();
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
            int equals = line.IndexOf('=');
            if (equals < 1) continue;
            string key = line.Substring(0, equals).Trim();
            string value = line.Substring(equals + 1).Trim();
            if (section == "Wallpaper" && key == "AutoEnabled") result.AutoEnabled = value != "0";
            if (section == "Color" && key == "Mode") { result.ColorMode = value == "PerFolder" ? "Profiles" : value; foundColorMode = true; }
            if (section == "NVIDIA" && key == "Mode" && !foundColorMode) result.ColorMode = value;
            int parsed;
            if ((section == "Color" || section == "NVIDIA") && key == "Intensity" && int.TryParse(value, out parsed)) result.Intensity = parsed;
            if ((section == "Color" || section == "NVIDIA") && key == "Saturation" && int.TryParse(value, out parsed)) result.Saturation = parsed;
        }
        if (result.ColorMode == "PerFolder") result.ColorMode = "Profiles";
        if (result.ColorMode != "Off" && result.ColorMode != "Manual" && result.ColorMode != "Profiles") result.ColorMode = "Off";
        result.Intensity = Math.Max(0, Math.Min(100, result.Intensity));
        result.Saturation = Math.Max(0, Math.Min(100, result.Saturation));
        return result;
    }

    private static void SaveSettings(WallpaperSettings value)
    {
        string text = "[Wallpaper]\r\nAutoEnabled=" + (value.AutoEnabled ? "1" : "0") +
                      "\r\n\r\n[Color]\r\nMode=" + value.ColorMode +
                      "\r\nIntensity=" + Math.Max(0, Math.Min(100, value.Intensity)) +
                      "\r\nSaturation=" + Math.Max(0, Math.Min(100, value.Saturation)) +
                      "\r\n\r\n[Safety]\r\nDriverPolicy=Unchanged\r\n";
        File.WriteAllText(SettingsPath, text, new UTF8Encoding(false));
    }

    private static string DescribeColor(WallpaperSettings value)
    {
        if (value.ColorMode == "Manual") return "Thủ công " + value.Intensity + "/" + value.Saturation + " → MPV +" + CalculateMpvBoost(value.Intensity, value.Saturation) + " · áp dụng chung";
        if (value.ColorMode == "Profiles") return "Dùng hồ sơ folder · video chưa phân loại sẽ không tăng màu";
        return "Tắt tăng màu MPV · NVIDIA không bị thay đổi";
    }

    private static string ResolveWallpaperRoot()
    {
        string environmentPath = Path.Combine(AutomationRoot, "LivelyEnvironment.ini");
        try
        {
            if (File.Exists(environmentPath))
            {
                foreach (string line in File.ReadAllLines(environmentPath))
                {
                    if (!line.StartsWith("WallpaperRoot=", StringComparison.OrdinalIgnoreCase)) continue;
                    string value = line.Substring("WallpaperRoot=".Length).Trim();
                    if (Directory.Exists(value)) return value;
                }
            }
        }
        catch { }
        if (Directory.Exists(Path.Combine(ApplicationRoot, "Videos"))) return ApplicationRoot;
        return null;
    }

    private void RefreshProfileState()
    {
        string root = ResolveWallpaperRoot();
        if (string.IsNullOrEmpty(root))
        {
            profileState.Text = "Chưa tìm thấy thư mục dữ liệu Lively để lưu hồ sơ.";
            return;
        }
        int colorCount = 0;
        int offCount = 0;
        try
        {
            foreach (string directory in Directory.GetDirectories(root))
            {
                string name = Path.GetFileName(directory);
                if (name.Equals("NONE RTX DYANMIC VIBRANCE", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("NONE RTX DYNAMIC VIBRANCE", StringComparison.OrdinalIgnoreCase)) offCount++;
                else if (Regex.IsMatch(name, @"^RTX DYNAMIC VIBRANCE \d{1,3}-\d{1,3}$", RegexOptions.IgnoreCase)) colorCount++;
            }
        }
        catch { }
        profileState.Text = "Đã nhận " + (offCount + colorCount) + " folder cấu hình: " + offCount + " tắt màu + " + colorCount + " hồ sơ màu.";
    }

    private static bool IsProfileFolderName(string name)
    {
        return name.Equals("NONE RTX DYANMIC VIBRANCE", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("NONE RTX DYNAMIC VIBRANCE", StringComparison.OrdinalIgnoreCase) ||
               Regex.IsMatch(name, @"^RTX DYNAMIC VIBRANCE \d{1,3}-\d{1,3}$", RegexOptions.IgnoreCase);
    }

    private static string[] GetProfileDirectories(string root)
    {
        try
        {
            string[] directories = Directory.GetDirectories(root);
            return Array.FindAll(directories, delegate(string directory) { return IsProfileFolderName(Path.GetFileName(directory)); });
        }
        catch { return new string[0]; }
    }

    private static string ResolveLivelyDataRoot()
    {
        string environmentPath = Path.Combine(AutomationRoot, "LivelyEnvironment.ini");
        try
        {
            if (File.Exists(environmentPath))
            {
                foreach (string line in File.ReadAllLines(environmentPath))
                {
                    if (!line.StartsWith("DataRoot=", StringComparison.OrdinalIgnoreCase)) continue;
                    string value = line.Substring("DataRoot=".Length).Trim();
                    if (Directory.Exists(value)) return value;
                }
            }
        }
        catch { }

        var candidates = new List<string>();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(local, "Lively Wallpaper", "Settings.json"));
        candidates.Add(Path.Combine(local, "Temp", "Lively Wallpaper", "Settings.json"));
        string packages = Path.Combine(local, "Packages");
        try
        {
            if (Directory.Exists(packages))
                foreach (string directory in Directory.GetDirectories(packages, "*LivelyWallpaper*"))
                    candidates.Add(Path.Combine(directory, "LocalCache", "Local", "Lively Wallpaper", "Settings.json"));
        }
        catch { }

        string selected = null;
        DateTime selectedTime = DateTime.MinValue;
        foreach (string candidate in candidates)
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                DateTime writeTime = File.GetLastWriteTimeUtc(candidate);
                if (selected == null || writeTime > selectedTime) { selected = candidate; selectedTime = writeTime; }
            }
            catch { }
        }
        return selected == null ? null : Path.GetDirectoryName(selected);
    }

    private static string ReadCurrentVideoFromLively()
    {
        string dataRoot = ResolveLivelyDataRoot();
        if (string.IsNullOrEmpty(dataRoot)) return null;
        string layoutPath = Path.Combine(dataRoot, "WallpaperLayout.json");
        if (!File.Exists(layoutPath)) return null;
        try
        {
            string layoutJson = File.ReadAllText(layoutPath, Encoding.UTF8);
            MatchCollection entries = Regex.Matches(layoutJson,
                @"""isStale""\s*:\s*(true|false)[\s\S]*?""LivelyInfoPath""\s*:\s*""((?:\\.|[^""])*)""",
                RegexOptions.IgnoreCase);
            foreach (Match entry in entries)
            {
                if (entry.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase)) continue;
                string infoPath = UnescapeJsonString(entry.Groups[2].Value);
                string infoFile = Path.Combine(infoPath, "LivelyInfo.json");
                if (!File.Exists(infoFile)) continue;
                Match fileNameMatch = Regex.Match(File.ReadAllText(infoFile, Encoding.UTF8),
                    @"""FileName""\s*:\s*""((?:\\.|[^""])*)""", RegexOptions.IgnoreCase);
                if (!fileNameMatch.Success) continue;
                string fileName = UnescapeJsonString(fileNameMatch.Groups[1].Value);
                if (!string.IsNullOrEmpty(fileName)) return Path.GetFileName(fileName);
            }
        }
        catch { }
        return null;
    }

    private static string UnescapeJsonString(string value)
    {
        return value.Replace("\\/", "/").Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    private static string ReadCurrentVideoName()
    {
        string shuffleVideo = ReadShuffleStateVideoName();
        if (Process.GetProcessesByName("LivelyShuffleLauncher").Length > 0 && !string.IsNullOrEmpty(shuffleVideo))
            return shuffleVideo;
        string livelyVideo = ReadCurrentVideoFromLively();
        if (!string.IsNullOrEmpty(livelyVideo)) return livelyVideo;
        return shuffleVideo;
    }

    private static string ReadShuffleStateVideoName()
    {
        string statePath = Path.Combine(AutomationRoot, "shuffle-state.json");
        try
        {
            if (!File.Exists(statePath)) return null;
            Match match = Regex.Match(File.ReadAllText(statePath, Encoding.UTF8), "\\\"Current\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"");
            if (!match.Success) return null;
            return match.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
        catch { return null; }
    }

    private static bool TryGetVideoProfile(string root, string videoName, out bool enabled, out int intensity, out int saturation, out string folderName)
    {
        enabled = false;
        intensity = 0;
        saturation = 100;
        folderName = null;
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(videoName)) return false;

        foreach (string directory in GetProfileDirectories(root))
        {
            string name = Path.GetFileName(directory);
            if (!name.StartsWith("NONE ", StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(Path.Combine(directory, videoName))) continue;
            folderName = name;
            return true;
        }
        foreach (string directory in GetProfileDirectories(root))
        {
            string name = Path.GetFileName(directory);
            Match match = Regex.Match(name, @"^RTX DYNAMIC VIBRANCE (\d{1,3})-(\d{1,3})$", RegexOptions.IgnoreCase);
            if (!match.Success || !File.Exists(Path.Combine(directory, videoName))) continue;
            enabled = true;
            intensity = Math.Max(0, Math.Min(100, int.Parse(match.Groups[1].Value)));
            saturation = Math.Max(0, Math.Min(100, int.Parse(match.Groups[2].Value)));
            folderName = name;
            return true;
        }
        return false;
    }

    private void RefreshCurrentVideoStatus()
    {
        string videoName = ReadCurrentVideoName();
        storageStatus.Text = "Lưu trữ: " + lastStorageSummary;
        if (string.IsNullOrEmpty(videoName))
        {
            currentVideoName.Text = "Video: chưa xác định";
            currentVideoConfig.Text = "Cấu hình thực tế: chưa có trạng thái phát từ Auto Wallpaper";
            return;
        }

        if (!string.Equals(lastObservedVideoName, videoName, StringComparison.OrdinalIgnoreCase))
        {
            lastObservedVideoName = videoName;
            RunApplyHelper(8, 250);
        }

        currentVideoName.Text = "Video: " + videoName;
        if (settings.ColorMode == "Off")
        {
            currentVideoConfig.Text = "Cấu hình thực tế: Tắt tăng màu · MPV +0";
            return;
        }
        if (settings.ColorMode == "Manual")
        {
            currentVideoConfig.Text = "Cấu hình thực tế: Thủ công · Intensity " + settings.Intensity + " · Saturation " + settings.Saturation + " · MPV +" + CalculateMpvBoost(settings.Intensity, settings.Saturation);
            return;
        }

        bool profileEnabled;
        int profileIntensity;
        int profileSaturation;
        string profileFolder;
        if (!TryGetVideoProfile(ResolveWallpaperRoot(), videoName, out profileEnabled, out profileIntensity, out profileSaturation, out profileFolder))
        {
            currentVideoConfig.Text = "Cấu hình thực tế: Chưa phân loại · MPV +0";
        }
        else if (!profileEnabled)
        {
            currentVideoConfig.Text = "Cấu hình thực tế: NONE · Tắt tăng màu · MPV +0";
        }
        else
        {
            currentVideoConfig.Text = "Cấu hình thực tế: " + profileFolder + " · Intensity " + profileIntensity + " · Saturation " + profileSaturation + " · MPV +" + CalculateMpvBoost(profileIntensity, profileSaturation);
        }
    }

    private void StartStorageSync()
    {
        if (storageSyncRunning) return;
        storageSyncRunning = true;
        nextStorageSyncUtc = DateTime.UtcNow.AddSeconds(15);
        ThreadPool.QueueUserWorkItem(delegate
        {
            try { lastStorageSummary = SynchronizeProfileStorage(); }
            catch (Exception ex) { lastStorageSummary = "không thể đồng bộ: " + ex.Message; }
            finally { storageSyncRunning = false; }
        });
    }

    private static string GetPhysicalFileId(string path)
    {
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                ByHandleFileInformation info;
                if (!NativeMethods.GetFileInformationByHandle(stream.SafeFileHandle, out info)) return null;
                return info.VolumeSerialNumber.ToString("X8") + ":" + info.FileIndexHigh.ToString("X8") + info.FileIndexLow.ToString("X8");
            }
        }
        catch { return null; }
    }

    private static bool FilesHaveEqualContent(string first, string second)
    {
        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);
        if (firstInfo.Length != secondInfo.Length) return false;
        const int bufferSize = 1024 * 1024;
        byte[] firstBuffer = new byte[bufferSize];
        byte[] secondBuffer = new byte[bufferSize];
        using (var firstStream = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var secondStream = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            while (true)
            {
                int firstRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length);
                int secondRead = secondStream.Read(secondBuffer, 0, secondBuffer.Length);
                if (firstRead != secondRead) return false;
                if (firstRead == 0) return true;
                for (int index = 0; index < firstRead; index++) if (firstBuffer[index] != secondBuffer[index]) return false;
            }
        }
    }

    private static string SynchronizeProfileStorage()
    {
        string root = ResolveWallpaperRoot();
        if (string.IsNullOrEmpty(root)) return "chưa tìm thấy WallpaperDir";
        string canonicalRoot = Path.Combine(root, "Videos");
        Directory.CreateDirectory(canonicalRoot);
        int created = 0;
        int deduplicated = 0;
        int linked = 0;
        int conflicts = 0;
        int errors = 0;

        foreach (string profileDirectory in GetProfileDirectories(root))
        {
            string[] files;
            try { files = Directory.GetFiles(profileDirectory, "*.mp4", SearchOption.TopDirectoryOnly); }
            catch { errors++; continue; }
            foreach (string profileVideo in files)
            {
                string canonicalVideo = Path.Combine(canonicalRoot, Path.GetFileName(profileVideo));
                try
                {
                    if (!File.Exists(canonicalVideo))
                    {
                        if (NativeMethods.CreateHardLink(canonicalVideo, profileVideo, IntPtr.Zero)) created++; else errors++;
                        continue;
                    }
                    string profileId = GetPhysicalFileId(profileVideo);
                    string canonicalId = GetPhysicalFileId(canonicalVideo);
                    if (!string.IsNullOrEmpty(profileId) && profileId == canonicalId) { linked++; continue; }
                    if (!FilesHaveEqualContent(profileVideo, canonicalVideo)) { conflicts++; continue; }

                    File.Delete(profileVideo);
                    if (NativeMethods.CreateHardLink(profileVideo, canonicalVideo, IntPtr.Zero)) deduplicated++;
                    else
                    {
                        File.Copy(canonicalVideo, profileVideo, false);
                        errors++;
                    }
                }
                catch { errors++; }
            }
        }
        string result = linked + " hard-link hợp lệ";
        if (created > 0) result += " · " + created + " video đã liên kết về Videos";
        if (deduplicated > 0) result += " · " + deduplicated + " bản sao đã giải phóng";
        if (conflicts > 0) result += " · " + conflicts + " trùng tên khác nội dung";
        if (errors > 0) result += " · " + errors + " lỗi";
        return result;
    }

    private void SaveFolderProfile()
    {
        int intensity = intensitySlider.Value;
        int saturation = saturationSlider.Value;
        string root = ResolveWallpaperRoot();
        if (string.IsNullOrEmpty(root))
        {
            MessageBox.Show("Không tìm thấy thư mục dữ liệu Lively Wallpaper.", "Wallpaper Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string folderName = "RTX DYNAMIC VIBRANCE " + intensity + "-" + saturation;
        string folderPath = Path.Combine(root, folderName);
        settings = LoadSettings();
        settings.ColorMode = "Manual";
        settings.Intensity = intensity;
        settings.Saturation = saturation;
        SaveSettings(settings);
        modeCombo.SelectedIndex = 1;
        RunApplyHelper();

        if (Directory.Exists(folderPath))
        {
            MessageBox.Show("Hồ sơ đã tồn tại:\n" + folderName + "\n\nBạn có thể đặt hard-link video vào folder này.", "Hồ sơ màu", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshStatus();
            return;
        }

        DialogResult choice = MessageBox.Show(
            "Lưu cấu hình Intensity " + intensity + " / Saturation " + saturation + " và tạo folder:\n\n" + folderName +
            "\n\nVideo được đặt bằng hard-link trong folder này sẽ tự dùng đúng cấu hình trên. Tạo folder ngay?",
            "Tạo hồ sơ màu", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (choice != DialogResult.Yes) { RefreshStatus(); return; }

        try
        {
            Directory.CreateDirectory(folderPath);
            MessageBox.Show("Đã tạo hồ sơ:\n" + folderPath + "\n\nVideo gốc vẫn nên giữ trong folder Videos; folder hồ sơ chỉ chứa hard-link.", "Đã lưu hồ sơ", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Không thể tạo folder hồ sơ:\n" + ex.Message, "Wallpaper Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        RefreshStatus();
    }

    private static void StopShuffle()
    {
        foreach (Process process in Process.GetProcessesByName("LivelyShuffleLauncher"))
        {
            try { process.Kill(); process.WaitForExit(2000); } catch { }
        }
        if (File.Exists(ServicePidPath))
        {
            int id;
            if (int.TryParse(File.ReadAllText(ServicePidPath).Trim(), out id))
            {
                try { Process.GetProcessById(id).Kill(); } catch { }
            }
            TryDelete(ServicePidPath);
        }
    }

    private static void StartShuffle()
    {
        if (Process.GetProcessesByName("LivelyShuffleLauncher").Length > 0) return;
        try
        {
            Process.Start(new ProcessStartInfo(LauncherPath) { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Không thể bật Auto Wallpaper:\n" + ex.Message, "Wallpaper Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void RunApplyHelper(int retryCount = 1, int retryDelayMilliseconds = 250)
    {
        try
        {
            string arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + ApplyHelperPath +
                "\" -Silent -RetryCount " + Math.Max(1, retryCount) + " -RetryDelayMilliseconds " + Math.Max(50, retryDelayMilliseconds);
            var start = new ProcessStartInfo("powershell.exe", arguments) {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(start);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Không thể áp dụng màu hình nền:\n" + ex.Message, "Wallpaper Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void ShowFromTray()
    {
        if (!CanInteractWithForm) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new MethodInvoker(ShowFromTray)); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            return;
        }

        if (!CanInteractWithForm) return;
        try
        {
            trayIcon.Visible = true;
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            RefreshStatus();
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { if (CanInteractWithForm) throw; }
    }

    private void HideToTray()
    {
        if (!CanInteractWithForm) return;
        ShowInTaskbar = false;
        Hide();
        trayIcon.Visible = true;
    }

    public void RequestShowFromAnotherInstance()
    {
        if (!CanInteractWithForm) return;
        showRequested = true;
    }

    private void PrepareForShutdown()
    {
        if (shutdownStarted) return;
        shutdownStarted = true;
        showRequested = false;
        statusTimer.Stop();
        TryDelete(activationSignalPath);
        if (trayMenu != null)
        {
            trayMenu.Enabled = false;
            try { trayMenu.Close(); } catch { }
        }
        try { trayIcon.Visible = false; } catch { }
    }

    private void OnFormClosing(object sender, FormClosingEventArgs e)
    {
        if (!exitRequested &&
            e.CloseReason != CloseReason.WindowsShutDown &&
            e.CloseReason != CloseReason.TaskManagerClosing &&
            e.CloseReason != CloseReason.ApplicationExitCall)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        exitRequested = true;
        PrepareForShutdown();
    }

    private static Image LoadLogo()
    {
        try
        {
            using (var source = Image.FromFile(LogoPath)) return new Bitmap(source);
        }
        catch { return null; }
    }

    private static Icon LoadAppIcon()
    {
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { return SystemIcons.Application; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

internal static class Program
{
    private static string GetUserScopeKey()
    {
        try
        {
            SecurityIdentifier sid = WindowsIdentity.GetCurrent().User;
            if (sid != null) return sid.Value.Replace('-', '_');
        }
        catch { }
        return Environment.UserName.Replace(' ', '_').Replace('\\', '_');
    }

    [STAThread]
    private static void Main(string[] args)
    {
        NativeMethods.SetCurrentProcessExplicitAppUserModelID(ShellIntegration.AppUserModelId);
        if (args.Length > 0 && string.Equals(args[0], "--install-shell", StringComparison.OrdinalIgnoreCase))
        {
            ShellIntegration.EnsureInstalled();
            return;
        }

        bool launchAtStartup = false;
        foreach (string argument in args)
            if (string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase)) launchAtStartup = true;

        string userScope = GetUserScopeKey();
        string mutexName = @"Local\WallpaperControlSingleInstance_" + userScope;
        string pipeName = "WallpaperControlActivation_" + userScope;
        string activationSignalPath = Path.Combine(WallpaperControlForm.AutomationRoot, "wallpaper-control." + userScope + ".show");
        bool firstInstance;
        using (var instanceLock = new Mutex(true, mutexName, out firstInstance))
        {
            if (!firstInstance)
            {
                if (launchAtStartup) return;
                try { File.WriteAllText(activationSignalPath, "show", new UTF8Encoding(false)); } catch { }
                try
                {
                    using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                    {
                        client.Connect(1500);
                        client.WriteByte(1);
                        client.Flush();
                    }
                }
                catch
                {
                    IntPtr existingWindow = NativeMethods.FindWindow(null, "Wallpaper Control");
                    if (existingWindow != IntPtr.Zero)
                    {
                        NativeMethods.ShowWindow(existingWindow, 9);
                        NativeMethods.SetForegroundWindow(existingWindow);
                    }
                }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { if (File.Exists(activationSignalPath)) File.Delete(activationSignalPath); } catch { }
            var form = new WallpaperControlForm(activationSignalPath, launchAtStartup);
            var activationThread = new Thread(delegate()
            {
                while (!form.IsDisposed)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None))
                        {
                            server.WaitForConnection();
                            if (server.ReadByte() >= 0 && !form.IsDisposed)
                                form.RequestShowFromAnotherInstance();
                        }
                    }
                    catch { if (!form.IsDisposed) Thread.Sleep(150); }
                }
            });
            activationThread.IsBackground = true;
            activationThread.Name = "WallpaperControlActivation_" + userScope;
            activationThread.Start();
            Application.Run(form);
        }
    }
}
