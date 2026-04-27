namespace AsIHaveWritten.GameScripts.CurrencyWars;

using Common.Helpers;
using System.Drawing;

internal class InvestmentEnvironment(GameWindow window) : PageBase("货币战争/投资环境", window)
{
    private Rectangle _card1 = new(212, 205, 450, 655);
    private Rectangle _card2 = new(735, 205, 450, 655);
    private Rectangle _card3 = new(1256, 205, 450, 655);
    private Rectangle _investmentEnvironment = new(892, 77, 140, 48);
    private Rectangle _confirm = new(1040, 960, 80, 48);
    private Point _refresh = new(674, 986);

    public override double GetSimilarity()
    {
        var expect = new List<string>() { "投资环境", "确认" };
        var actual = _window.Recognize([_investmentEnvironment, _confirm]);
        return expect.Zip(actual).Average(x => StringHelper.GetSimilarity(x.First, x.Second));
    }

    private IReadOnlyList<string> GetInvestmentEnvironment()
    {
        return _window.DetectAndRecognize([_card1, _card2, _card3]);
    }

    private void Refresh()
    {
        _window.MouseMove(_refresh);
        _window.MouseClick();
    }

    private void Select(int index)
    {
        var target = index switch
        {
            0 => _card1,
            1 => _card2,
            2 => _card3,
            _ => throw new IndexOutOfRangeException()
        };
        _window.MouseMove(target.Location);
        _window.MouseClick();
    }

    private void Confirm()
    {
        _window.MouseMove(_confirm.Location);
        _window.MouseClick();
    }

    public bool TrySelect(Predicate<string> cond)
    {
        var cards = GetInvestmentEnvironment();
        var index = -1;
        var blueSeaIndex = -1;

        for (int i = 0; i < cards.Count; i++)
        {
            if (cond(cards[i]))
            {
                index = i;
                break;
            }
        }

        if (index == -1)
        {
            Refresh();
            Thread.Sleep(1000);
            cards = GetInvestmentEnvironment();
            for (int i = 0; i < cards.Count; i++)
            {
                if (cond(cards[i]))
                {
                    index = i;
                    break;
                }

                if (cards[i].Contains("蓝海"))
                {
                    blueSeaIndex = i;
                }
            }
        }

        var hasMatch = index != -1;
        // 没有匹配默认选蓝海或中间
        if (index == -1)
        {
            if (blueSeaIndex != -1)
            {
                index = blueSeaIndex;
            }
            else
            {
                index = 1;
            }
        }

        Select(index);
        Thread.Sleep(1000);
        Confirm();

        // 蓝海 特殊处理
        if (index == blueSeaIndex)
        {
            Thread.Sleep(1000);
            hasMatch = cond(_window.DetectAndRecognize([_card2])[0]);
            Select(1);
            Thread.Sleep(1000);
            Confirm();
        }

        return hasMatch;
    }
}
