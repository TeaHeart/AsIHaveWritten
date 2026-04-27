namespace AsIHaveWritten.GameScripts.CurrencyWars;

using Common.Helpers;
using System.Drawing;

internal class PreparePage(GameWindow window) : PageBase("货币战争/备战页面", window)
{
    private Rectangle _buyExp = new(244, 848, 110, 32);
    private Rectangle _shop = new(1593, 966, 60, 32);
    private Rectangle _battle = new(1780, 727, 80, 48);
    private Point _leave = new(61, 67);

    public override double GetSimilarity()
    {
        var expect = new List<string>() { "购买经验", "商店", "出战" };
        var actual = _window.Recognize([_buyExp, _shop, _battle]);
        return expect.Zip(actual).Average(x => StringHelper.GetSimilarity(x.First, x.Second));
    }

    public void Leave()
    {
        _window.MouseMove(_leave);
        _window.MouseClick();
    }
}
