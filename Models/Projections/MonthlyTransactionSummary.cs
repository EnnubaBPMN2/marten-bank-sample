namespace marten_bank_sample.Models.Projections;

// Read Model para reporte mensual
public class MonthlyTransactionSummary
{
    // Id compuesto: year-month (ej: "2025-11")
    public string Id { get; set; } = string.Empty;

    public int Year { get; set; }
    public int Month { get; set; }

    public int TotalTransactions { get; set; }
    public decimal TotalDebited { get; set; }
    public decimal TotalCredited { get; set; }
    public int AccountsCreated { get; set; }
    public int AccountsClosed { get; set; }
    public int OverdraftAttempts { get; set; }

    public DateTimeOffset LastUpdated { get; set; }
}