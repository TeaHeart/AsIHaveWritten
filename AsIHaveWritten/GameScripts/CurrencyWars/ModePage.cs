namespace AsIHaveWritten.GameScripts.CurrencyWars;

using Common.Helpers;
using System.Drawing;

internal class ModePage(GameWindow window) : PageBase("货币战争/模式选择", window)
{
    private Rectangle _enterWar = new(1549, 936, 177, 48);
    private Rectangle _standardMode = new(86, 206, 140, 48);
    private Rectangle _overclockMode = new(86, 394, 140, 48);

    public override double GetSimilarity()
    {
        var expect = new List<string>() { "标准博弈", "超频博弈" };
        var actual = _window.Recognize([_standardMode, _overclockMode]);
        return expect.Zip(actual).Average(x => StringHelper.GetSimilarity(x.First, x.Second));
    }

    private void ChangeMode(Mode mode)
    {
        _window.MouseMove(mode switch
        {
            Mode.Standard => _standardMode.Location,
            Mode.Overclock => _overclockMode.Location,
            _ => throw new InvalidOperationException(),
        });
        _window.MouseClick();
    }

    public void NextStep(Mode mode)
    {
        ChangeMode(mode);
        Thread.Sleep(1000);
        _window.MouseMove(_enterWar.Location);
        _window.MouseClick();
    }
}

internal enum Mode
{
    Standard,
    Overclock
}
