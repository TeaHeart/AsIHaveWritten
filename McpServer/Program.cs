namespace McpServer;

using Common;
using Common.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaddleOcr;
using OmniParser;
using SharpHook;
using System.Diagnostics;
using System.Security.Principal;

internal class Program
{
    static async Task RunMcpServer()
    {
        var args = Environment.GetCommandLineArgs();
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
        builder.Services.AddSingleton<WindowMonitor>(_ => new WindowMonitor("StarRail"));
        builder.Services.AddSingleton<OmniParserEngine>(_ =>
        {
            var baseDir = AppContext.BaseDirectory;
            var florenceDir = Path.Combine(baseDir, "Resources", "icon_caption");
            return new OmniParserEngine(
                yoloModelPath: Path.Combine(baseDir, "Resources", "icon_detect.onnx"),
                detModelPath: Path.Combine(baseDir, "Resources", "ppocr", "PP-OCRv5_mobile_det_infer.onnx"),
                recModelPath: Path.Combine(baseDir, "Resources", "ppocr", "PP-OCRv5_mobile_rec_infer.onnx"),
                wordDictPath: Path.Combine(baseDir, "Resources", "ppocr", "characterDict.txt"),
                florenceModelDir: Directory.Exists(florenceDir) ? florenceDir : null);
        });
        builder.Services.AddSingleton<PaddleOcrEngine>();
        builder.Services.AddSingleton<IEventSimulator, EventSimulator>();
        builder.Services.AddMcpServer()
                        .WithHttpTransport()
                        .WithTools<WindowTools>();
        var app = builder.Build();
        app.MapMcp();
        await app.RunAsync();
    }

    static void Main(string[] args)
    {
        if (IsAdmin())
        {
            ConfigApplication();
            RunMcpServer().Wait();
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
