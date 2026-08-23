using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Wallpaper Control")]
[assembly: AssemblyDescription("Lively Wallpaper automation and safe MPV color control")]
[assembly: AssemblyProduct("Wallpaper Control")]
[assembly: AssemblyCompany("Wallpaper Control Community")]
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

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
    public const int HTCAPTION = 0x2;

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

    public static void EnsureInstalled()
    {
        try
        {
            string exe = Application.ExecutablePath;
            string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Wallpaper Control.lnk");
            string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Wallpaper Control.lnk");
            CreateShortcut(startMenu, exe);
            CreateShortcut(desktop, exe);

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

    private static void CreateShortcut(string shortcutPath, string exe)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
        var link = (IShellLinkW)new ShellLinkCom();
        link.SetPath(exe);
        link.SetWorkingDirectory(Path.GetDirectoryName(exe));
        link.SetDescription("Điều khiển hình nền động Lively");
        link.SetIconLocation(exe, 0);
        link.SetShowCmd(1);

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
    private readonly Label topStatus = new Label();
    private readonly RogButton autoButton = new RogButton(Theme.Red, Theme.Magenta);
    private readonly RogButton applyButton = new RogButton(Theme.Red, Theme.Magenta);
    private readonly RogButton resetButton = new RogButton(Color.FromArgb(43, 47, 60), Color.FromArgb(61, 66, 83));
    private readonly ComboBox modeCombo = new ComboBox();
    private readonly RogSlider intensitySlider = new RogSlider();
    private readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer();
    private readonly NotifyIcon trayIcon = new NotifyIcon();
    private WallpaperSettings settings;
    private bool exitRequested;
    private volatile bool showRequested;
    private DateTime nextServiceRestartUtc = DateTime.MinValue;

    public WallpaperControlForm(string userActivationSignalPath)
    {
        activationSignalPath = userActivationSignalPath;
        settings = LoadSettings();
        BuildInterface();
        LoadSettingsIntoControls();
        RefreshStatus();
        Shown += delegate { ShellIntegration.EnsureInstalled(); };
        statusTimer.Interval = 1000;
        statusTimer.Tick += delegate {
            if (showRequested || File.Exists(activationSignalPath))
            {
                showRequested = false;
                TryDelete(activationSignalPath);
                ShowFromTray();
            }
            else RefreshStatus();
        };
        statusTimer.Start();
    }

    private void BuildInterface()
    {
        Text = "Wallpaper Control";
        ClientSize = new Size(980, 620);
        MinimumSize = MaximumSize = Size;
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
        topStatus.Location = new Point(700, 18);
        topStatus.Size = new Size(160, 27);
        topStatus.BackColor = Color.FromArgb(31, 39, 47);
        topStatus.ForeColor = Theme.Green;
        topStatus.Font = new Font("Segoe UI Semibold", 8.5F);
        titleBar.Controls.Add(topStatus);

        var minimize = MakeWindowButton("—", 884);
        minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
        titleBar.Controls.Add(minimize);
        var close = MakeWindowButton("×", 932);
        close.Font = new Font("Segoe UI", 17F);
        close.Click += delegate { HideToTray(); };
        titleBar.Controls.Add(close);
        Controls.Add(titleBar);

        var sidebar = new Panel { Location = new Point(0, 62), Size = new Size(218, 558), BackColor = Color.FromArgb(10, 12, 18) };
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

        var lockPanel = new Panel { Location = new Point(18, 440), Size = new Size(182, 72), BackColor = Color.FromArgb(17, 22, 28) };
        lockPanel.Controls.Add(MakeLabel("NVIDIA DRIVER", 14, 10, 150, 18, Theme.Muted, 8F, FontStyle.Bold));
        lockPanel.Controls.Add(MakeLabel("●  LOCKED OFF", 14, 33, 150, 23, Theme.Green, 9.5F, FontStyle.Bold));
        sidebar.Controls.Add(lockPanel);
        sidebar.Controls.Add(MakeLabel("v2.0  ·  GAMING EDITION", 31, 526, 170, 18, Color.FromArgb(91, 98, 116), 7.5F, FontStyle.Bold));
        Controls.Add(sidebar);

        Controls.Add(MakeLabel("SYSTEM DASHBOARD", 252, 91, 350, 34, Theme.Text, 19F, FontStyle.Bold));
        Controls.Add(MakeLabel("Điều khiển hình nền, chuyển cảnh và màu video an toàn", 254, 126, 520, 24, Theme.Muted, 9.5F, FontStyle.Regular));

        var autoCard = new RogCard { Location = new Point(252, 168), Size = new Size(692, 154), AccentColor = Theme.Red };
        autoCard.Controls.Add(MakeLabel("AUTO WALLPAPER", 24, 18, 220, 24, Theme.Text, 11F, FontStyle.Bold));
        autoCard.Controls.Add(MakeLabel("LIVELY SHUFFLE SERVICE", 24, 44, 210, 18, Theme.Muted, 7.8F, FontStyle.Bold));
        autoState.Location = new Point(24, 78);
        autoState.Size = new Size(220, 25);
        autoState.Font = new Font("Segoe UI Semibold", 11F);
        autoCard.Controls.Add(autoState);
        autoDetail.Location = new Point(24, 108);
        autoDetail.Size = new Size(420, 22);
        autoDetail.ForeColor = Theme.Muted;
        autoCard.Controls.Add(autoDetail);
        autoButton.Location = new Point(492, 51);
        autoButton.Size = new Size(166, 54);
        autoButton.Click += delegate { ToggleAutoWallpaper(); };
        autoCard.Controls.Add(autoButton);
        Controls.Add(autoCard);

        var colorCard = new RogCard { Location = new Point(252, 340), Size = new Size(692, 231), AccentColor = Theme.Magenta };
        colorCard.Controls.Add(MakeLabel("WALLPAPER COLOR BOOST", 24, 17, 280, 24, Theme.Text, 11F, FontStyle.Bold));
        var safeBadge = MakeLabel("MPV ONLY  ·  SAFE", 500, 15, 158, 28, Theme.Cyan, 8.5F, FontStyle.Bold);
        safeBadge.BackColor = Color.FromArgb(16, 42, 51);
        safeBadge.TextAlign = ContentAlignment.MiddleCenter;
        colorCard.Controls.Add(safeBadge);
        colorCard.Controls.Add(MakeLabel("Chế độ", 24, 61, 80, 22, Theme.Muted, 9F, FontStyle.Bold));

        modeCombo.Items.AddRange(new object[] { "Tắt tăng màu", "Theo thư mục 50 / 70 / 100", "Thủ công" });
        modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        modeCombo.FlatStyle = FlatStyle.Flat;
        modeCombo.DrawMode = DrawMode.OwnerDrawFixed;
        modeCombo.ItemHeight = 24;
        modeCombo.BackColor = Theme.CardAlt;
        modeCombo.ForeColor = Theme.Text;
        modeCombo.Location = new Point(112, 57);
        modeCombo.Size = new Size(255, 30);
        modeCombo.DrawItem += DrawComboItem;
        modeCombo.SelectedIndexChanged += delegate { UpdateManualControls(); };
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

        colorCard.Controls.Add(MakeLabel("Intensity", 24, 110, 80, 22, Theme.Muted, 9F, FontStyle.Bold));
        intensitySlider.Location = new Point(111, 102);
        intensitySlider.Size = new Size(405, 40);
        intensitySlider.ValueChanged += delegate { UpdateIntensityText(); };
        colorCard.Controls.Add(intensitySlider);
        intensityValue.Location = new Point(532, 109);
        intensityValue.Size = new Size(126, 25);
        intensityValue.TextAlign = ContentAlignment.MiddleRight;
        intensityValue.ForeColor = Theme.Magenta;
        intensityValue.Font = new Font("Segoe UI Semibold", 10F);
        colorCard.Controls.Add(intensityValue);

        colorState.Location = new Point(24, 153);
        colorState.Size = new Size(634, 22);
        colorState.ForeColor = Theme.Text;
        colorState.Font = new Font("Segoe UI Semibold", 9F);
        colorCard.Controls.Add(colorState);
        var safetyLine = MakeLabel("NVIDIA RTX Dynamic Vibrance luôn tắt · màu chỉ áp dụng cho video MPV, không ảnh hưởng game/app", 24, 187, 634, 25, Theme.Muted, 8.5F, FontStyle.Regular);
        safetyLine.BackColor = Color.FromArgb(17, 22, 30);
        safetyLine.TextAlign = ContentAlignment.MiddleCenter;
        colorCard.Controls.Add(safetyLine);
        Controls.Add(colorCard);

        Controls.Add(MakeLabel("Nút X thu Control xuống tray · dùng menu tray để thoát hoàn toàn", 252, 584, 500, 20, Color.FromArgb(91, 98, 116), 8F, FontStyle.Regular));

        BuildTrayMenu();
        FormClosing += OnFormClosing;
        FormClosed += delegate { trayIcon.Visible = false; trayIcon.Dispose(); };
    }

    private void BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.BackColor = Theme.CardAlt;
        menu.ForeColor = Theme.Text;
        menu.Renderer = new ToolStripProfessionalRenderer(new RogColorTable());
        menu.Items.Add("Mở Wallpaper Control", null, delegate { ShowFromTray(); });
        menu.Items.Add("Bật / tắt Auto Wallpaper", null, delegate { ToggleAutoWallpaper(); });
        menu.Items.Add("Đặt lại màu hình nền", null, delegate { ResetColor(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Thoát ứng dụng", null, delegate { exitRequested = true; Close(); });
        trayIcon.Icon = LoadAppIcon();
        trayIcon.Text = "Wallpaper Control · NVIDIA Off";
        trayIcon.ContextMenuStrip = menu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += delegate { ShowFromTray(); };
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
        modeCombo.SelectedIndex = settings.ColorMode == "PerFolder" ? 1 : settings.ColorMode == "Manual" ? 2 : 0;
        intensitySlider.Value = Math.Max(0, Math.Min(100, settings.Intensity));
        UpdateIntensityText();
        UpdateManualControls();
    }

    private void UpdateIntensityText()
    {
        int boost = (int)Math.Round(intensitySlider.Value * 0.35);
        intensityValue.Text = intensitySlider.Value + " / 100   →  +" + boost;
    }

    private void UpdateManualControls()
    {
        bool manual = modeCombo.SelectedIndex == 2;
        intensitySlider.Enabled = manual;
        intensityValue.Enabled = manual;
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
        settings.ColorMode = modeCombo.SelectedIndex == 1 ? "PerFolder" : modeCombo.SelectedIndex == 2 ? "Manual" : "Off";
        settings.Intensity = intensitySlider.Value;
        settings.Saturation = 100;
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
            if (section == "Color" && key == "Mode") { result.ColorMode = value; foundColorMode = true; }
            if (section == "NVIDIA" && key == "Mode" && !foundColorMode) result.ColorMode = value;
            int parsed;
            if ((section == "Color" || section == "NVIDIA") && key == "Intensity" && int.TryParse(value, out parsed)) result.Intensity = parsed;
        }
        if (result.ColorMode != "Off" && result.ColorMode != "PerFolder" && result.ColorMode != "Manual") result.ColorMode = "Off";
        return result;
    }

    private static void SaveSettings(WallpaperSettings value)
    {
        string text = "[Wallpaper]\r\nAutoEnabled=" + (value.AutoEnabled ? "1" : "0") +
                      "\r\n\r\n[Color]\r\nMode=" + value.ColorMode +
                      "\r\nIntensity=" + Math.Max(0, Math.Min(100, value.Intensity)) +
                      "\r\nSaturation=100\r\n\r\n[Safety]\r\nNvidiaDynamicVibrance=Off\r\n";
        File.WriteAllText(SettingsPath, text, new UTF8Encoding(false));
    }

    private static string DescribeColor(WallpaperSettings value)
    {
        if (value.ColorMode == "Manual") return "Thủ công " + value.Intensity + "/100 · Saturation 100 · MPV only";
        if (value.ColorMode == "PerFolder") return "Theo thư mục 50/70/100 · Saturation 100 · MPV only";
        return "Tắt tăng màu · NVIDIA driver vẫn khóa Off";
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

    private static void RunApplyHelper()
    {
        try
        {
            var start = new ProcessStartInfo("powershell.exe", "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + ApplyHelperPath + "\" -Silent") {
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
        trayIcon.Visible = true;
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        RefreshStatus();
    }

    private void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
        trayIcon.Visible = true;
    }

    public void RequestShowFromAnotherInstance()
    {
        showRequested = true;
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
        trayIcon.Visible = false;
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

        string userScope = GetUserScopeKey();
        string mutexName = @"Local\WallpaperControlSingleInstance_" + userScope;
        string pipeName = "WallpaperControlActivation_" + userScope;
        string activationSignalPath = Path.Combine(WallpaperControlForm.AutomationRoot, "wallpaper-control." + userScope + ".show");
        bool firstInstance;
        using (var instanceLock = new Mutex(true, mutexName, out firstInstance))
        {
            if (!firstInstance)
            {
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
            var form = new WallpaperControlForm(activationSignalPath);
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
