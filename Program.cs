using SharpHook;
using SharpHook.Data;
using SharpHook.Native;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using WindowsInput;
using WindowsInput.Native;

class Program
{
    // Thêm các API Win32 để quản lý cửa sổ
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    private static InputSimulator input = new InputSimulator();
    private static Random rand = new Random();

    private static bool _isLooping = false;
    private static readonly object lockObj = new object();

    // Thuộc tính quản lý trạng thái có thread-safe để cập nhật giao diện
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
                    RenderStatus(); // Cập nhật lại giao diện ngay khi đổi trạng thái
                }
            }
        }
    }

    private static Thread actionThread;
    private static KeyCode targetSharpHookKey = KeyCode.VcSpace;
    private static string selectedKeyName = "SPACE";

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "Stardew Valley Animation Canceler Pro";

        RenderHeader();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write(" ➜ Nhập phím kích hoạt (Ví dụ: Space, C, V, F...)\n ➜ Hoặc nhấn [ENTER] để dùng mặc định (Space): ");
        Console.ResetColor();

        string readLine = Console.ReadLine();
        string inputKey = (readLine != null) ? readLine.Trim().ToUpper() : "";

        if (string.IsNullOrEmpty(inputKey) || inputKey == "SPACE")
        {
            targetSharpHookKey = KeyCode.VcSpace;
            selectedKeyName = "SPACE";
        }
        else
        {
            string enumName = "Vc" + inputKey;
            try
            {
                targetSharpHookKey = (KeyCode)Enum.Parse(typeof(KeyCode), enumName, true);
                selectedKeyName = inputKey;
            }
            catch
            {
                targetSharpHookKey = KeyCode.VcSpace;
                selectedKeyName = "SPACE (Lỗi nhập, tự động quay về mặc định)";
            }
        }

        // Vẽ lại toàn bộ giao diện điều khiển sạch sẽ sau khi cấu hình xong
        Console.Clear();
        RenderHeader();
        RenderStatus();

        SimpleGlobalHook hook = new SimpleGlobalHook();
        hook.KeyPressed += OnKeyPressed;
        hook.KeyReleased += OnKeyReleased;

        hook.Run();
    }

    private static void RenderHeader()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("┌─────────────────────────────────────────────────────────────┐");
        Console.Write("│");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("      STARDEW VALLEY - ANIMATION CANCELER (ULTRA FOCUS)      ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("│");
        Console.WriteLine("└─────────────────────────────────────────────────────────────┘");
        Console.ResetColor();
    }

    // Giao diện hiển thị trạng thái động (Style Claude tối giản, trực quan)
    private static void RenderStatus()
    {
        // Lưu vị trí con trỏ hiện tại để tránh làm loạn console khi cập nhật liên tục
        int currentLeft = Console.CursorLeft;
        int currentTop = Console.CursorTop;

        // Cố định vị trí vẽ bảng trạng thái ở dòng thứ 4
        Console.SetCursorPosition(0, 4);

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(" ───────────── CẤU HÌNH & TRẠNG THÁI HỆ THỐNG ─────────────");
        Console.ResetColor();

        Console.Write(" ❖ Phím kích hoạt hiện tại : ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[{selectedKeyName}]");
        Console.ResetColor();

        Console.Write(" ❖ Trạng thái hoạt động    : ");
        if (IsLooping)
        {
            Console.BackgroundColor = ConsoleColor.DarkGreen;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("  RUNNING (ON)  ");
        }
        else
        {
            Console.BackgroundColor = ConsoleColor.DarkRed;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("  STOPPED (OFF) ");
        }
        Console.ResetColor();
        Console.WriteLine("\n");

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(" ─────────────────────────────────────────────────────────");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(" [Tính năng bảo vệ]: Tự động Focus game nếu game chạy ngầm.");
        Console.WriteLine(" [An toàn]: Thả phím kích hoạt hoặc Alt+Tab sẽ ngắt cụm tool lập tức.");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(" ─────────────────────────────────────────────────────────");
        Console.ResetColor();

        // Trả con trỏ về vị trí cũ (nếu có nhập liệu sau này)
        try { Console.SetCursorPosition(currentLeft, currentTop); } catch { }
    }

    private static void OnKeyPressed(object sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == targetSharpHookKey)
        {
            // Kiểm tra và tự động lôi cửa sổ Stardew lên trước nếu nó đang chạy ngầm
            if (!EnsureStardewValleyActive()) return;

            if (!IsLooping)
            {
                IsLooping = true;
                actionThread = new Thread(DoAnimationCancel);
                actionThread.IsBackground = true;
                actionThread.Start();
            }
        }
    }

    private static void OnKeyReleased(object sender, KeyboardHookEventArgs e)
    {
        if (e.Data.KeyCode == targetSharpHookKey)
        {
            IsLooping = false;
        }
    }

    private static void DoAnimationCancel()
    {
        while (IsLooping)
        {
            if (!IsStardewValleyActive())
            {
                IsLooping = false;
                break;
            }

            // --- 1. CLICK CHUỘT TRÁI ---
            input.Mouse.LeftButtonDown();
            SleepForFrames(1);
            input.Mouse.LeftButtonUp();

            Thread.Sleep(12 + rand.Next(0, 4));

            // --- 2. CHỜ VUNG DỤNG CỤ ---
            SleepForFrames(6);

            if (!IsStardewValleyActive())
            {
                IsLooping = false;
                break;
            }

            // --- 3. ĐÈ GIỮ COMBO CANCEL ---
            input.Keyboard.KeyDown(VirtualKeyCode.VK_R);
            input.Keyboard.KeyDown(VirtualKeyCode.DELETE);
            input.Keyboard.KeyDown(VirtualKeyCode.RSHIFT);

            Thread.Sleep(25);

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

    // Hàm mới: Nếu Stardew Valley đang chạy nhưng không Active -> Ép nó hiện lên trước
    private static bool EnsureStardewValleyActive()
    {
        if (IsStardewValleyActive()) return true;

        // Tìm tiến trình game
        Process[] processes = Process.GetProcesses();
        foreach (var proc in processes)
        {
            if (proc.ProcessName.IndexOf("Stardew", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                IntPtr handle = proc.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    // Khôi phục cửa sổ nếu bị thu nhỏ dưới Taskbar
                    ShowWindow(handle, SW_RESTORE);
                    // Đưa lên vị trí Foreground hàng đầu
                    SetForegroundWindow(handle);

                    Thread.Sleep(150); // Chờ một chút ngắn để Windows chuyển cảnh mượt mà
                    return true;
                }
            }
        }
        return false;
    }
}