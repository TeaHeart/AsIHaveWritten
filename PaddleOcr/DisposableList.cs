namespace PaddleOcr;

using Microsoft.ML.OnnxRuntime;

internal sealed class DisposableList<T> : List<T>, IDisposableReadOnlyCollection<T> where T : IDisposable
{
    private bool _disposed;

    public DisposableList() { }
    public DisposableList(int count) : base(count) { }
    public DisposableList(IEnumerable<T> collection) : base(collection) { }

    public void Dispose()
    {
        if (!_disposed)
        {
            for (int i = Count - 1; i >= 0; i--)
            {
                this[i]?.Dispose();
            }
            Clear();
            _disposed = true;
        }
    }
}
