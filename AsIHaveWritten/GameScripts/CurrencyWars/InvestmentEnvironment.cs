namespace AsIHaveWritten.GameScripts.CurrencyWars;

using Common.Extensions;
using Common.Helpers;
using System;
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
        var cards = _window.DetectAndRecognize([_card1, _card2, _card3]);
        cards.ForEach(x => Console.WriteLine($"{Name}, 投资环境: {x}"));
        return cards;
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

    private static int FindMatch(IReadOnlyList<string> cards, IReadOnlyList<string> envPriority, int higherCount = int.MaxValue)
    {
        if (envPriority.Count == 0)
        {
            // 无限定选默认中间
            return 1;
        }

        foreach (var env in envPriority.Take(higherCount))
        {
            var index = cards.FindIndex(x => x.Contains(env));
            if (index >= 0)
            {
                return index;
            }
        }

        return -1;
    }

    public bool TrySelect(IReadOnlyList<string> envPriority, int higherCount)
    {
        var cards = GetInvestmentEnvironment();
        var index = FindMatch(cards, envPriority, higherCount);

        // 首次没有匹配的，或者在higherCount个之后的，直接刷新
        if (index == -1)
        {
            Refresh();
            Thread.Sleep(1000);

            cards = GetInvestmentEnvironment();
            index = FindMatch(cards, envPriority);
        }

        var hasMatch = index != -1;
        var blueSeaIndex = cards.FindIndex(x => x.Contains("蓝海"));
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
            cards = GetInvestmentEnvironment();
            index = FindMatch(cards, envPriority);
            hasMatch = index != -1;

            Select(1);
            Thread.Sleep(1000);
            Confirm();
        }

        return hasMatch;
    }
}
