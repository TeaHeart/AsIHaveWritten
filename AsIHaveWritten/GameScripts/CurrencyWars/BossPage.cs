namespace AsIHaveWritten.GameScripts.CurrencyWars;

using Common.Helpers;
using System.Drawing;

internal class BossPage(GameWindow window) : PageBase("货币战争/对手信息", window)
{
    private Rectangle _bossText = new(1440, 742, 160, 36);
    private Rectangle _nextStep = new(1467, 961, 100, 48);
    private Rectangle _buff = new(247, 964, 900, 36);

    public override double GetSimilarity()
    {
        var expect = new List<string>() { "本场对局首领", "下一步" };
        var actual = _window.Recognize([_bossText, _nextStep]);
        return expect.Zip(actual).Average(x => StringHelper.GetSimilarity(x.First, x.Second));
    }

    private string NextStep()
    {
        _window.MouseMove(_nextStep.Location);
        _window.MouseClick();
        var debuffs = _window.Recognize([_buff])[0];
        Console.WriteLine($"{Name}, 词缀: {debuffs}");
        return debuffs;
    }

    public bool NextStep(IReadOnlyList<string> debuffExcludes)
    {
        var debuffs = NextStep();

        return debuffExcludes.All(x => !debuffs.Contains(x));
    }
}
