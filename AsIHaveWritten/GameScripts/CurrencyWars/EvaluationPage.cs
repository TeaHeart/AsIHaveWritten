namespace AsIHaveWritten.GameScripts.CurrencyWars;

using System.Drawing;
using AsIHaveWritten.Helpers;

internal class EvaluationPage(GameWindow window) : PageBase("货币战争/对局评价", window)
{
    private Rectangle _evaluation = new(900, 353, 130, 48);
    private Rectangle _nextStep = new(910, 870, 100, 36);
    private Rectangle _toHome = new(878, 877, 100, 48);

    public override double GetSimilarity()
    {
        var expect = new List<string>() { "对局评价", "下一步" };
        var actual = _window.Recognize([_evaluation, _nextStep]);
        var score1 = expect.Zip(actual).Average(x => StringHelper.GetSimilarity(x.First, x.Second));
        var score2 = StringHelper.GetSimilarity("下一步", _window.Recognize([_toHome])[0]);
        var score3 = StringHelper.GetSimilarity("返回货币战争", _window.Recognize([_toHome])[0]);
        return Math.Max(score1, Math.Max(score2, score3));
    }

    public void NextStep()
    {
        _window.MouseMove(_nextStep.Location);
        _window.MouseClick();
        Thread.Sleep(1000);

        _window.MouseMove(_toHome.Location);
        _window.MouseClick();
        Thread.Sleep(1000);

        _window.MouseMove(_toHome.Location);
        _window.MouseClick();
        Thread.Sleep(1000);
    }
}
