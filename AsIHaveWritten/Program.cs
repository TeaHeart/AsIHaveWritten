namespace AsIHaveWritten;

using AsIHaveWritten.Extensions;
using AsIHaveWritten.GameScripts;
using AsIHaveWritten.GameScripts.Zzz;
using AsIHaveWritten.Helpers;
using PaddleOcr;
using SharpHook;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;

internal class Program
{
    static void RunApplication()
    {
        using var hook = new SimpleGlobalHook();
        var sim = new EventSimulator();
        using var ocr = new PaddleOcrEngine();
        using var win = new GameWindow("ZenlessZoneZero");
        using var wm = new WindowMonitor(win);
        using var mcm = new MouseClickerManager(hook, sim);
        using var afm = new ZzzAutoFishingManager(hook, sim, ocr, win);

        wm.WindowStatusChanged += (_, e) =>
        {
            Console.WriteLine(e);
            mcm.Enabled = afm.Enabled = e.IsForeground;
        };

        wm.Enabled = true;

        hook.RunAsync();

        while (true)
        {
            using var bitmap = win.Bitmap;
            bitmap?.Show("win", 0.5, 1);
        }
    }

    static void Main(string[] args)
    {
        if (IsAdmin())
        {
            ConfigApplication();
            RunApplication();
        }
        else
        {
            if (Debugger.IsAttached)
            {
                Console.WriteLine("正在调试，但未使用管理员运行");
            }
            else
            {
                Console.WriteLine("尝试以管理员身份运行");
                RunAsAdmin();
            }
        }
    }

    static void ConfigApplication()
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        if (!Win32Helper.SetProcessDPIAware())
        {
            Console.WriteLine("设置DPI感应失败");
        }
    }

    static bool IsAdmin()
    {
        var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    static void RunAsAdmin()
    {
        Process.Start(new ProcessStartInfo
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory,
            FileName = Environment.ProcessPath,
            Arguments = Environment.CommandLine,
            Verb = "runas"
        });
    }
}
