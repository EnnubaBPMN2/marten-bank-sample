using System;

namespace Accounting.Events
{
    public class AccountClosed
    {
        public AccountClosed()
        {
            ClosedAt = DateTime.UtcNow;
        }

        public Guid AccountId { get; set; }
        public DateTimeOffset ClosedAt { get; set; }
        public string Reason { get; set; } = string.Empty;
        public decimal FinalBalance { get; set; }

        public override string ToString()
        {
            return $"{ClosedAt} Account closed with final balance of {FinalBalance:C}. Reason: {Reason}";
        }
    }
}
