namespace AsIHaveWritten.GameScripts;

internal abstract class PageBase(string name, GameWindow window) : IDisposable
{
    public string Name { get; } = name;
    protected readonly GameWindow _window = window;
    public abstract double GetSimilarity();
    public virtual void Dispose() { }
}
