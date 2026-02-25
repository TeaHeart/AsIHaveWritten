namespace AsIHaveWritten.GameScripts.CurrencyWars;

using System.Drawing;
using AsIHaveWritten.Helpers;

internal class HomePage(GameWindow window) : PageBase("货币战争/主页", window)
{
    private Rectangle _startWar = new(1446, 953, 260, 48);

    public override double GetSimilarity()
    {
        var expect = new List<string>() { "开始货币战争" };
        var actual = _window.Recognize([_startWar]);
        return expect.Zip(actual).Average(x => StringHelper.GetSimilarity(x.First, x.Second));
    }

    public void StartWar()
    {
        _window.MouseMove(_startWar.Location);
        _window.MouseClick();
    }
}
