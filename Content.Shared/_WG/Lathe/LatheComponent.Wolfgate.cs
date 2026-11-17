namespace Content.Shared.Lathe;

public sealed partial class LatheComponent
{
    /// <summary>
    /// Most separate batches a queue holds.
    /// </summary>
    public const int MaxQueuedBatches = 50;

    /// <summary>
    /// Index of the batch the item being printed came from.
    /// </summary>
    [ViewVariables]
    public int? PrintingBatch;
}

public sealed partial class LatheRecipeBatch
{
    /// <summary>
    /// Largest requested total for one batch.
    /// </summary>
    public const int MaxItemsRequested = 9999;
}
