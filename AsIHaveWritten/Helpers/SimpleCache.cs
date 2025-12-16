namespace AsIHaveWritten.Helpers;

public class SimpleCache<T>(Func<T> creater, TimeSpan cacheTime, Func<T?, T?>? cloner = null, Action<T?>? disposer = null) : IDisposable
{
    private readonly object _syncRoot = new();
    private DateTime _lastTime;
    private T? _value;
    private bool _disposed;

    public T? Value
    {
        get
        {
            lock (_syncRoot)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (DateTime.Now - _lastTime > cacheTime)
                {
                    Refresh();
                }
                return cloner == null ? _value : cloner(_value);
            }
        }
    }

    public void Refresh()
    {
        lock (_syncRoot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            disposer?.Invoke(_value);
            _value = creater();
            _lastTime = DateTime.Now;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (!_disposed)
            {
                disposer?.Invoke(_value);
                _disposed = true;
            }
        }
    }
}
