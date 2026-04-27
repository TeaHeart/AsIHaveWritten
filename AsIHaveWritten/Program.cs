namespace AsIHaveWritten;

using AsIHaveWritten.Extensions;
using AsIHaveWritten.GameScripts;
using AsIHaveWritten.Helpers;
using System.Diagnostics;
using System.Security.Principal;

internal class Program
{
    static void RunApplication()
    {
        using var win = new GameWindow("endfield");
        using var mcm = new MouseClickerManager(win);
        using var timer = new Timer(_ =>
        {
            mcm.Enabled = win.Window.IsForeground;
        });
        timer.Enable(100);
        Console.WriteLine("F8 记录/清除点， 左 Control 连点");
        Console.ReadLine();
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
        ConsoleHelper.SetEncoding();

        if (!WindowHelper.SetProcessDPIAware())
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
