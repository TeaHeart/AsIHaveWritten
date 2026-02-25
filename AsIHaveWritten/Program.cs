namespace AsIHaveWritten;

using AsIHaveWritten.GameScripts;
using AsIHaveWritten.GameScripts.CurrencyWars;
using AsIHaveWritten.Helpers;
using SharpHook.Providers;
using System.Diagnostics;
using System.Security.Principal;

internal class Program
{
    static void RunApplication()
    {
        using var win = new GameWindow("StarRail");
        // 手动刷了一下午没有3专家 😅
        CurrencyWars.RefreshOpening(win, buff => true, inv => inv.Contains("专家")
                                                 || inv.Contains("银金彩")
                                                 || (inv.Contains("棱彩") && !inv.Contains("尾彩")),
                                                 Mode.Overclock,
                                                 (int)Rank.A7);
        UioHookProvider.Instance.Stop();
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
