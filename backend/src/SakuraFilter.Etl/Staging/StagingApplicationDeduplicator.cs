namespace SakuraFilter.Etl.Staging;

/// <summary>
/// dataa0827 的整行去重器。
/// WHY: 同一个 OEM NO 1 下的不同车型必须保留，只有整行哈希重复才可丢弃。
/// </summary>
public sealed class StagingApplicationDeduplicator
{
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public long AcceptedCount { get; private set; }
    public long DuplicateCount { get; private set; }

    public bool TryAccept(string rowHash)
    {
        if (!_seen.Add(rowHash))
        {
            DuplicateCount++;
            return false;
        }

        AcceptedCount++;
        return true;
    }
}
