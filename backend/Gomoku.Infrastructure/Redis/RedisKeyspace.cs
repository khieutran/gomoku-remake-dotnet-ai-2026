namespace Gomoku.Infrastructure.Redis;

public sealed class RedisKeyspace
{
    public RedisKeyspace(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.EndsWith(':'))
            throw new ArgumentException("Redis namespace must be nonempty and have no trailing colon.", nameof(value));
        Value = value;
    }

    public string Value { get; }

    public string Key(string logicalKey) => $"{Value}:{logicalKey}";
}
