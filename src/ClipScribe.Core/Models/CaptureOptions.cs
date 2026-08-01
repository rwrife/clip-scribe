namespace ClipScribe.Core.Models;

public sealed record CaptureOptions(
    int MaxHistoryItems = 5_000,
    TimeSpan? Retention = null)
{
    public CaptureOptions Validate()
    {
        if (MaxHistoryItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxHistoryItems), "Max history items must be > 0.");
        }

        if (Retention is { } retention && retention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Retention), "Retention must be positive when set.");
        }

        return this;
    }
}
