using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Web.Script.Serialization;

[assembly: AssemblyTitle("Wallpaper Control Setup")]
[assembly: AssemblyDescription("Portable one-file setup for Lively Wallpaper Control")]
[assembly: AssemblyProduct("Wallpaper Control")]
[assembly: AssemblyCompany("Wallpaper Control Community")]
[assembly: AssemblyVersion("2.4.0.0")]
[assembly: AssemblyFileVersion("2.4.0.0")]

internal sealed class LivelyDetection
{
    public string SettingsPath;
    public string DataRoot;
    public string WallpaperRoot;
    public string LivelyExePath;
    public string Distribution;
    public string AppUserModelId = "12030rocksdanister.LivelyWallpaper_97hta09mmv6hy!App";
    public bool Found { get { return !string.IsNullOrEmpty(SettingsPath) && !string.IsNullOrEmpty(WallpaperRoot); } }
}

internal static class LivelyDetector
{
    public static LivelyDetection Detect()
    {
        var result = new LivelyDetection();
        var candidates = new List<string>();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        candidates.Add(Path.Combine(local, "Lively Wallpaper", "Settings.json"));
        candidates.Add(Path.Combine(local, "Temp", "Lively Wallpaper", "Settings.json"));
        string packages = Path.Combine(local, "Packages");
        try
        {
            if (Directory.Exists(packages))
            {
                foreach (string directory in Directory.GetDirectories(packages, "*LivelyWallpaper*"))
                    candidates.Add(Path.Combine(directory, "LocalCache", "Local", "Lively Wallpaper", "Settings.json"));
            }
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
        if (selected == null) return result;

        try
        {
            var serializer = new JavaScriptSerializer();
            var values = serializer.DeserializeObject(File.ReadAllText(selected, Encoding.UTF8)) as Dictionary<string, object>;
            object rootValue;
            if (values == null || !values.TryGetValue("WallpaperDir", out rootValue)) return result;
            string wallpaperRoot = Convert.ToString(rootValue);
            if (string.IsNullOrWhiteSpace(wallpaperRoot)) return result;
            result.SettingsPath = selected;
            result.DataRoot = Path.GetDirectoryName(selected);
            result.WallpaperRoot = wallpaperRoot;
            result.Distribution = selected.IndexOf(Path.DirectorySeparatorChar + "Packages" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0 ? "Microsoft Store" : "Desktop Installer";
        }
        catch { return new LivelyDetection(); }

        result.LivelyExePath = result.Distribution == "Desktop Installer" ? FindLivelyExecutable() : null;
        return result;
    }

    private static string FindLivelyExecutable()
    {
        try
        {
            foreach (Process process in Process.GetProcessesByName("Lively"))
            {
                try { if (!string.IsNullOrEmpty(process.MainModule.FileName) && File.Exists(process.MainModule.FileName)) return process.MainModule.FileName; }
                catch { }
            }
        }
        catch { }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string[] fixedCandidates = {
            Path.Combine(local, "Programs", "Lively Wallpaper", "Lively.exe"),
            Path.Combine(programFiles, "Lively Wallpaper", "Lively.exe"),
            Path.Combine(programFilesX86, "Lively Wallpaper", "Lively.exe")
        };
        foreach (string candidate in fixedCandidates) if (File.Exists(candidate)) return candidate;

        string registryResult = FindFromUninstallRegistry(Registry.CurrentUser);
        if (!string.IsNullOrEmpty(registryResult)) return registryResult;
        registryResult = FindFromUninstallRegistry(Registry.LocalMachine);
        return registryResult;
    }

    private static string FindFromUninstallRegistry(RegistryKey root)
    {
        string[] keys = {
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };
        foreach (string keyPath in keys)
        {
            try
            {
                using (RegistryKey uninstall = root.OpenSubKey(keyPath))
                {
                    if (uninstall == null) continue;
                    foreach (string childName in uninstall.GetSubKeyNames())
                    {
                        using (RegistryKey child = uninstall.OpenSubKey(childName))
                        {
                            string displayName = Convert.ToString(child.GetValue("DisplayName"));
                            if (displayName.IndexOf("Lively Wallpaper", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            string location = Convert.ToString(child.GetValue("InstallLocation"));
                            string candidate = Path.Combine(location, "Lively.exe");
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                }
            }
            catch { }
        }
        return null;
    }
}

internal sealed class PayloadFile
{
    public string ResourceName;
    public string RelativePath;
    public PayloadFile(string resource, string path) { ResourceName = resource; RelativePath = path; }
}

internal static class PortableInstaller
{
    public static readonly string InstallRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Wallpaper Control");

    private static readonly PayloadFile[] Payload = {
        new PayloadFile("Payload.WallpaperControl.exe", "WallpaperControl.exe"),
        new PayloadFile("Payload.Automation.LivelyShuffle.ps1", @"Automation\LivelyShuffle.ps1"),
        new PayloadFile("Payload.Automation.ApplyWallpaperControl.ps1", @"Automation\ApplyWallpaperControl.ps1"),
        new PayloadFile("Payload.Automation.LivelyShuffleLauncher.exe", @"Automation\LivelyShuffleLauncher.exe"),
        new PayloadFile("Payload.Assets.WallpaperControl.png", @"Automation\Assets\WallpaperControl.png"),
        new PayloadFile("Payload.Assets.WallpaperControl.ico", @"Automation\Assets\WallpaperControl.ico")
    };

    public static void Install(LivelyDetection detection, bool createStartup, string targetRoot, bool configureLively)
    {
        if (detection == null || !detection.Found) throw new InvalidOperationException("Không tìm thấy dữ liệu Lively Wallpaper.");
        StopInstalledControl(targetRoot);
        Directory.CreateDirectory(targetRoot);
        ExtractPayload(targetRoot);

        string automation = Path.Combine(targetRoot, "Automation");
        Directory.CreateDirectory(automation);
        string settings = Path.Combine(automation, "WallpaperControl.ini");
        if (!File.Exists(settings))
        {
            File.WriteAllText(settings,
                "[Wallpaper]\r\nAutoEnabled=0\r\n\r\n[Color]\r\nMode=Off\r\nIntensity=50\r\nSaturation=100\r\n\r\n[Safety]\r\nDriverPolicy=Unchanged\r\n",
                new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(automation, "wallpaper-auto.disabled"), "Disabled after first install\r\n", new UTF8Encoding(false));
        }

        var environment = new StringBuilder();
        environment.AppendLine("SettingsPath=" + detection.SettingsPath);
        environment.AppendLine("DataRoot=" + detection.DataRoot);
        environment.AppendLine("WallpaperRoot=" + detection.WallpaperRoot);
        environment.AppendLine("LivelyExePath=" + (detection.LivelyExePath ?? ""));
        environment.AppendLine("AppUserModelId=" + detection.AppUserModelId);
        environment.AppendLine("Distribution=" + detection.Distribution);
        File.WriteAllText(Path.Combine(automation, "LivelyEnvironment.ini"), environment.ToString(), new UTF8Encoding(false));

        foreach (string folder in new[] {
            "NONE RTX DYANMIC VIBRANCE",
            "RTX DYNAMIC VIBRANCE 50-100",
            "RTX DYNAMIC VIBRANCE 70-100",
            "RTX DYNAMIC VIBRANCE 100-100"
        }) Directory.CreateDirectory(Path.Combine(detection.WallpaperRoot, folder));

        if (configureLively) RunOneTimeConfiguration(targetRoot);

        if (string.Equals(targetRoot, InstallRoot, StringComparison.OrdinalIgnoreCase))
        {
            string exe = Path.Combine(targetRoot, "WallpaperControl.exe");
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Wallpaper Control.lnk"), exe);
            CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Wallpaper Control.lnk"), exe);
            string startup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Wallpaper Control.lnk");
            if (createStartup) CreateShortcut(startup, exe); else TryDelete(startup);
        }
    }

    private static void RunOneTimeConfiguration(string targetRoot)
    {
        string script = Path.Combine(targetRoot, "Automation", "LivelyShuffle.ps1");
        var start = new ProcessStartInfo("powershell.exe",
            "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"" + script + "\" -ConfigureScalingOnly") {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using (Process process = Process.Start(start))
        {
            if (process == null || !process.WaitForExit(300000)) throw new TimeoutException("Lively mất quá nhiều thời gian để cấu hình thư viện.");
            if (process.ExitCode != 0) throw new InvalidOperationException("Không thể hoàn tất cấu hình thư viện Lively (mã " + process.ExitCode + ").");
        }
    }

    public static void Uninstall()
    {
        StopInstalledControl(InstallRoot);
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Wallpaper Control.lnk"));
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Wallpaper Control.lnk"));
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Wallpaper Control.lnk"));
        if (Directory.Exists(InstallRoot)) Directory.Delete(InstallRoot, true);
    }

    private static void ExtractPayload(string targetRoot)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        foreach (PayloadFile item in Payload)
        {
            string target = Path.Combine(targetRoot, item.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using (Stream input = assembly.GetManifestResourceStream(item.ResourceName))
            {
                if (input == null) throw new InvalidDataException("Thiếu payload: " + item.ResourceName);
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None)) input.CopyTo(output);
            }
        }
    }

    private static void StopInstalledControl(string root)
    {
        foreach (Process process in Process.GetProcessesByName("WallpaperControl"))
        {
            try
            {
                string path = process.MainModule.FileName;
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { process.Kill(); process.WaitForExit(2500); }
            }
            catch { }
        }
    }

    private static void CreateShortcut(string path, string exe)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType);
        object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
        Type linkType = shortcut.GetType();
        linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exe });
        linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(exe) });
        linkType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Điều khiển Lively Wallpaper tự động" });
        linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { exe + ",0" });
        linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}

internal sealed class SetupForm : Form
{
    private readonly Label status = new Label();
    private readonly Label details = new Label();
    private readonly Button install = new Button();
    private readonly Button uninstall = new Button();
    private readonly CheckBox startup = new CheckBox();
    private LivelyDetection detection;

    public SetupForm()
    {
        Text = "Wallpaper Control Setup";
        ClientSize = new Size(760, 455);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(8, 10, 15);
        ForeColor = Color.FromArgb(243, 244, 249);
        Font = new Font("Segoe UI", 10F);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var header = new Panel { Dock = DockStyle.Top, Height = 106, BackColor = Color.FromArgb(18, 21, 30) };
        header.Controls.Add(new Label { Text = "WALLPAPER CONTROL", Location = new Point(28, 21), Size = new Size(420, 34), ForeColor = Color.White, Font = new Font("Segoe UI", 19F, FontStyle.Bold) });
        header.Controls.Add(new Label { Text = "PORTABLE SETUP  ·  LIVELY AUTO DETECTION", Location = new Point(31, 60), Size = new Size(430, 24), ForeColor = Color.FromArgb(255, 42, 116), Font = new Font("Segoe UI Semibold", 9F) });
        Controls.Add(header);

        Controls.Add(new Label { Text = "TRẠNG THÁI PHÁT HIỆN", Location = new Point(34, 133), Size = new Size(230, 25), ForeColor = Color.FromArgb(153, 160, 180), Font = new Font("Segoe UI Semibold", 9F) });
        status.Location = new Point(34, 162);
        status.Size = new Size(690, 31);
        status.Font = new Font("Segoe UI Semibold", 12F);
        Controls.Add(status);
        details.Location = new Point(34, 202);
        details.Size = new Size(690, 86);
        details.ForeColor = Color.FromArgb(180, 187, 207);
        Controls.Add(details);

        startup.Text = "Khởi động Wallpaper Control cùng Windows";
        startup.Checked = true;
        startup.Location = new Point(34, 304);
        startup.Size = new Size(380, 28);
        startup.ForeColor = Color.FromArgb(230, 232, 239);
        Controls.Add(startup);

        install.Text = "CÀI ĐẶT / CẬP NHẬT";
        install.Location = new Point(34, 350);
        install.Size = new Size(260, 56);
        install.FlatStyle = FlatStyle.Flat;
        install.FlatAppearance.BorderSize = 0;
        install.BackColor = Color.FromArgb(230, 0, 70);
        install.ForeColor = Color.White;
        install.Font = new Font("Segoe UI Semibold", 10F);
        install.Click += InstallClicked;
        Controls.Add(install);

        uninstall.Text = "GỠ CONTROL";
        uninstall.Location = new Point(310, 350);
        uninstall.Size = new Size(170, 56);
        uninstall.FlatStyle = FlatStyle.Flat;
        uninstall.FlatAppearance.BorderColor = Color.FromArgb(80, 86, 104);
        uninstall.BackColor = Color.FromArgb(25, 29, 40);
        uninstall.ForeColor = Color.FromArgb(210, 214, 226);
        uninstall.Click += UninstallClicked;
        Controls.Add(uninstall);

        Controls.Add(new Label { Text = "Không sao chép video · không cần quyền Administrator", Location = new Point(500, 362), Size = new Size(225, 45), ForeColor = Color.FromArgb(112, 120, 140), TextAlign = ContentAlignment.MiddleRight });
        Shown += delegate { Detect(); };
    }

    private void Detect()
    {
        detection = LivelyDetector.Detect();
        if (detection.Found)
        {
            status.Text = "●  ĐÃ TÌM THẤY LIVELY WALLPAPER";
            status.ForeColor = Color.FromArgb(56, 220, 140);
            details.Text = "Phiên bản: " + detection.Distribution + "\r\nDữ liệu: " + detection.DataRoot + "\r\nThư viện: " + detection.WallpaperRoot;
            install.Enabled = true;
        }
        else
        {
            status.Text = "●  CHƯA TÌM THẤY LIVELY WALLPAPER";
            status.ForeColor = Color.FromArgb(255, 65, 105);
            details.Text = "Hãy cài và mở Lively Wallpaper ít nhất một lần, sau đó bấm lại file Setup này.";
            install.Enabled = false;
        }
        uninstall.Enabled = Directory.Exists(PortableInstaller.InstallRoot);
    }

    private void InstallClicked(object sender, EventArgs e)
    {
        try
        {
            install.Enabled = false;
            status.Text = "Đang cài đặt...";
            status.ForeColor = Color.FromArgb(0, 205, 255);
            Application.DoEvents();
            PortableInstaller.Install(detection, startup.Checked, PortableInstaller.InstallRoot, true);
            string exe = Path.Combine(PortableInstaller.InstallRoot, "WallpaperControl.exe");
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            status.Text = "●  CÀI ĐẶT HOÀN TẤT";
            status.ForeColor = Color.FromArgb(56, 220, 140);
            details.Text = "Wallpaper Control đã được thêm vào Desktop, Start Menu và tự nhận thư viện Lively.\r\nColor Boost mặc định tắt; video hiện có không bị sao chép.";
            uninstall.Enabled = true;
        }
        catch (Exception ex)
        {
            status.Text = "●  CÀI ĐẶT THẤT BẠI";
            status.ForeColor = Color.FromArgb(255, 65, 105);
            details.Text = ex.Message;
        }
        finally { install.Enabled = detection != null && detection.Found; }
    }

    private void UninstallClicked(object sender, EventArgs e)
    {
        if (MessageBox.Show("Gỡ Wallpaper Control? Video và thư viện Lively sẽ được giữ nguyên.", "Wallpaper Control Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            PortableInstaller.Uninstall();
            status.Text = "●  ĐÃ GỠ WALLPAPER CONTROL";
            status.ForeColor = Color.FromArgb(180, 187, 207);
            details.Text = "Không xóa video, thư viện hay cài đặt Lively Wallpaper.";
            uninstall.Enabled = false;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Không thể gỡ", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}

internal static class SetupProgram
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--self-test")
        {
            try
            {
                LivelyDetection detection = LivelyDetector.Detect();
                PortableInstaller.Install(detection, false, args[1], false);
                File.WriteAllText(Path.Combine(args[1], "setup-self-test.txt"),
                    "Found=" + detection.Found + "\r\nDistribution=" + detection.Distribution + "\r\nSettings=" + detection.SettingsPath + "\r\nWallpaperRoot=" + detection.WallpaperRoot,
                    new UTF8Encoding(false));
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(args[1]);
                File.WriteAllText(Path.Combine(args[1], "setup-self-test.txt"), "ERROR=" + ex, new UTF8Encoding(false));
                Environment.ExitCode = 1;
            }
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
    }
}
