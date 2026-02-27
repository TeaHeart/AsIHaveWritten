namespace AsIHaveWritten.GameScripts.CurrencyWars;

using System.Drawing;
using AsIHaveWritten.Helpers;

internal class RankPage(GameWindow window) : PageBase("货币战争/职级选择", window)
{
    private Rectangle _toTopRank = new(1323, 944, 140, 48);
    private Rectangle _beginWar = new(1622, 944, 140, 48);
    private Rectangle _difficulty = new(506, 927, 80, 64);
    private Point _upDifficulty = new(960, 140);
    private Point _downDifficulty = new(960, 850);

    public override double GetSimilarity()
    {
        var expect = new List<string>() { "返回最高职级", "开始对局" };
        var actual = _window.Recognize([_toTopRank, _beginWar]);
        return expect.Zip(actual).Average(x => StringHelper.GetSimilarity(x.First, x.Second));
    }

    private void ChangeDifficulty(int targetDifficulty)
    {
        if (targetDifficulty == (int)Rank.None)
        {
            return;
        }
        if (targetDifficulty == (int)Rank.Max)
        {
            if (_window.Recognize([_toTopRank])[0] == "返回最高职级")
            {
                _window.MouseMove(_toTopRank.Location);
                _window.MouseClick();
                Thread.Sleep(5000);
            }
            return;
        }
        while (true)
        {
            Thread.Sleep(200);
            var diff = _window.Recognize([_difficulty])[0];
            var value = int.Parse(diff);
            Console.WriteLine($"{Name}, 当前难度: {diff}, 目标难度: {targetDifficulty}");

            if (value < targetDifficulty)
            {
                _window.MouseMove(_upDifficulty);
                _window.MouseClick();
            }
            else if (value > targetDifficulty)
            {
                _window.MouseMove(_downDifficulty);
                _window.MouseClick();
            }
            else
            {
                break;
            }
        }
    }

    public void NextStep(int targetDifficulty)
    {
        ChangeDifficulty(targetDifficulty);
        _window.MouseMove(_beginWar.Location);
        _window.MouseClick();
    }
}

internal enum Rank
{
    // 保持原本
    None = 0,
    A0 = 1,
    A1 = 6,
    A2 = 11,
    A3 = 16,
    A4 = 23,
    A5 = 30,
    A6 = 39,
    A7 = 49,
    A8 = 61,
    Max = 255
}
