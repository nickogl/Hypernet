using System.Buffers;

namespace Hypernet.Tests;

internal class AutoReleasingArrayPool<T> : ArrayPool<T>, IDisposable
{
	private readonly Lock _gate = new();
	private readonly HashSet<T[]> _rented = [];
	private bool _disposed;

	protected IReadOnlyCollection<T[]> Rented
	{
		get
		{
			lock (_gate)
			{
				return [.. _rented];
			}
		}
	}

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

	public virtual void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_rented.Clear();
		}
	}
}
