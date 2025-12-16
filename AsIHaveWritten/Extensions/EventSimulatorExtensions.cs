namespace AsIHaveWritten.Extensions;

using SharpHook;
using SharpHook.Data;
using System.Drawing;

public static class EventSimulatorExtensions
{
    public static void SimulateMouseMovement(this IEventSimulator simulator, Point location)
    {
        simulator.SimulateMouseMovement((short)location.X, (short)location.Y);
    }

    public static void SimulateMouseClick(this IEventSimulator simulator, MouseButton button, Point? location = null, int ms = 0)
    {
        if (location is Point p)
        {
            simulator.SimulateMouseMovement(p);
        }

        simulator.SimulateMousePress(button);
        Thread.Sleep(ms);
        simulator.SimulateMouseRelease(button);
    }

    public static void SimulateKeyClick(this IEventSimulator simulator, KeyCode[] code, int ms = 0)
    {
        code.ForEach(x => simulator.SimulateKeyPress(x));
        Thread.Sleep(ms);
        code.ForEach(x => simulator.SimulateKeyRelease(x));
    }
}
