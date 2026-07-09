using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Terminal.Gui;
using SharpHook;
using SharpHook.Data;
using SharpHook.Native;
using WindowsInput;
using WindowsInput.Native;

class Program
{
    // Win32 API quản lý cửa sổ hiển thị
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;
    private const string CONFIG_FILE = "config.txt";

    // Biến điều khiển lõi Tool
    private static InputSimulator input = new InputSimulator();
    private static Random rand = new Random();
    private static bool _isLooping = false;
    private static readonly object lockObj = new object();
    private static Thread actionThread = null;
    private static SimpleGlobalHook hook = null;

    // Toàn bộ cấu hình nạp từ file config.txt (Giá trị mặc định tối ưu nhất)
    private static int cfgVirtualKeyCode = 32;
    private static int cfgDelayClick = 12;
    private static int cfgFramesSwing = 6;
    private static int cfgDelayCombo = 25;
    private static bool cfgEnableFocusCheck = true;
    private static bool cfgEnableAutoFocus = true;

    // Các thành phần giao diện Dashboard hiển thị thông tin tĩnh/động
    private static Label lblStatusBadge = null;

    private static bool IsLooping
    {
        get { lock (lockObj) return _isLooping; }
        set
        {
            lock (lockObj)
            {
                if (_isLooping != value)
                {
                    _isLooping = value;
                    Application.MainLoop.Invoke(new Action(UpdateStatusUI));
                }
            }
        }
    }

    static void Main(string[] args)
    {
        Console.SetWindowSize(104, 12);
        // 1. Luôn tải cấu hình từ file trước
        LoadConfig();

        Application.Init();
        var top = Application.Top;

        var win = new Window("STARDEW VALLEY ANIMATION CANCELER PRO - github.com/FrySimpl3/Cancel_animation_SV")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        top.Add(win);

        // --- KHU VỰC 1: THÔNG TIN CẤU HÌNH ĐANG ĐƯỢC CHẠY ---
        var frameConfigInfo = new FrameView(" Thông Số Đang Áp Dụng (Đọc từ config.txt) ")
        {
            X = 1,
            Y = 1,
            Width = Dim.Percent(55),
            Height = 9
        };

        frameConfigInfo.Add(new Label("• Phím kích hoạt (VK Code) : " + cfgVirtualKeyCode + " (" + ((VirtualKeyCode)cfgVirtualKeyCode).ToString() + ")") { X = 1, Y = 1 });
        frameConfigInfo.Add(new Label("• Delay sau Click chuột    : " + cfgDelayClick + " ms") { X = 1, Y = 2 });
        frameConfigInfo.Add(new Label("• Chờ vung dụng cụ        : " + cfgFramesSwing + " frames") { X = 1, Y = 3 });
        frameConfigInfo.Add(new Label("• Delay đè giữ Combo       : " + cfgDelayCombo + " ms") { X = 1, Y = 4 });
        frameConfigInfo.Add(new Label("• Chống spam khi Alt+Tab   : " + (cfgEnableFocusCheck ? "BẬT [ON]" : "TẮT [OFF]")) { X = 1, Y = 5 });
        frameConfigInfo.Add(new Label("• Tự động lôi game lên     : " + (cfgEnableAutoFocus ? "BẬT [ON]" : "TẮT [OFF]")) { X = 1, Y = 6 });

        // --- KHU VỰC 2: GIÁM SÁT TRẠNG THÁI THỜI GIAN THỰC ---
        var frameStatus = new FrameView(" Trạng Thái Hoạt Động ")
        {
            X = Pos.Right(frameConfigInfo) + 1,
            Y = 1,
            Width = Dim.Fill() - 1,
            Height = 5
        };

        var lblStatusTitle = new Label("Trạng thái hiện tại: ") { X = 1, Y = 1 };
        lblStatusBadge = new Label("  STOPPED (OFF)  ") { X = Pos.Right(lblStatusTitle), Y = 1 };
        frameStatus.Add(lblStatusTitle, lblStatusBadge);

        // --- KHU VỰC 3: HƯỚNG DẪN VÀ THOÁT ---
        var frameHelp = new FrameView(" Hướng Dẫn Vận Hành ")
        {
            X = Pos.Right(frameConfigInfo) + 1,
            Y = Pos.Bottom(frameStatus),
            Width = Dim.Fill() - 1,
            Height = 4
        };
        frameHelp.Add(new Label("➜ Đè giữ phím cấu hình để spam chặt/đào.\n➜ Bấm phím ESC để đóng hoàn toàn tool.") { X = 1, Y = 1 });

        win.Add(frameConfigInfo, frameStatus, frameHelp);

        UpdateStatusUI();

        // Chạy luồng bắt phím nền SharpHook
        Thread hookThread = new Thread(new ThreadStart(StartGlobalHook));
        hookThread.IsBackground = true;
        hookThread.Start();

        Application.Run();

        if (hook != null) hook.Dispose();
    }

    // Đọc file config.txt
    private static void LoadConfig()
    {
        if (!File.Exists(CONFIG_FILE))
        {
            SaveConfig(); // Tự sinh file cấu hình tối ưu nếu chưa có
            return;
        }

        try
        {
            string[] lines = File.ReadAllLines(CONFIG_FILE);
            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line) || !line.Contains("=")) continue;
                string[] parts = line.Split('=');
                string key = parts[0].Trim();
                string val = parts[1].Trim();

                switch (key)
                {
                    case "VirtualKeyCode": cfgVirtualKeyCode = int.Parse(val); break;
                    case "DelayClick": cfgDelayClick = int.Parse(val); break;
                    case "FramesSwing": cfgFramesSwing = int.Parse(val); break;
                    case "DelayCombo": cfgDelayCombo = int.Parse(val); break;
                    case "EnableFocusCheck": cfgEnableFocusCheck = bool.Parse(val); break;
                    case "EnableAutoFocus": cfgEnableAutoFocus = bool.Parse(val); break;
                }
            }
        }
        catch { }
    }

    // Ghi cấu hình mặc định (Gia tri TỐI ƯU NHẤT)
    private static void SaveConfig()
    {
        try
        {
            using (StreamWriter sw = new StreamWriter(CONFIG_FILE, false))
            {
                sw.WriteLine("VirtualKeyCode=" + cfgVirtualKeyCode);
                sw.WriteLine("DelayClick=" + cfgDelayClick);
                sw.WriteLine("FramesSwing=" + cfgFramesSwing);
                sw.WriteLine("DelayCombo=" + cfgDelayCombo);
                sw.WriteLine("EnableFocusCheck=" + cfgEnableFocusCheck.ToString());
                sw.WriteLine("EnableAutoFocus=" + cfgEnableAutoFocus.ToString());
            }
        }
        catch { }
    }

    private static void StartGlobalHook()
    {
        hook = new SimpleGlobalHook();
        hook.KeyPressed += OnKeyPressed;
        hook.KeyReleased += OnKeyReleased;
        hook.Run();
    }

    private static void UpdateStatusUI()
    {
        if (lblStatusBadge == null) return;

        if (_isLooping)
        {
            lblStatusBadge.Text = "  RUNNING (ON)  ";
            ColorScheme activeScheme = new ColorScheme();
            activeScheme.Normal = Terminal.Gui.Attribute.Make(Color.White, Color.BrightGreen);
            lblStatusBadge.ColorScheme = activeScheme;
        }
        else
        {
            lblStatusBadge.Text = "  STOPPED (OFF) ";
            ColorScheme stopScheme = new ColorScheme();
            stopScheme.Normal = Terminal.Gui.Attribute.Make(Color.White, Color.BrightRed);
            lblStatusBadge.ColorScheme = stopScheme;
        }
    }

    private static void OnKeyPressed(object sender, KeyboardHookEventArgs e)
    {
        int pressedVkCode = (int)e.Data.RawCode;

        if (pressedVkCode == cfgVirtualKeyCode)
        {
            if (cfgEnableAutoFocus)
            {
                if (!EnsureStardewValleyActive()) return;
            }
            else
            {
                if (cfgEnableFocusCheck && !IsStardewValleyActive()) return;
            }

            if (!IsLooping)
            {
                IsLooping = true;
                actionThread = new Thread(new ThreadStart(DoAnimationCancel));
                actionThread.IsBackground = true;
                actionThread.Start();
            }
        }
    }

    private static void OnKeyReleased(object sender, KeyboardHookEventArgs e)
    {
        int releasedVkCode = (int)e.Data.RawCode;

        if (releasedVkCode == cfgVirtualKeyCode)
        {
            IsLooping = false;
        }
    }

    private static void DoAnimationCancel()
    {
        while (IsLooping)
        {
            if (cfgEnableFocusCheck && !IsStardewValleyActive())
            {
                IsLooping = false;
                break;
            }

            // --- 1. CLICK CHUỘT TRÁI ---
            input.Mouse.LeftButtonDown();
            SleepForFrames(1);
            input.Mouse.LeftButtonUp();

            Thread.Sleep(cfgDelayClick + rand.Next(0, 4));

            // --- 2. CHỜ VUNG DỤNG CỤ ---
            SleepForFrames(cfgFramesSwing);

            if (cfgEnableFocusCheck && !IsStardewValleyActive())
            {
                IsLooping = false;
                break;
            }

            // --- 3. ĐÈ GIỮ COMBO CANCEL ---
            input.Keyboard.KeyDown(VirtualKeyCode.VK_R);
            input.Keyboard.KeyDown(VirtualKeyCode.DELETE);
            input.Keyboard.KeyDown(VirtualKeyCode.RSHIFT);

            Thread.Sleep(cfgDelayCombo);

            // --- 4. THẢ COMBO CANCEL ---
            input.Keyboard.KeyUp(VirtualKeyCode.VK_R);
            input.Keyboard.KeyUp(VirtualKeyCode.DELETE);
            input.Keyboard.KeyUp(VirtualKeyCode.RSHIFT);

            SleepForFrames(1);
            Thread.Sleep(rand.Next(5, 15));
        }
    }

    private static void SleepForFrames(int frames)
    {
        int ms = (int)Math.Round(frames * 16.666);
        if (ms > 0) Thread.Sleep(ms);
    }

    private static bool IsStardewValleyActive()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        try
        {
            Process proc = Process.GetProcessById((int)pid);
            return proc.ProcessName.IndexOf("Stardew", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool EnsureStardewValleyActive()
    {
        if (IsStardewValleyActive()) return true;

        Process[] processes = Process.GetProcesses();
        foreach (var proc in processes)
        {
            if (proc.ProcessName.IndexOf("Stardew", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                IntPtr handle = proc.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, SW_RESTORE);
                    SetForegroundWindow(handle);
                    Thread.Sleep(150);
                    return true;
                }
            }
        }
        return false;
    }
}