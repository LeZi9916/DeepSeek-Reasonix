namespace Reasonix.Studio.Kernel.Session;

/// <summary>Whether the mounted transcript covers its history and can load more.</summary>
public sealed record HistoryReadState
{
    /// <summary>Gets a value indicating whether an initial history read succeeded.</summary>
    public bool Initialized
    {
        get; init;
    }

    /// <summary>Gets a value indicating whether a history request is in flight.</summary>
    public bool Busy
    {
        get; init;
    }

    /// <summary>Gets a value indicating whether the last read failed and can be retried.</summary>
    public bool Failed
    {
        get; init;
    }

    /// <summary>Gets a value indicating whether an earlier window exists.</summary>
    public bool HasMore
    {
        get; init;
    }

    /// <summary>Gets how many raw messages precede the window.</summary>
    public int Earlier
    {
        get; init;
    }
}
