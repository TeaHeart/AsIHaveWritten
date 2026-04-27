namespace AsIHaveWritten.GameScripts.CurrencyWars;

using Common.Helpers;
using System.Drawing;

internal class LeavePage(GameWindow window) : PageBase("货币战争/离开", window)
{
    private Rectangle _exit = new(704, 728, 140, 36);
    private Rectangle _saveAndExit = new(1136, 728, 140, 36);

    public override double GetSimilarity()
    {
        var expect = new List<string>() { "放弃并结算", "暂时离开" };
        var actual = _window.Recognize([_exit, _saveAndExit]);
        return expect.Zip(actual).Average(x => StringHelper.GetSimilarity(x.First, x.Second));
    }

    public void Leave(int mode)
    {
        if (mode == 0)
        {
            _window.MouseMove(_exit.Location);
            _window.MouseClick();
        }
        else if (mode == 1)
        {
            _window.MouseMove(_saveAndExit.Location);
            _window.MouseClick();
        }
    }
}
