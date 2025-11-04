namespace marten_bank_sample.Models.Events;

public class InvalidOperationAttempted
{
    public required string Description { get; set; }
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;

    public override string ToString()
    {
        return $"{Time} Attempted Invalid Action: {Description}";
    }
}