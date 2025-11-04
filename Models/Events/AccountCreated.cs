namespace marten_bank_sample.Models.Events;

public class AccountCreated
{
    public AccountCreated()
    {
        CreatedAt = DateTime.UtcNow;
    }

    public string Owner { get; set; }
    public Guid AccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public decimal StartingBalance { get; set; } = 0;

    public override string ToString()
    {
        return $"{CreatedAt} Created account for {Owner} with starting balance of {StartingBalance.ToString("C")}";
    }
}