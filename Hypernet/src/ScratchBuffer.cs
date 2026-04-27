using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Hypernet;

/// <summary>
/// Provides caller-supplied inline storage before it falls back to pooled buffers.
/// Use this type to eliminate memory pooling overhead in the common case on hot paths.
/// </summary>
internal ref struct ScratchBuffer<T> : IDisposable
	where T : unmanaged
{
	internal Span<T> _inline;
	private T[]? _rented;
	private int _length;

	public readonly int Length => _length;

	[UnscopedRef]
	public readonly ref T this[int index] => ref Items[index];

	[UnscopedRef]
	public readonly Span<T> Items => _rented is null
		? _inline[.._length]
		: _rented.AsSpan(0, _length);

	public ScratchBuffer(Span<T> inlineStorage)
	{
		_inline = inlineStorage;
	}

	public unsafe ScratchBuffer(T* inlineStorage, int inlineCapacity)
	{
		_inline = new Span<T>(inlineStorage, inlineCapacity);
	}

	[UnscopedRef]
	public readonly ReadOnlySpan<T>.Enumerator GetEnumerator()
	{
		ReadOnlySpan<T> readOnlyItems = Items;
		return readOnlyItems.GetEnumerator();
	}

	public void Push(T item, int maxLength)
	{
		if (_length + 1 > maxLength)
		{
			ThrowMaxLengthExceeded(maxLength);
		}

		Push(item);
	}

	public void Push(T item)
	{
		if (_rented is null && _length >= _inline.Length)
		{
			_rented = ArrayPool<T>.Shared.Rent(GetRentLength(_length * 2));
			_inline.CopyTo(_rented);
		}
		if (_rented is null)
		{
			_inline[_length++] = item;
			return;
		}

		if (_length >= _rented.Length)
		{
			var newRented = ArrayPool<T>.Shared.Rent(GetRentLength(_length * 2));
			_rented[.._length].CopyTo(newRented);
			ArrayPool<T>.Shared.Return(_rented);
			_rented = newRented;
		}
		_rented[_length++] = item;
	}

	public T Pop()
	{
		return _rented is null
			? _inline[--_length]
			: _rented[--_length];
	}

	public void Dispose()
	{
		if (_rented is not null)
		{
			ArrayPool<T>.Shared.Return(_rented);
			_rented = null;
			_length = 0;
		}
	}

	private static int GetRentLength(int minimumLength)
	{
		return minimumLength > 0
			? (int)BitOperations.RoundUpToPowerOf2((uint)minimumLength)
			: 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowMaxLengthExceeded(int maxLength)
	{
		throw new InvalidOperationException($"Maximum scratch buffer length of '{maxLength}' exceeded.");
	}
}
