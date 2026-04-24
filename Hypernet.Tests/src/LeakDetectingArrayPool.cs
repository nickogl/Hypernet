using System.Buffers;

namespace Hypernet.Tests;

internal sealed class LeakDetectingArrayPool<T> : ArrayPool<T>, IDisposable
{
	private readonly Lock _gate = new();
	private readonly HashSet<T[]> _rented = [];
	private bool _disposed;

	public override T[] Rent(int minimumLength)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		var buffer = new T[Math.Max(minimumLength, 1)];
		lock (_gate)
		{
			_rented.Add(buffer);
		}

		return buffer;
	}

	public override void Return(T[] array, bool clearArray = false)
	{
		ArgumentNullException.ThrowIfNull(array);

		lock (_gate)
		{
			if (!_rented.Remove(array))
			{
				throw new InvalidOperationException("Returned buffer was not rented from this pool or returned twice");
			}
		}

		if (clearArray)
		{
			Array.Clear(array);
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_disposed = true;
			if (_rented.Count > 0)
			{
				throw new InvalidOperationException($"Detected {_rented.Count} leaked buffer(s)");
			}
		}
	}
}
