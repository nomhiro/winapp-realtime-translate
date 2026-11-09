namespace RealtimeTranslate;

internal sealed class HistoryFollowState
{
    internal const double BottomTolerance = 0.001;

    public bool IsFollowing { get; private set; } = true;

    public void Update(double verticalOffset, double viewportHeight, double extentHeight)
    {
        IsFollowing = extentHeight <= viewportHeight + BottomTolerance
            || verticalOffset + viewportHeight >= extentHeight - BottomTolerance;
    }

    public void ReturnToLatest() => IsFollowing = true;

    public void Pause() => IsFollowing = false;
}
