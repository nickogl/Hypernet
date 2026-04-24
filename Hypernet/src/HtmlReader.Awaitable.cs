using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Hypernet;

public ref partial struct HtmlReader
{
	[EditorBrowsable(EditorBrowsableState.Never)]
	public readonly struct Awaitable
	{
		private readonly ValueTask<Input> _source;

		internal Awaitable(ValueTask<Input> source)
		{
			_source = source;
		}

		public Awaiter GetAwaiter()
		{
			return new Awaiter(_source.GetAwaiter());
		}

		public readonly struct Awaiter : ICriticalNotifyCompletion
		{
			private readonly ValueTaskAwaiter<Input> _inner;

			internal Awaiter(ValueTaskAwaiter<Input> inner)
			{
				_inner = inner;
			}

			public bool IsCompleted => _inner.IsCompleted;

			public HtmlReader GetResult()
			{
				return new HtmlReader(_inner.GetResult());
			}

			public void OnCompleted(Action continuation)
			{
				_inner.OnCompleted(continuation);
			}

			public void UnsafeOnCompleted(Action continuation)
			{
				_inner.UnsafeOnCompleted(continuation);
			}
		}
	}
}
