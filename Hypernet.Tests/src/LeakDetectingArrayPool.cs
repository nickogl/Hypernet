namespace Hypernet.Tests;

internal sealed class LeakDetectingArrayPool<T> : AutoReleasingArrayPool<T>
{
	public override void Dispose()
	{
		var outstandingRentalCount = Rented.Count;
		base.Dispose();
		if (outstandingRentalCount > 0)
		{
			throw new InvalidOperationException($"Detected {outstandingRentalCount} leaked buffer(s)");
		}
	}
}
