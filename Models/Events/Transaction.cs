using marten_bank_sample.Models.Projections;

namespace marten_bank_sample.Models.Events;

public abstract class Transaction
{
    public Guid To { get; set; }
    public Guid From { get; set; }
    public required string Description { get; set; }
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
    public decimal Amount { get; set; }

    public abstract void Apply(Account account);
}