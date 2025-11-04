using Account.Events;
using Accounting.Events;
using Marten.Events.Projections;

namespace Accounting.Projections;

// Proyección ASÍNCRONA: se procesa en background por el daemon
public class MonthlyTransactionProjection : MultiStreamProjection<MonthlyTransactionSummary, string>
{
    public MonthlyTransactionProjection()
    {
        // Identifica qué eventos debe procesar esta proyección
        Identity<AccountCreated>(e => GetMonthKey(e.CreatedAt));
        Identity<AccountCredited>(e => GetMonthKey(e.Time));
        Identity<AccountDebited>(e => GetMonthKey(e.Time));
        Identity<AccountClosed>(e => GetMonthKey(e.ClosedAt));
        Identity<InvalidOperationAttempted>(e => GetMonthKey(e.Time));
    }

    // Genera la clave compuesta "year-month"
    private static string GetMonthKey(DateTimeOffset timestamp)
    {
        return $"{timestamp.Year}-{timestamp.Month:D2}";
    }

    // Handler para AccountCreated
    public MonthlyTransactionSummary Create(AccountCreated created)
    {
        return new MonthlyTransactionSummary
        {
            Id = GetMonthKey(created.CreatedAt),
            Year = created.CreatedAt.Year,
            Month = created.CreatedAt.Month,
            AccountsCreated = 1,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    // Handler incremental para AccountCreated (si ya existe el documento)
    public void Apply(AccountCreated created, MonthlyTransactionSummary summary)
    {
        summary.AccountsCreated++;
        summary.LastUpdated = DateTimeOffset.UtcNow;
    }

    // Handler para AccountCredited
    public void Apply(AccountCredited credited, MonthlyTransactionSummary summary)
    {
        summary.TotalTransactions++;
        summary.TotalCredited += credited.Amount;
        summary.LastUpdated = DateTimeOffset.UtcNow;
    }

    // Handler para AccountDebited
    public void Apply(AccountDebited debited, MonthlyTransactionSummary summary)
    {
        summary.TotalTransactions++;
        summary.TotalDebited += debited.Amount;
        summary.LastUpdated = DateTimeOffset.UtcNow;
    }

    // Handler para AccountClosed
    public void Apply(AccountClosed closed, MonthlyTransactionSummary summary)
    {
        summary.AccountsClosed++;
        summary.LastUpdated = DateTimeOffset.UtcNow;
    }

    // Handler para InvalidOperationAttempted
    public void Apply(InvalidOperationAttempted invalid, MonthlyTransactionSummary summary)
    {
        summary.OverdraftAttempts++;
        summary.LastUpdated = DateTimeOffset.UtcNow;
    }
}