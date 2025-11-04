using marten_bank_sample.Models.Events;

namespace marten_bank_sample.Models.Projections;

public class Account
{
    public Guid Id { get; set; }
    public required string Owner { get; set; }
    public decimal Balance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool IsClosed { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ClosureReason { get; set; }

    public void Apply(AccountCreated created)
    {
        Id = created.AccountId;
        Owner = created.Owner;
        Balance = created.StartingBalance;
        CreatedAt = UpdatedAt = created.CreatedAt;

        Console.ForegroundColor = ConsoleColor.DarkMagenta;
        Console.WriteLine($"Account created for {Owner} with Balance of {Balance:C}");
    }

    public bool HasSufficientFunds(AccountDebited debit)
    {
        var result = Balance - debit.Amount >= 0;
        if (!result)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{Owner} has insufficient funds for debit");
        }

        return result;
    }

    public void Apply(AccountDebited debit)
    {
        debit.Apply(this);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Debiting {Owner} ({debit.Amount:C}): {debit.Description}");
    }

    public void Apply(AccountCredited credit)
    {
        credit.Apply(this);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Crediting {Owner} {credit.Amount:C}: {credit.Description}");
    }

    public void Apply(AccountClosed closed)
    {
        IsClosed = true;
        ClosedAt = closed.ClosedAt;
        ClosureReason = closed.Reason;
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine($"Account closed for {Owner}. Reason: {closed.Reason}");
    }

    public override string ToString()
    {
        Console.ForegroundColor = ConsoleColor.White;
        var status = IsClosed ? " [CLOSED]" : "";
        return $"{Owner} ({Id}) : {Balance:C}{status}";
    }
}