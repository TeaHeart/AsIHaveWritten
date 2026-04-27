namespace AsIHaveWritten.GameScripts.CurrencyWars;

internal class CurrencyWars
{
    public static void RefreshOpening(GameWindow window,
                                      Predicate<string> buffCond,
                                      Predicate<string> invEnvCond,
                                      Mode mode = Mode.Overclock,
                                      int diff = (int)Rank.A7)
    {
        var context = new Dictionary<string, object>();

        var homePage = new HomePage(window);
        var modePage = new ModePage(window);
        var rankPage = new RankPage(window);
        var bossPage = new BossPage(window);
        var planePage = new PlanePage(window);
        var invEnvPage = new InvestmentEnvironment(window);
        var preparePage = new PreparePage(window);
        var leavePage = new LeavePage(window);
        var evalPage = new EvaluationPage(window);

        var router = new Dictionary<PageBase, Action>
        {
            { homePage, () => homePage.StartWar() },
            { modePage, () => modePage.NextStep(mode) },
            { rankPage, () => rankPage.NextStep(diff) },
            { bossPage, () => context["BuffFlag"] = buffCond(bossPage.NextStep()) },
            { planePage, () => planePage.NextStep() },
            { invEnvPage, () => context["InvEnvFlag"] = invEnvPage.TrySelect(invEnvCond) },
            { preparePage, () =>
            {
                if (context.GetValueOrDefault("BuffFlag", false) is bool v1 && v1
                 && context.GetValueOrDefault("InvEnvFlag", false) is bool v2 && v2)
                {
                    context["/STOP"] = true;
                }
                else
                {
                    preparePage.Leave();
                }
            } },
            { leavePage, () => leavePage.Leave(0) },
            { evalPage, () => evalPage.NextStep() },
        };

        while (true)
        {
            Thread.Sleep(1000);

            if (!window.Window.IsForeground)
            {
                Console.WriteLine("游戏窗口非焦点");
                continue;
            }

            var (Score, Name, Callback) = router.Select(x => (Score: x.Key.GetSimilarity(), x.Key.Name, x.Value)).OrderByDescending(x => x.Score).First();
            Console.WriteLine($"{Name}, 分数: {Score}");

            if (Score >= 0.5f)
            {
                try
                {
                    Callback();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            if (context.GetValueOrDefault("/STOP", false) is bool v && v)
            {
                Console.WriteLine("终止");
                break;
            }
        }
    }
}
