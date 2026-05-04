namespace AsIHaveWritten;

using AsIHaveWritten.GameScripts;
using AsIHaveWritten.GameScripts.CurrencyWars;
using Common.Helpers;
using SharpHook.Providers;
using System.Diagnostics;
using System.Security.Principal;

internal class Program
{
    static void RunApplication()
    {
        var debuff = new string[]
        {
            "首领强化",
            "第三位面强化",
            "第二位面强化",
            "时间刺客",
        };
        var env = new string[]
        {
            "欢愉契约",
            "战技点契约",
            "蓝海",
            "英雄登场",
            "经济严重过热",
            "命运礼物",
            "彩虹时代",
            "银金彩",
            "昼之半神概念股",
            "进化算法",
        };
        
        using var win = new GameWindow("StarRail");
        // 手动刷了一下午没有3专家 😅
        CurrencyWars.RefreshOpening(win,
                                    debuff,
                                    env,
                                    4,
                                    Mode.Standard,
                                    (int)Rank.Max);
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
