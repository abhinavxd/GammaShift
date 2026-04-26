// GammaShift — Open Source Display Brightness Switcher for Gaming
// License: MIT
// https://github.com/abhinavxd/gammashift

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

[assembly: AssemblyTitle("GammaShift")]
[assembly: AssemblyDescription("Display brightness, contrast and vibrance switcher for gaming")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// ============================================================================
// P/Invoke Declarations
// ============================================================================

static class NativeMethods
{
    // --- GDI: Gamma Ramp ---
    [DllImport("gdi32.dll")] public static extern bool SetDeviceGammaRamp(IntPtr hdc, ref GammaRamp ramp);
    [DllImport("gdi32.dll")] public static extern bool GetDeviceGammaRamp(IntPtr hdc, ref GammaRamp ramp);
    [DllImport("gdi32.dll", CharSet = CharSet.Auto)] public static extern IntPtr CreateDC(string driver, string device, string output, IntPtr devMode);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);



    // --- User32: Display & Window ---
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE dd, uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    // --- User32: Keyboard Hook ---
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    // --- User32: Overlay ---
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_LAYERED = 0x80000;
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_TOPMOST = 0x8;
    public const int WS_EX_TOOLWINDOW = 0x80;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public const uint SWP_NOMOVE = 0x2;
    public const uint SWP_NOSIZE = 0x1;
    public const uint SWP_NOACTIVATE = 0x10;

    // --- Kernel32 ---
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] public static extern IntPtr GetModuleHandle(string lpModuleName);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] public static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32.dll")] public static extern IntPtr GetProcAddress(IntPtr hModule, string name);
    [DllImport("kernel32.dll")] public static extern bool FreeLibrary(IntPtr hModule);

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    // --- Structs ---
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    public const int DISPLAY_DEVICE_ACTIVE = 0x1;
}

[StructLayout(LayoutKind.Sequential)]
public struct GammaRamp
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Red;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Green;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Blue;
}

public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

// ============================================================================
// Main Application
// ============================================================================

class GammaShift : ApplicationContext
{
    // --- Profile Data ---
    class ProfileData
    {
        public double Gamma = 1.0;
        public double Contrast = 1.0;
        public int Vibrance = 50;
        public string Name = "Default";
        public double BrightnessMin = -1.0;
        public double BrightnessMax = -1.0;
    }

    // --- State ---
    NotifyIcon trayIcon;
    int currentProfile = 1;
    int profileCount = 9;
    ProfileData[] profiles = new ProfileData[9];
    GammaRamp originalGamma;
    bool hasOriginalGamma = false;

    // Keyboard hook
    IntPtr hookId = IntPtr.Zero;
    LowLevelKeyboardProc hookProc;

    // Display
    string selectedDisplay = "";
    List<string> displayNames = new List<string>();

    // Auto-brightness
    bool autoBrightness = false;
    Timer autoBrightnessTimer;
    double lastMeasuredBrightness = -1;
    int brightenDelayMs = 200;
    int dimDelayMs = 2000;
    int pendingProfile = 0;
    DateTime pendingSince = DateTime.MinValue;

    // Hotkeys
    bool hotkeysDisabled = false;

    // Debug overlay
    Form debugOverlay;
    bool debugOverlayVisible = false;
    Timer overlayRefreshTimer;

    // Toast
    Form toastForm;
    Timer toastTimer;

    // Settings
    bool autoStart = false;
    string language = "en";
    bool notifications = true;

    // GPU APIs
    bool nvApiAvail = false;
    bool adlAvail = false;
    IntPtr nvDisplayHandle = IntPtr.Zero;

    // NvAPI function pointers
    delegate int NvAPI_InitializeDelegate();
    delegate int NvAPI_EnumNvidiaDisplayHandleDelegate(int index, ref IntPtr handle);
    delegate int NvAPI_GetDVCInfoDelegate(IntPtr handle, int outputId, ref NVAPI_DVC_INFO info);
    delegate int NvAPI_SetDVCLevelDelegate(IntPtr handle, int outputId, int level);

    NvAPI_InitializeDelegate nvInit;
    NvAPI_EnumNvidiaDisplayHandleDelegate nvEnumDisplay;
    NvAPI_GetDVCInfoDelegate nvGetDVC;
    NvAPI_SetDVCLevelDelegate nvSetDVC;

    [StructLayout(LayoutKind.Sequential)]
    struct NVAPI_DVC_INFO
    {
        public int version;
        public int currentLevel;
        public int minLevel;
        public int maxLevel;
    }

    // ADL function pointers
    delegate int ADL_Main_Control_CreateDelegate(ADL_MemAllocDelegate callback, int enumConnected);
    delegate int ADL_Display_Color_GetDelegate(int adapterIndex, int displayIndex, int colorType, out int current, out int def, out int min, out int max, out int step);
    delegate int ADL_Display_Color_SetDelegate(int adapterIndex, int displayIndex, int colorType, int value);
    delegate int ADL_Main_Control_DestroyDelegate();
    delegate IntPtr ADL_MemAllocDelegate(int size);

    ADL_Main_Control_CreateDelegate adlCreate;
    ADL_Display_Color_GetDelegate adlColorGet;
    ADL_Display_Color_SetDelegate adlColorSet;
    ADL_Main_Control_DestroyDelegate adlDestroy;
    IntPtr adlModule = IntPtr.Zero;

    const int ADL_DISPLAY_COLOR_SATURATION = 2;

    // Config path
    string configPath;

    // Hidden form for UI thread marshaling
    Form marshalForm;

    // ========================================================================
    // Localization
    // ========================================================================

    string L(string en, string de)
    {
        return language == "de" ? de : en;
    }

    // ========================================================================
    // Entry Point
    // ========================================================================

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--setregistry")
        {
            SetGammaRegistryValue();
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new GammaShift());
    }

    // ========================================================================
    // Constructor
    // ========================================================================

    GammaShift()
    {
        configPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "GammaShift.cfg");

        // Hidden form for UI thread marshaling (keyboard hook fires on any thread)
        marshalForm = new Form();
        marshalForm.ShowInTaskbar = false;
        marshalForm.FormBorderStyle = FormBorderStyle.None;
        marshalForm.Size = new Size(0, 0);
        marshalForm.Opacity = 0;
        marshalForm.Visible = false;
        marshalForm.CreateControl();
        // Force handle creation so BeginInvoke works
        IntPtr _ = marshalForm.Handle;

        InitDefaultProfiles();
        EnumerateDisplays();
        InitGpuApis();
        SaveOriginalGamma();
        EnsureGammaRegistryKey();
        LoadConfig();
        if (!File.Exists(configPath)) SaveConfig();

        trayIcon = new NotifyIcon()
        {
            Icon = SystemIcons.Application,
            Text = "GammaShift - Profile 1 (" + profiles[0].Name + ")",
            Visible = true,
        };

        BuildMenu();

        // Keyboard hook
        hookProc = new LowLevelKeyboardProc(HookCallback);
        using (Process cur = Process.GetCurrentProcess())
        using (ProcessModule mod = cur.MainModule)
            hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, hookProc, NativeMethods.GetModuleHandle(mod.ModuleName), 0);

        // Brightness sampling timer (drives both auto-brightness and the debug overlay)
        autoBrightnessTimer = new Timer();
        autoBrightnessTimer.Interval = 500;
        autoBrightnessTimer.Tick += delegate { RunAutoBrightness(); };
        UpdateBrightnessSamplingState();

        // Toast timer
        toastTimer = new Timer();
        toastTimer.Interval = 1500;
        toastTimer.Tick += delegate { HideToast(); toastTimer.Stop(); };

        // Apply profile 1 on start
        ApplyProfile(1);
    }

    // ========================================================================
    // Display Enumeration
    // ========================================================================

    void EnumerateDisplays()
    {
        displayNames.Clear();
        NativeMethods.DISPLAY_DEVICE dd = new NativeMethods.DISPLAY_DEVICE();
        dd.cb = Marshal.SizeOf(dd);
        uint i = 0;
        while (NativeMethods.EnumDisplayDevices(null, i, ref dd, 0))
        {
            if ((dd.StateFlags & NativeMethods.DISPLAY_DEVICE_ACTIVE) != 0)
                displayNames.Add(dd.DeviceName);
            i++;
        }
        if (displayNames.Count > 0 && string.IsNullOrEmpty(selectedDisplay))
            selectedDisplay = displayNames[0];
    }

    // ========================================================================
    // GPU API Initialization
    // ========================================================================

    void InitGpuApis()
    {
        InitNvApi();
        InitAdl();
    }

    void InitNvApi()
    {
        try
        {
            string dll = Environment.Is64BitProcess ? "nvapi64.dll" : "nvapi.dll";
            IntPtr hModule = NativeMethods.LoadLibrary(dll);
            if (hModule == IntPtr.Zero) return;

            // NvAPI uses a query interface pattern — all functions accessed via nvapi_QueryInterface
            IntPtr queryPtr = NativeMethods.GetProcAddress(hModule, "nvapi_QueryInterface");
            if (queryPtr == IntPtr.Zero) return;

            var queryInterface = (NvAPI_QueryInterfaceDelegate)Marshal.GetDelegateForFunctionPointer(queryPtr, typeof(NvAPI_QueryInterfaceDelegate));

            IntPtr pInit = queryInterface(0x0150E828);
            IntPtr pEnum = queryInterface(0x9ABDD40D);
            IntPtr pGetDVC = queryInterface(0x4085DE45);
            IntPtr pSetDVC = queryInterface(0x172409B4);

            if (pInit == IntPtr.Zero || pEnum == IntPtr.Zero || pGetDVC == IntPtr.Zero || pSetDVC == IntPtr.Zero) return;

            nvInit = (NvAPI_InitializeDelegate)Marshal.GetDelegateForFunctionPointer(pInit, typeof(NvAPI_InitializeDelegate));
            nvEnumDisplay = (NvAPI_EnumNvidiaDisplayHandleDelegate)Marshal.GetDelegateForFunctionPointer(pEnum, typeof(NvAPI_EnumNvidiaDisplayHandleDelegate));
            nvGetDVC = (NvAPI_GetDVCInfoDelegate)Marshal.GetDelegateForFunctionPointer(pGetDVC, typeof(NvAPI_GetDVCInfoDelegate));
            nvSetDVC = (NvAPI_SetDVCLevelDelegate)Marshal.GetDelegateForFunctionPointer(pSetDVC, typeof(NvAPI_SetDVCLevelDelegate));

            if (nvInit() != 0) return;

            IntPtr handle = IntPtr.Zero;
            if (nvEnumDisplay(0, ref handle) == 0)
            {
                nvDisplayHandle = handle;
                nvApiAvail = true;
            }
        }
        catch { }
    }

    delegate IntPtr NvAPI_QueryInterfaceDelegate(uint id);

    void InitAdl()
    {
        try
        {
            string dll = Environment.Is64BitProcess ? "atiadlxx.dll" : "atiadlxy.dll";
            adlModule = NativeMethods.LoadLibrary(dll);
            if (adlModule == IntPtr.Zero) return;

            IntPtr pCreate = NativeMethods.GetProcAddress(adlModule, "ADL_Main_Control_Create");
            IntPtr pColorGet = NativeMethods.GetProcAddress(adlModule, "ADL_Display_Color_Get");
            IntPtr pColorSet = NativeMethods.GetProcAddress(adlModule, "ADL_Display_Color_Set");
            IntPtr pDestroy = NativeMethods.GetProcAddress(adlModule, "ADL_Main_Control_Destroy");

            if (pCreate == IntPtr.Zero || pColorGet == IntPtr.Zero || pColorSet == IntPtr.Zero) return;

            adlCreate = (ADL_Main_Control_CreateDelegate)Marshal.GetDelegateForFunctionPointer(pCreate, typeof(ADL_Main_Control_CreateDelegate));
            adlColorGet = (ADL_Display_Color_GetDelegate)Marshal.GetDelegateForFunctionPointer(pColorGet, typeof(ADL_Display_Color_GetDelegate));
            adlColorSet = (ADL_Display_Color_SetDelegate)Marshal.GetDelegateForFunctionPointer(pColorSet, typeof(ADL_Display_Color_SetDelegate));
            if (pDestroy != IntPtr.Zero)
                adlDestroy = (ADL_Main_Control_DestroyDelegate)Marshal.GetDelegateForFunctionPointer(pDestroy, typeof(ADL_Main_Control_DestroyDelegate));

            int result = adlCreate(ADL_MemAlloc, 1);
            adlAvail = (result == 0);
        }
        catch { }
    }

    static IntPtr ADL_MemAlloc(int size)
    {
        return Marshal.AllocHGlobal(size);
    }

    // ========================================================================
    // Gamma Ramp
    // ========================================================================

    IntPtr AcquireDisplayDC()
    {
        string dev = string.IsNullOrEmpty(selectedDisplay) ? null : selectedDisplay;
        return NativeMethods.CreateDC(null, dev, null, IntPtr.Zero);
    }

    void SaveOriginalGamma()
    {
        IntPtr hdc = AcquireDisplayDC();
        if (hdc != IntPtr.Zero)
        {
            originalGamma = new GammaRamp();
            originalGamma.Red = new ushort[256];
            originalGamma.Green = new ushort[256];
            originalGamma.Blue = new ushort[256];
            hasOriginalGamma = NativeMethods.GetDeviceGammaRamp(hdc, ref originalGamma);
            NativeMethods.DeleteDC(hdc);
        }
    }

    void RestoreOriginalGamma()
    {
        if (!hasOriginalGamma) return;
        IntPtr hdc = AcquireDisplayDC();
        if (hdc != IntPtr.Zero)
        {
            NativeMethods.SetDeviceGammaRamp(hdc, ref originalGamma);
            NativeMethods.DeleteDC(hdc);
        }
    }

    void ApplyGamma(double gamma, double contrast)
    {
        GammaRamp ramp = new GammaRamp();
        ramp.Red = new ushort[256];
        ramp.Green = new ushort[256];
        ramp.Blue = new ushort[256];

        for (int i = 0; i < 256; i++)
        {
            double val = Math.Pow(i / 255.0, 1.0 / gamma) * contrast;
            if (val < 0) val = 0;
            if (val > 1) val = 1;
            ushort v = (ushort)(val * 65535);
            ramp.Red[i] = v;
            ramp.Green[i] = v;
            ramp.Blue[i] = v;
        }

        IntPtr hdc = AcquireDisplayDC();
        if (hdc != IntPtr.Zero)
        {
            NativeMethods.SetDeviceGammaRamp(hdc, ref ramp);
            NativeMethods.DeleteDC(hdc);
        }
    }

    // ========================================================================
    // Digital Vibrance / Saturation
    // ========================================================================

    void SetVibrance(int level)
    {
        if (nvApiAvail)
        {
            try { nvSetDVC(nvDisplayHandle, 0, level); } catch { }
        }
        else if (adlAvail)
        {
            try { adlColorSet(0, 0, ADL_DISPLAY_COLOR_SATURATION, level); } catch { }
        }
    }

    int GetVibrance()
    {
        if (nvApiAvail)
        {
            try
            {
                NVAPI_DVC_INFO info = new NVAPI_DVC_INFO();
                info.version = Marshal.SizeOf(typeof(NVAPI_DVC_INFO)) | (1 << 16);
                if (nvGetDVC(nvDisplayHandle, 0, ref info) == 0)
                    return info.currentLevel;
            }
            catch { }
        }
        else if (adlAvail)
        {
            try
            {
                int cur, def, min, max, step;
                if (adlColorGet(0, 0, ADL_DISPLAY_COLOR_SATURATION, out cur, out def, out min, out max, out step) == 0)
                    return cur;
            }
            catch { }
        }
        return 50;
    }

    // ========================================================================
    // Registry: Gamma Range
    // ========================================================================

    static void SetGammaRegistryValue()
    {
        try
        {
            Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM", true);
            if (key == null)
                key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM");
            key.SetValue("GdiIcmGammaRange", 256, Microsoft.Win32.RegistryValueKind.DWord);
            key.Close();
            Environment.ExitCode = 0;
        }
        catch
        {
            Environment.ExitCode = 1;
        }
    }

    void EnsureGammaRegistryKey()
    {
        try
        {
            Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM", false);
            if (key != null)
            {
                object val = key.GetValue("GdiIcmGammaRange");
                key.Close();
                if (val != null && Convert.ToInt32(val) >= 256) return;
            }
        }
        catch { }

        // Need elevation to set the key
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Application.ExecutablePath;
            psi.Arguments = "--setregistry";
            psi.Verb = "runas";
            psi.UseShellExecute = true;
            Process p = Process.Start(psi);
            if (p != null) p.WaitForExit();
        }
        catch { }
    }

    // ========================================================================
    // Default Profiles
    // ========================================================================

    void InitDefaultProfiles()
    {
        for (int i = 0; i < 9; i++)
            profiles[i] = new ProfileData();

        profiles[0].Gamma = 1.0; profiles[0].Contrast = 1.0; profiles[0].Vibrance = 50;
        profiles[0].Name = "Normal"; profiles[0].BrightnessMin = 10.0; profiles[0].BrightnessMax = 255.0;

        profiles[1].Gamma = 1.5; profiles[1].Contrast = 1.1; profiles[1].Vibrance = 60;
        profiles[1].Name = "Bright"; profiles[1].BrightnessMin = 4.0; profiles[1].BrightnessMax = 9.9;

        profiles[2].Gamma = 2.0; profiles[2].Contrast = 1.1; profiles[2].Vibrance = 70;
        profiles[2].Name = "Brighter"; profiles[2].BrightnessMin = 0.0; profiles[2].BrightnessMax = 3.9;

        for (int i = 3; i < 9; i++)
        {
            profiles[i].Name = "Profile " + (i + 1);
            profiles[i].BrightnessMin = -1.0;
            profiles[i].BrightnessMax = -1.0;
        }
    }

    // ========================================================================
    // Config Persistence
    // ========================================================================

    void SaveConfig()
    {
        try
        {
            List<string> lines = new List<string>();
            lines.Add("Language=" + language);
            lines.Add("SelectedDisplay=" + selectedDisplay);
            lines.Add("AutoBrightness=" + (autoBrightness ? "1" : "0"));
            lines.Add("AutoStart=" + (autoStart ? "1" : "0"));
            lines.Add("Notifications=" + (notifications ? "1" : "0"));
            lines.Add("ProfileCount=" + profileCount);
            lines.Add("BrightenDelayMs=" + brightenDelayMs);
            lines.Add("DimDelayMs=" + dimDelayMs);
            lines.Add("HotkeysDisabled=" + (hotkeysDisabled ? "1" : "0"));

            for (int i = 0; i < 9; i++)
            {
                string p = "Profile" + (i + 1);
                lines.Add(p + "_Gamma=" + profiles[i].Gamma.ToString("F2"));
                lines.Add(p + "_Contrast=" + profiles[i].Contrast.ToString("F2"));
                lines.Add(p + "_Vibrance=" + profiles[i].Vibrance);
                lines.Add(p + "_Name=" + profiles[i].Name);
                lines.Add(p + "_BrMin=" + profiles[i].BrightnessMin.ToString("F1"));
                lines.Add(p + "_BrMax=" + profiles[i].BrightnessMax.ToString("F1"));
            }

            File.WriteAllLines(configPath, lines.ToArray());
        }
        catch { }
    }

    void LoadConfig()
    {
        if (!File.Exists(configPath)) return;
        try
        {
            string[] lines = File.ReadAllLines(configPath);
            foreach (string line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();

                if (k == "Language") language = v;
                else if (k == "SelectedDisplay") selectedDisplay = v;
                else if (k == "AutoBrightness") autoBrightness = v == "1";
                else if (k == "AutoStart") autoStart = v == "1";
                else if (k == "Notifications") notifications = v == "1";
                else if (k == "ProfileCount") { int pc; if (int.TryParse(v, out pc) && pc >= 3 && pc <= 9) profileCount = pc; }
                else if (k == "BrightenDelayMs") { int d; if (int.TryParse(v, out d) && d >= 0 && d <= 30000) brightenDelayMs = d; }
                else if (k == "DimDelayMs") { int d; if (int.TryParse(v, out d) && d >= 0 && d <= 30000) dimDelayMs = d; }
                else if (k == "HotkeysDisabled") hotkeysDisabled = v == "1";
                else if (k.StartsWith("Profile") && k.Contains("_"))
                {
                    // e.g. Profile1_Gamma=1.5
                    int underIdx = k.IndexOf('_');
                    int profNum;
                    if (int.TryParse(k.Substring(7, underIdx - 7), out profNum) && profNum >= 1 && profNum <= 9)
                    {
                        string field = k.Substring(underIdx + 1);
                        ProfileData pd = profiles[profNum - 1];
                        double dv; int iv;
                        if (field == "Gamma" && double.TryParse(v, out dv)) pd.Gamma = dv;
                        else if (field == "Contrast" && double.TryParse(v, out dv)) pd.Contrast = dv;
                        else if (field == "Vibrance" && int.TryParse(v, out iv)) pd.Vibrance = iv;
                        else if (field == "Name") pd.Name = v;
                        else if (field == "BrMin" && double.TryParse(v, out dv)) pd.BrightnessMin = dv;
                        else if (field == "BrMax" && double.TryParse(v, out dv)) pd.BrightnessMax = dv;
                    }
                }
            }
        }
        catch { }
    }

    // ========================================================================
    // Apply Profile
    // ========================================================================

    void ApplyProfile(int profile)
    {
        if (profile < 1 || profile > profileCount) return;
        ProfileData p = profiles[profile - 1];
        ApplyGamma(p.Gamma, p.Contrast);
        SetVibrance(p.Vibrance);
        currentProfile = profile;

        trayIcon.Text = "GammaShift - " + L("Profile ", "Profil ") + profile + " (" + p.Name + ")";

        if (notifications)
            ShowToast(L("Profile ", "Profil ") + profile + " (" + p.Name + ")");
    }

    // ========================================================================
    // Keyboard Hook
    // ========================================================================

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_KEYDOWN && !hotkeysDisabled)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            int profile = 0;

            // Numpad 1-9 (both NumLock on and off)
            if (vkCode >= 0x61 && vkCode <= 0x69) profile = vkCode - 0x60;       // VK_NUMPAD1-9
            else if (vkCode >= 0x31 && vkCode <= 0x39)                             // VK_1-9 (top row as fallback)
            {
                // Only use top row if Shift is held (to avoid conflicts with typing)
                if ((Control.ModifierKeys & Keys.Shift) != 0)
                    profile = vkCode - 0x30;
            }

            if (profile >= 1 && profile <= profileCount)
            {
                int p = profile; // capture for closure
                marshalForm.BeginInvoke(new Action(delegate { ApplyProfile(p); }));
            }
        }

        return NativeMethods.CallNextHookEx(hookId, nCode, wParam, lParam);
    }

    // ========================================================================
    // Auto-Brightness
    // ========================================================================

    const double HysteresisMargin = 1.5;

    void UpdateBrightnessSamplingState()
    {
        if (autoBrightness || debugOverlayVisible) autoBrightnessTimer.Start();
        else autoBrightnessTimer.Stop();
    }

    void RunAutoBrightness()
    {
        if (!autoBrightness && !debugOverlayVisible) return;

        double avg = MeasureScreenBrightness();
        lastMeasuredBrightness = avg;

        if (!autoBrightness) return;

        // Hysteresis: if the current profile still covers avg (with a small
        // margin), keep it. Stops profile flicker when avg oscillates around a
        // boundary like 9.9.
        if (currentProfile >= 1 && currentProfile <= profileCount)
        {
            ProfileData cur = profiles[currentProfile - 1];
            if (cur.BrightnessMin >= 0 && cur.BrightnessMax >= 0
                && avg >= cur.BrightnessMin - HysteresisMargin
                && avg <= cur.BrightnessMax + HysteresisMargin)
                return;
        }

        List<int> rangeProfiles = new List<int>();
        for (int i = 0; i < profileCount; i++)
        {
            if (profiles[i].BrightnessMin >= 0 && profiles[i].BrightnessMax >= 0)
                rangeProfiles.Add(i);
        }

        if (rangeProfiles.Count == 0) return;

        // Sort by BrightnessMin descending (brightest range first)
        rangeProfiles.Sort(delegate(int a, int b) {
            return profiles[b].BrightnessMin.CompareTo(profiles[a].BrightnessMin);
        });

        // Find which profile range the current brightness falls into
        int bestProfile = -1;

        if (rangeProfiles.Count >= 2)
        {
            for (int i = 0; i < rangeProfiles.Count; i++)
            {
                ProfileData p = profiles[rangeProfiles[i]];
                if (avg >= p.BrightnessMin && avg <= p.BrightnessMax)
                {
                    bestProfile = rangeProfiles[i] + 1;
                    break;
                }
            }

            // If above highest range, use brightest profile
            if (bestProfile < 0 && avg > profiles[rangeProfiles[0]].BrightnessMax)
                bestProfile = rangeProfiles[0] + 1;

            // If below lowest range, use darkest profile
            if (bestProfile < 0 && avg < profiles[rangeProfiles[rangeProfiles.Count - 1]].BrightnessMin)
                bestProfile = rangeProfiles[rangeProfiles.Count - 1] + 1;
        }
        else if (rangeProfiles.Count == 1)
        {
            bestProfile = rangeProfiles[0] + 1;
        }

        if (bestProfile <= 0 || bestProfile == currentProfile)
        {
            pendingProfile = 0;
            return;
        }

        // Asymmetric debounce. Direction is determined by comparing the
        // candidate profile's trigger range to the current profile's: a
        // lower BrightnessMin means the scene got darker (brighten), a
        // higher one means it got brighter (dim).
        int delay = brightenDelayMs;
        if (currentProfile >= 1 && currentProfile <= profileCount
            && profiles[currentProfile - 1].BrightnessMin >= 0
            && profiles[bestProfile - 1].BrightnessMin >= 0)
        {
            delay = profiles[bestProfile - 1].BrightnessMin > profiles[currentProfile - 1].BrightnessMin
                ? dimDelayMs
                : brightenDelayMs;
        }

        if (bestProfile != pendingProfile)
        {
            pendingProfile = bestProfile;
            pendingSince = DateTime.UtcNow;
        }

        if ((DateTime.UtcNow - pendingSince).TotalMilliseconds >= delay)
        {
            ApplyProfile(bestProfile);
            pendingProfile = 0;
        }
    }

    static int[][] GetMeasurementZones(int screenW, int screenH)
    {
        // Corner zones at 1/3 and 2/3 (not 1/4 and 3/4) so they sit inside the
        // typical HUD margin and sample the actual scene instead of UI elements.
        return new int[][] {
            new int[] { screenW / 2, screenH / 2 },
            new int[] { screenW / 3, screenH / 3 },
            new int[] { 2 * screenW / 3, screenH / 3 },
            new int[] { screenW / 3, 2 * screenH / 3 },
            new int[] { 2 * screenW / 3, 2 * screenH / 3 },
        };
    }

    static readonly double[] zoneWeights = { 2.0, 1.0, 1.0, 1.0, 1.0 };

    Rectangle GetSelectedDisplayBounds()
    {
        if (!string.IsNullOrEmpty(selectedDisplay))
        {
            foreach (Screen s in Screen.AllScreens)
                if (s.DeviceName == selectedDisplay) return s.Bounds;
        }
        return Screen.PrimaryScreen.Bounds;
    }

    double MeasureScreenBrightness()
    {
        Rectangle bounds = GetSelectedDisplayBounds();
        int screenW = bounds.Width;
        int screenH = bounds.Height;
        int[][] zones = GetMeasurementZones(screenW, screenH);

        int sampleSize = 16;
        double totalBrightness = 0;
        double totalWeight = 0;

        using (Bitmap bmp = new Bitmap(sampleSize, sampleSize))
        using (Graphics gBmp = Graphics.FromImage(bmp))
        {
            for (int z = 0; z < zones.Length; z++)
            {
                int cx = bounds.X + zones[z][0] - sampleSize / 2;
                int cy = bounds.Y + zones[z][1] - sampleSize / 2;

                gBmp.CopyFromScreen(cx, cy, 0, 0, new Size(sampleSize, sampleSize));

                double zoneBrightness = 0;
                int sampleCount = 0;
                for (int y = 0; y < sampleSize; y += 4)
                {
                    for (int x = 0; x < sampleSize; x += 4)
                    {
                        Color pixel = bmp.GetPixel(x, y);
                        zoneBrightness += 0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B;
                        sampleCount++;
                    }
                }

                if (sampleCount > 0) zoneBrightness /= sampleCount;
                totalBrightness += zoneBrightness * zoneWeights[z];
                totalWeight += zoneWeights[z];
            }
        }

        return totalWeight > 0 ? totalBrightness / totalWeight : 128;
    }

    // ========================================================================
    // Overlay Helpers
    // ========================================================================

    void MakeClickThroughOverlay(Form form)
    {
        int exStyle = NativeMethods.GetWindowLong(form.Handle, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(form.Handle, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT
                    | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        NativeMethods.SetWindowPos(form.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    // ========================================================================
    // Toast Notification
    // ========================================================================

    void ShowToast(string text)
    {
        HideToast();

        int screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int screenH = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

        int tw = 280, th = 48;
        toastForm = new Form()
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point((screenW - tw) / 2, screenH - 120),
            Size = new Size(tw, th),
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = Color.FromArgb(30, 30, 30),
            Opacity = 0.9,
        };

        Label lbl = new Label()
        {
            Text = text,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
        };
        toastForm.Controls.Add(lbl);

        IntPtr _ = toastForm.Handle;
        MakeClickThroughOverlay(toastForm);

        toastForm.Show();
        toastTimer.Start();
    }

    void HideToast()
    {
        if (toastForm != null)
        {
            toastForm.Close();
            toastForm.Dispose();
            toastForm = null;
        }
    }

    // ========================================================================
    // Debug Overlay
    // ========================================================================

    void ToggleDebugOverlay()
    {
        if (debugOverlayVisible)
        {
            if (overlayRefreshTimer != null) { overlayRefreshTimer.Stop(); overlayRefreshTimer.Dispose(); overlayRefreshTimer = null; }
            if (debugOverlay != null) { debugOverlay.Close(); debugOverlay.Dispose(); debugOverlay = null; }
            debugOverlayVisible = false;
            UpdateBrightnessSamplingState();
            return;
        }

        Rectangle bounds = GetSelectedDisplayBounds();
        int screenW = bounds.Width;
        int screenH = bounds.Height;

        debugOverlay = new Form()
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(bounds.X, bounds.Y),
            Size = new Size(screenW, screenH),
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = Color.Magenta,
            TransparencyKey = Color.Magenta,
        };

        debugOverlay.Paint += delegate(object s, PaintEventArgs e) {
            int[][] zones = GetMeasurementZones(screenW, screenH);
            int sz = 32;
            using (Pen pen = new Pen(Color.Lime, 2))
            {
                foreach (int[] z in zones)
                    e.Graphics.DrawRectangle(pen, z[0] - sz / 2, z[1] - sz / 2, sz, sz);
            }
            string info = lastMeasuredBrightness >= 0
                ? "Brightness: " + lastMeasuredBrightness.ToString("F1")
                : "Brightness: --";
            using (Font f = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (Brush b = new SolidBrush(Color.Lime))
                e.Graphics.DrawString(info, f, b, 10, 10);
        };

        IntPtr _ = debugOverlay.Handle;
        MakeClickThroughOverlay(debugOverlay);

        overlayRefreshTimer = new Timer();
        overlayRefreshTimer.Interval = 1000;
        overlayRefreshTimer.Tick += delegate { if (debugOverlay != null) debugOverlay.Invalidate(); };
        overlayRefreshTimer.Start();

        debugOverlay.Show();
        debugOverlayVisible = true;
        UpdateBrightnessSamplingState();
    }

    // ========================================================================
    // Profile Editor
    // ========================================================================

    void ShowProfileEditor()
    {
        Form editor = new Form();
        editor.Text = "GammaShift - " + L("Edit Profiles", "Profile bearbeiten");
        editor.Size = new Size(520, 560);
        editor.FormBorderStyle = FormBorderStyle.FixedDialog;
        editor.MaximizeBox = false;
        editor.StartPosition = FormStartPosition.CenterScreen;

        Panel scrollPanel = new Panel();
        scrollPanel.AutoScroll = true;
        scrollPanel.Dock = DockStyle.Fill;
        editor.Controls.Add(scrollPanel);

        // Profile count selector
        Label lblCount = new Label() { Text = L("Number of profiles:", "Anzahl Profile:"), Location = new Point(15, 15), AutoSize = true };
        NumericUpDown nudCount = new NumericUpDown() { Minimum = 3, Maximum = 9, Value = profileCount, Location = new Point(180, 12), Width = 50 };
        scrollPanel.Controls.Add(lblCount);
        scrollPanel.Controls.Add(nudCount);

        // Auto-brightness switch delays (asymmetric debounce)
        Label lblBrighten = new Label() { Text = L("Brighten delay (ms):", "Aufhellverzoegerung (ms):"), Location = new Point(15, 42), AutoSize = true };
        NumericUpDown nudBrighten = new NumericUpDown() { Minimum = 0, Maximum = 30000, Increment = 100, Value = brightenDelayMs, Location = new Point(180, 39), Width = 70 };
        Label lblDim = new Label() { Text = L("Dim delay (ms):", "Abdunkelverzoegerung (ms):"), Location = new Point(265, 42), AutoSize = true };
        NumericUpDown nudDim = new NumericUpDown() { Minimum = 0, Maximum = 30000, Increment = 100, Value = dimDelayMs, Location = new Point(395, 39), Width = 70 };
        scrollPanel.Controls.Add(lblBrighten); scrollPanel.Controls.Add(nudBrighten);
        scrollPanel.Controls.Add(lblDim); scrollPanel.Controls.Add(nudDim);

        int yOffset = 78;
        TextBox[] nameBoxes = new TextBox[9];
        NumericUpDown[] gammaBoxes = new NumericUpDown[9];
        NumericUpDown[] contrastBoxes = new NumericUpDown[9];
        NumericUpDown[] vibranceBoxes = new NumericUpDown[9];
        NumericUpDown[] brMinBoxes = new NumericUpDown[9];
        NumericUpDown[] brMaxBoxes = new NumericUpDown[9];

        for (int i = 0; i < 9; i++)
        {
            int y = yOffset + i * 52;
            ProfileData p = profiles[i];

            Label hdr = new Label() { Text = "P" + (i + 1) + ":", Location = new Point(15, y + 3), AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            scrollPanel.Controls.Add(hdr);

            nameBoxes[i] = new TextBox() { Text = p.Name, Location = new Point(45, y), Width = 70 };
            scrollPanel.Controls.Add(nameBoxes[i]);

            Label lg = new Label() { Text = "G:", Location = new Point(120, y + 3), AutoSize = true };
            gammaBoxes[i] = new NumericUpDown() { Minimum = 0.1m, Maximum = 5.0m, DecimalPlaces = 1, Increment = 0.1m, Value = (decimal)p.Gamma, Location = new Point(135, y), Width = 50 };
            scrollPanel.Controls.Add(lg); scrollPanel.Controls.Add(gammaBoxes[i]);

            Label lc = new Label() { Text = "C:", Location = new Point(190, y + 3), AutoSize = true };
            contrastBoxes[i] = new NumericUpDown() { Minimum = 0.1m, Maximum = 3.0m, DecimalPlaces = 1, Increment = 0.1m, Value = (decimal)p.Contrast, Location = new Point(207, y), Width = 50 };
            scrollPanel.Controls.Add(lc); scrollPanel.Controls.Add(contrastBoxes[i]);

            Label lv = new Label() { Text = "V:", Location = new Point(262, y + 3), AutoSize = true };
            vibranceBoxes[i] = new NumericUpDown() { Minimum = 0, Maximum = 100, Value = p.Vibrance, Location = new Point(278, y), Width = 50 };
            scrollPanel.Controls.Add(lv); scrollPanel.Controls.Add(vibranceBoxes[i]);

            Label lbr = new Label() { Text = "Br:", Location = new Point(333, y + 3), AutoSize = true };
            brMinBoxes[i] = new NumericUpDown() { Minimum = -1, Maximum = 255, DecimalPlaces = 1, Increment = 1m, Value = (decimal)p.BrightnessMin, Location = new Point(355, y), Width = 55 };
            brMaxBoxes[i] = new NumericUpDown() { Minimum = -1, Maximum = 255, DecimalPlaces = 1, Increment = 1m, Value = (decimal)p.BrightnessMax, Location = new Point(415, y), Width = 55 };
            scrollPanel.Controls.Add(lbr); scrollPanel.Controls.Add(brMinBoxes[i]); scrollPanel.Controls.Add(brMaxBoxes[i]);
        }

        // Help text
        int helpY = yOffset + 9 * 52 + 5;
        Label helpLbl = new Label()
        {
            Text = L("G=Gamma  C=Contrast  V=Vibrance  Br=Auto-Brightness range (Min-Max, -1=disabled)\nHover tray icon to see current brightness value.",
                      "G=Gamma  C=Kontrast  V=Vibrance  Br=Auto-Helligkeitsbereich (Min-Max, -1=deaktiviert)\nTray-Icon hovern fuer aktuellen Wert."),
            Location = new Point(15, helpY),
            AutoSize = true,
            Font = new Font("Segoe UI", 8f),
            ForeColor = Color.Gray,
        };
        scrollPanel.Controls.Add(helpLbl);

        // Buttons
        Button btnSave = new Button() { Text = L("Save", "Speichern"), Location = new Point(15, helpY + 40), Width = 80 };
        Button btnCancel = new Button() { Text = L("Cancel", "Abbrechen"), Location = new Point(100, helpY + 40), Width = 80 };
        Button btnPreview = new Button() { Text = L("Preview", "Vorschau"), Location = new Point(185, helpY + 40), Width = 80 };

        btnSave.Click += delegate {
            profileCount = (int)nudCount.Value;
            brightenDelayMs = (int)nudBrighten.Value;
            dimDelayMs = (int)nudDim.Value;
            for (int i = 0; i < 9; i++)
            {
                profiles[i].Name = nameBoxes[i].Text;
                profiles[i].Gamma = (double)gammaBoxes[i].Value;
                profiles[i].Contrast = (double)contrastBoxes[i].Value;
                profiles[i].Vibrance = (int)vibranceBoxes[i].Value;
                profiles[i].BrightnessMin = (double)brMinBoxes[i].Value;
                profiles[i].BrightnessMax = (double)brMaxBoxes[i].Value;
            }
            SaveConfig();
            BuildMenu();
            ApplyProfile(currentProfile);
            editor.Close();
        };
        btnCancel.Click += delegate { editor.Close(); };
        btnPreview.Click += delegate {
            int idx = currentProfile - 1;
            if (idx >= 0 && idx < 9)
            {
                ApplyGamma((double)gammaBoxes[idx].Value, (double)contrastBoxes[idx].Value);
                SetVibrance((int)vibranceBoxes[idx].Value);
            }
        };

        scrollPanel.Controls.Add(btnSave);
        scrollPanel.Controls.Add(btnCancel);
        scrollPanel.Controls.Add(btnPreview);

        editor.ShowDialog();
    }

    // ========================================================================
    // Calibration Wizard
    // ========================================================================

    void ShowCalibrationWizard()
    {
        MessageBox.Show(
            L("Calibration Wizard\n\n1. Enable Debug Overlay from the tray menu\n2. Launch your game and enter a bright area\n3. Note the brightness value shown in the top-left of the overlay\n4. Enter a dark area (e.g., a cave)\n5. Note that brightness value too\n6. Disable Debug Overlay\n7. Open Edit Profiles and set brightness ranges:\n   - Profile 1 (Normal): BrMin=10, BrMax=255\n   - Profile 2 (Bright): BrMin=4, BrMax=9.9\n   - Profile 3 (Brighter): BrMin=0, BrMax=3.9\n\nEnable Auto-Brightness and the profiles will switch automatically!",
              "Kalibrierungsassistent\n\n1. Aktiviere Debug-Overlay im Tray-Menue\n2. Starte dein Spiel und gehe in einen hellen Bereich\n3. Notiere den Helligkeitswert oben links im Overlay\n4. Gehe in einen dunklen Bereich (z.B. eine Hoehle)\n5. Notiere auch diesen Helligkeitswert\n6. Deaktiviere Debug-Overlay\n7. Oeffne Profile bearbeiten und setze Helligkeitsbereiche:\n   - Profil 1 (Normal): BrMin=10, BrMax=255\n   - Profil 2 (Hell): BrMin=4, BrMax=9.9\n   - Profil 3 (Heller): BrMin=0, BrMax=3.9\n\nAktiviere Auto-Helligkeit und die Profile wechseln automatisch!"),
            "GammaShift - " + L("Calibration", "Kalibrierung"),
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ========================================================================
    // Auto-Start
    // ========================================================================

    void SetAutoStart(bool enable)
    {
        autoStart = enable;
        try
        {
            Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (enable)
                key.SetValue("GammaShift", "\"" + Application.ExecutablePath + "\"");
            else
                key.DeleteValue("GammaShift", false);
            key.Close();
        }
        catch { }
        SaveConfig();
    }

    // ========================================================================
    // Context Menu
    // ========================================================================

    void BuildMenu()
    {
        ContextMenuStrip menu = new ContextMenuStrip();

        // Profiles
        for (int i = 0; i < profileCount; i++)
        {
            int idx = i;
            ProfileData p = profiles[i];
            string label = L("Profile ", "Profil ") + (i + 1) + ": " + p.Name + "  [Num " + (i + 1) + " / Shift+" + (i + 1) + "]";
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            if (i + 1 == currentProfile) item.Checked = true;
            item.Click += delegate { ApplyProfile(idx + 1); BuildMenu(); };
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        // Auto-Brightness
        ToolStripMenuItem autoBrItem = new ToolStripMenuItem(L("Auto-Brightness", "Auto-Helligkeit"));
        autoBrItem.Checked = autoBrightness;
        autoBrItem.Click += delegate {
            autoBrightness = !autoBrightness;
            UpdateBrightnessSamplingState();
            SaveConfig();
            BuildMenu();
        };
        menu.Items.Add(autoBrItem);

        // Disable Hotkeys
        ToolStripMenuItem disableHotkeysItem = new ToolStripMenuItem(L("Disable Hotkeys", "Hotkeys deaktivieren"));
        disableHotkeysItem.Checked = hotkeysDisabled;
        disableHotkeysItem.Click += delegate {
            hotkeysDisabled = !hotkeysDisabled;
            SaveConfig();
            BuildMenu();
        };
        menu.Items.Add(disableHotkeysItem);

        menu.Items.Add(new ToolStripSeparator());

        // Settings submenu
        ToolStripMenuItem settingsMenu = new ToolStripMenuItem(L("Settings", "Einstellungen"));

        // Edit Profiles
        ToolStripMenuItem editItem = new ToolStripMenuItem(L("Edit Profiles", "Profile bearbeiten"));
        editItem.Click += delegate { ShowProfileEditor(); };
        settingsMenu.DropDownItems.Add(editItem);

        // Calibrate
        ToolStripMenuItem calibItem = new ToolStripMenuItem(L("Calibration Guide", "Kalibrierungsanleitung"));
        calibItem.Click += delegate { ShowCalibrationWizard(); };
        settingsMenu.DropDownItems.Add(calibItem);

        // Debug Overlay
        ToolStripMenuItem overlayItem = new ToolStripMenuItem(L("Debug Overlay", "Debug-Overlay"));
        overlayItem.Checked = debugOverlayVisible;
        overlayItem.Click += delegate { ToggleDebugOverlay(); BuildMenu(); };
        settingsMenu.DropDownItems.Add(overlayItem);

        settingsMenu.DropDownItems.Add(new ToolStripSeparator());

        // Display selector
        if (displayNames.Count > 1)
        {
            ToolStripMenuItem dispMenu = new ToolStripMenuItem(L("Display", "Anzeige"));
            foreach (string disp in displayNames)
            {
                string d = disp;
                ToolStripMenuItem dItem = new ToolStripMenuItem(d);
                dItem.Checked = (d == selectedDisplay);
                dItem.Click += delegate { selectedDisplay = d; SaveConfig(); BuildMenu(); ApplyProfile(currentProfile); };
                dispMenu.DropDownItems.Add(dItem);
            }
            settingsMenu.DropDownItems.Add(dispMenu);
        }

        // Auto-Start
        ToolStripMenuItem autoStartItem = new ToolStripMenuItem(L("Auto-Start with Windows", "Auto-Start mit Windows"));
        autoStartItem.Checked = autoStart;
        autoStartItem.Click += delegate { SetAutoStart(!autoStart); BuildMenu(); };
        settingsMenu.DropDownItems.Add(autoStartItem);

        // Notifications
        ToolStripMenuItem notifItem = new ToolStripMenuItem(L("Notifications", "Benachrichtigungen"));
        notifItem.Checked = notifications;
        notifItem.Click += delegate { notifications = !notifications; SaveConfig(); BuildMenu(); };
        settingsMenu.DropDownItems.Add(notifItem);

        // Language
        ToolStripMenuItem langMenu = new ToolStripMenuItem(L("Language", "Sprache"));
        ToolStripMenuItem enItem = new ToolStripMenuItem("English");
        enItem.Checked = language == "en";
        enItem.Click += delegate { language = "en"; SaveConfig(); BuildMenu(); };
        ToolStripMenuItem deItem = new ToolStripMenuItem("Deutsch");
        deItem.Checked = language == "de";
        deItem.Click += delegate { language = "de"; SaveConfig(); BuildMenu(); };
        langMenu.DropDownItems.Add(enItem);
        langMenu.DropDownItems.Add(deItem);
        settingsMenu.DropDownItems.Add(langMenu);

        menu.Items.Add(settingsMenu);

        menu.Items.Add(new ToolStripSeparator());

        // About
        ToolStripMenuItem aboutItem = new ToolStripMenuItem(L("About GammaShift", "Ueber GammaShift"));
        aboutItem.Click += delegate {
            string gpuInfo = nvApiAvail ? "NVIDIA NvAPI" : adlAvail ? "AMD ADL" : "GDI only";
            MessageBox.Show(
                "GammaShift v1.0\n" +
                L("Open source display brightness switcher for gaming\n\n",
                  "Open-Source Display-Helligkeitsumschalter fuer Gaming\n\n") +
                "GPU: " + gpuInfo + "\n" +
                L("License: MIT\n", "Lizenz: MIT\n"),
                "GammaShift", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        menu.Items.Add(aboutItem);

        // Exit
        ToolStripMenuItem exitItem = new ToolStripMenuItem(L("Exit", "Beenden"));
        exitItem.Click += delegate { Shutdown(); };
        menu.Items.Add(exitItem);

        trayIcon.ContextMenuStrip = menu;
    }

    // ========================================================================
    // Shutdown
    // ========================================================================

    void Shutdown()
    {
        autoBrightnessTimer.Stop();
        autoBrightnessTimer.Dispose();
        RestoreOriginalGamma();

        if (hookId != IntPtr.Zero)
            NativeMethods.UnhookWindowsHookEx(hookId);

        if (overlayRefreshTimer != null) { overlayRefreshTimer.Stop(); overlayRefreshTimer.Dispose(); }
        if (debugOverlay != null) { debugOverlay.Close(); debugOverlay.Dispose(); }

        HideToast();
        toastTimer.Dispose();

        if (marshalForm != null) { marshalForm.Close(); marshalForm.Dispose(); }

        if (adlDestroy != null) { try { adlDestroy(); } catch { } }
        if (adlModule != IntPtr.Zero) NativeMethods.FreeLibrary(adlModule);

        trayIcon.Visible = false;
        trayIcon.Dispose();
        Application.Exit();
    }
}
