namespace AsIHaveWritten.GameScripts.CurrencyWars;

using System.Drawing;
using AsIHaveWritten.Helpers;

internal class PlanePage(GameWindow window) : PageBase("货币战争/位面信息", window)
{
    private Rectangle _planeText = new(928, 360, 64, 32);
    private Rectangle _continue = new(867, 944, 190, 36);

    public override double GetSimilarity()
    {
        var expect = new List<string>() { "位面", "点击空白处继续" };
        var actual = _window.Recognize([_planeText, _continue]);
        return expect.Zip(actual).Average(x => StringHelper.GetSimilarity(x.First, x.Second));
    }

    public void NextStep()
    {
        _window.MouseMove(_continue.Location);
        _window.MouseClick();
    }
}
