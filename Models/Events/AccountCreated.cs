namespace marten_bank_sample.Models.Events;

public class AccountCreated
{
    public required string Owner { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public decimal StartingBalance { get; set; }

    public override string ToString()
    {
        return $"{CreatedAt} Created account for {Owner} with starting balance of {StartingBalance:C}";
    }
}