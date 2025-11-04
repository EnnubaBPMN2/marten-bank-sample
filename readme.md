# Marten Bank Sample - Event Sourcing with .NET & PostgreSQL

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Marten](https://img.shields.io/badge/Marten-7.31.2-00A4EF?logo=postgresql)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18-336791?logo=postgresql)
![License](https://img.shields.io/badge/license-MIT-green)

A comprehensive educational example demonstrating **Event Sourcing** and **CQRS** patterns using [Marten](https://martendb.io/) as an event store with PostgreSQL. This project showcases a simple banking system with accounts, transactions, projections, concurrency handling, and time-travel queries.

## Table of Contents

- [What is Event Sourcing?](#what-is-event-sourcing)
- [Features](#features)
- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Project Structure](#project-structure)
- [Key Concepts](#key-concepts)
  - [Events](#events)
  - [Projections](#projections)
  - [Concurrency Control](#concurrency-control)
  - [Time Travel Queries](#time-travel-queries)
- [Database Schema](#database-schema)
- [Example Output](#example-output)
- [Advanced Topics](#advanced-topics)
- [Troubleshooting](#troubleshooting)
- [Contributing](#contributing)
- [Resources](#resources)

## What is Event Sourcing?

Event Sourcing is a pattern where **state changes are stored as a sequence of events** rather than just storing the current state. Instead of updating records in place, we append immutable events to an event store.

| Traditional Approach (EF Core) | Event Sourcing with Marten |
|-------------------------------|---------------------------|
| UPDATE accounts SET balance = 150 WHERE id = 1 | INSERT event: AccountDebited(-50) |
| Current state only | Complete audit trail |
| Lost history | Time travel possible |
| Overwrite data | Append-only |
| Potential data loss | Guaranteed audit log |

## Features

- **Event Store**: PostgreSQL-backed event store using Marten
- **Multiple Event Types**: AccountCreated, AccountCredited, AccountDebited, AccountClosed, InvalidOperationAttempted
- **Inline Projections**: Real-time aggregate snapshots (Account state)
- **Async Projections**: Background-processed read models (Monthly summaries)
- **Optimistic Concurrency**: Stream versioning to handle concurrent modifications
- **Time Travel**: Query historical state at any point in time
- **Complete Audit Trail**: Every state change is permanently recorded
- **CQRS Pattern**: Separation of command (events) and query (projections) models

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Application Layer                       │
│  (Program.cs - Commands: Create, Credit, Debit, Close)      │
└──────────────────┬──────────────────────────────────────────┘
                   │ Commands
                   ▼
┌─────────────────────────────────────────────────────────────┐
│                    Marten Event Store                        │
│                   (PostgreSQL: mt_events)                    │
└──────────┬────────────────────────────────────┬─────────────┘
           │                                     │
           │ Event Stream                        │ Event Stream
           ▼                                     ▼
┌──────────────────────┐              ┌─────────────────────────┐
│  Inline Projection   │              │  Async Projection       │
│  (Account Snapshot)  │              │  (Monthly Summary)      │
│  ↳ Immediate         │              │  ↳ Eventual Consistency│
│    Consistency       │              │  ↳ ProjectionDaemon     │
└──────────────────────┘              └─────────────────────────┘
```

**Event Flow:**
1. Command creates an event (e.g., AccountDebited)
2. Event is appended to mt_events table
3. Inline projection updates Account snapshot immediately
4. Async projection updates MonthlyTransactionSummary in background

## Prerequisites

- **.NET 8.0 SDK** or later - [Download](https://dot.net)
- **PostgreSQL 14+** - [Download](https://www.postgresql.org/download/)
- **IDE**: Visual Studio 2022, VS Code, or JetBrains Rider (optional)

## Quick Start

### 1. Clone the Repository

```bash
git clone https://github.com/yourusername/marten-bank-sample.git
cd marten-bank-sample
```

### 2. Set Up PostgreSQL Database

```sql
-- Connect to PostgreSQL as superuser
psql -U postgres

-- Create database and user
CREATE DATABASE marten_bank;
CREATE USER marten_user WITH PASSWORD 'P@ssw0rd!';

-- Grant permissions
GRANT ALL PRIVILEGES ON DATABASE marten_bank TO marten_user;
\c marten_bank
GRANT ALL ON SCHEMA public TO marten_user;
ALTER DATABASE marten_bank OWNER TO marten_user;
```

### 3. Configure Connection String

The connection string is configured in [Program.cs:18](Program.cs#L18):

```csharp
_.Connection("host=localhost;database=marten_bank;password=P@ssw0rd!;username=marten_user");
```

Update the password if you used a different one.

### 4. Restore Dependencies

```bash
dotnet restore
```

### 5. Build and Run

```bash
dotnet build
dotnet run
```

You should see output demonstrating account creation, transactions, concurrency handling, and time travel queries.

## Project Structure

```
marten-bank-sample/
├── Models/
│   ├── Events/
│   │   ├── AccountCreated.cs         # Event: New account opened
│   │   ├── AccountCredited.cs        # Event: Money deposited
│   │   ├── AccountDebited.cs         # Event: Money withdrawn
│   │   ├── AccountClosed.cs          # Event: Account closed
│   │   └── InvalidOperationAttempted.cs # Event: Overdraft attempt
│   └── Projections/
│       ├── Account.cs                # Inline projection (aggregate)
│       ├── MonthlyTransactionSummary.cs       # Async read model
│       └── MonthlyTransactionProjection.cs    # Async projection handler
├── ConcurrencyExample.cs             # Demonstrates optimistic locking
├── TimeTravelExample.cs              # Demonstrates historical queries
├── Program.cs                        # Main application entry point
├── marten-bank-sample.csproj         # Project file
├── marten-bank-sample.sln            # Solution file
└── marten-bank-sample.md             # Comprehensive technical documentation
```

## Key Concepts

### Events

Events are immutable facts that represent something that happened in the past. All events are stored in the `mt_events` table.

**AccountCreated.cs** - Opening a new account:
```csharp
public class AccountCreated
{
    public Guid AccountId { get; set; }
    public string Owner { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

**AccountDebited.cs** - Withdrawing money:
```csharp
public class AccountDebited
{
    public Guid From { get; set; }      // Source account
    public Guid To { get; set; }        // Destination account
    public decimal Amount { get; set; }
    public string Description { get; set; }
    public DateTimeOffset Time { get; set; }
}
```

**Appending Events:**
```csharp
session.Events.Append(accountId, new AccountDebited
{
    From = accountId,
    To = Guid.NewGuid(),
    Amount = 50m,
    Description = "ATM Withdrawal"
});
await session.SaveChangesAsync();
```

### Projections

Projections transform events into queryable read models.

#### Inline Projections (Immediate Consistency)

**Account.cs** - Real-time snapshot of account state:
```csharp
public class Account
{
    public Guid Id { get; set; }
    public string Owner { get; set; }
    public decimal Balance { get; set; }
    public bool IsClosed { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    // Event handlers
    public void Apply(AccountCreated created) { /* ... */ }
    public void Apply(AccountCredited credited) { Balance += credited.Amount; }
    public void Apply(AccountDebited debited) { Balance -= debited.Amount; }
    public void Apply(AccountClosed closed) { IsClosed = true; }
}
```

Configured in Program.cs:
```csharp
_.Projections.Snapshot<Account>(SnapshotLifecycle.Inline);
```

#### Async Projections (Eventual Consistency)

**MonthlyTransactionProjection.cs** - Cross-stream aggregation:
```csharp
public class MonthlyTransactionProjection : MultiStreamProjection<MonthlyTransactionSummary, string>
{
    public MonthlyTransactionProjection()
    {
        // Group events by month
        Identity<AccountCreated>(e => GetMonthKey(e.CreatedAt));
        Identity<AccountCredited>(e => GetMonthKey(e.Time));
        Identity<AccountDebited>(e => GetMonthKey(e.Time));
    }

    private static string GetMonthKey(DateTimeOffset timestamp)
        => $"{timestamp.Year}-{timestamp.Month:D2}";

    public void Apply(AccountCredited credited, MonthlyTransactionSummary summary)
    {
        summary.TotalTransactions++;
        summary.TotalCredited += credited.Amount;
    }
}
```

Configured in Program.cs:
```csharp
_.Projections.Add<MonthlyTransactionProjection>(ProjectionLifecycle.Async);
```

Processing async projections:
```csharp
using (var daemon = store.BuildProjectionDaemon())
{
    await daemon.RebuildProjectionAsync<MonthlyTransactionProjection>(CancellationToken.None);
}
```

### Concurrency Control

Marten uses **optimistic concurrency** with stream versioning. When you append events, you can specify the expected version:

```csharp
// User 1: Fetch current version
var version1 = await session.Events.FetchStreamStateAsync(accountId);

// User 1: Append with expected version
session.Events.Append(accountId, version1.Version, new AccountCredited { Amount = 50m });
await session.SaveChangesAsync(); // ✓ Success

// User 2: Tries to append with outdated version
try
{
    session2.Events.Append(accountId, version1.Version, new AccountDebited { Amount = 25m });
    await session2.SaveChangesAsync(); // ✗ Throws exception
}
catch (EventStreamUnexpectedMaxEventIdException)
{
    // Reload current state and retry
    var freshVersion = await session.Events.FetchStreamStateAsync(accountId);
    session.Events.Append(accountId, freshVersion.Version, event);
    await session.SaveChangesAsync(); // ✓ Success with current version
}
```

See [ConcurrencyExample.cs](ConcurrencyExample.cs) for a complete demonstration.

### Time Travel Queries

Event Sourcing enables querying historical state at any point in time:

**Query state at specific version:**
```csharp
var accountAtVersion3 = await session.Events
    .AggregateStreamAsync<Account>(accountId, version: 3);
```

**Query state at specific timestamp:**
```csharp
var accountOnJan1 = await session.Events
    .AggregateStreamAsync<Account>(accountId, timestamp: new DateTime(2025, 1, 1));
```

**Reconstruct state evolution:**
```csharp
for (int version = 1; version <= events.Count; version++)
{
    var accountAtVersion = await session.Events
        .AggregateStreamAsync<Account>(accountId, version: version);
    Console.WriteLine($"V{version}: Balance = {accountAtVersion.Balance:C}");
}
```

See [TimeTravelExample.cs](TimeTravelExample.cs) for complete demonstrations.

## Database Schema

Marten automatically creates the following core tables:

### mt_events (Event Store)

| Column | Type | Description |
|--------|------|-------------|
| seq_id | bigint | Global event sequence number (identity) |
| id | uuid | Unique event ID |
| stream_id | uuid | Aggregate/stream identifier |
| version | integer | Event version within stream |
| data | jsonb | Event payload (JSON) |
| type | varchar | Event type name |
| timestamp | timestamptz | When event occurred |
| tenant_id | varchar | Multi-tenancy support |
| mt_dotnet_type | varchar | .NET CLR type |

### mt_doc_account (Account Projection)

| Column | Type | Description |
|--------|------|-------------|
| id | uuid | Account ID (stream_id) |
| data | jsonb | Account state (Owner, Balance, IsClosed, etc.) |
| mt_last_modified | timestamptz | Last projection update |
| mt_version | integer | Stream version at projection |
| mt_deleted | boolean | Soft delete flag |
| mt_deleted_at | timestamptz | Deletion timestamp |

### mt_doc_monthlytransactionsummary (Async Projection)

| Column | Type | Description |
|--------|------|-------------|
| id | varchar | Month key ("2025-11") |
| data | jsonb | Monthly statistics (TotalTransactions, TotalDebited, etc.) |
| mt_last_modified | timestamptz | Last projection update |

## Example Output

```
Creating Khalid's Account...
✓ Account created for Khalid. ID: 6d3e7a21-...

===== Bill's Banking Activity =====
Creating Bill's Account...
✓ Account created for Bill. ID: 8f2c4b93-...
✓ Current Balance: $0.00

Making a deposit of $100...
✓ Credited +$100.00 - Balance: $100.00

Making a withdrawal of $50...
✓ Debited -$50.00 - Balance: $50.00

Closing account...
Account closed for Bill. Reason: Customer request

===== MONTHLY PROJECTION (Async) =====
Processing async projections...
✓ Projection rebuild completed

Month: 2025-11
  Accounts Created: 2
  Accounts Closed: 1
  Total Transactions: 2
  Total Debited: $50.00
  Total Credited: $100.00
  Overdraft Attempts: 0

===== CONCURRENCY DEMO =====
Simulando dos usuarios modificando la misma cuenta simultáneamente...
Usuario 1 - Versión del stream: 3
Usuario 1 - Balance actual: $50.00

Usuario 1 intenta hacer un depósito de $50...
✓ Usuario 1: Transacción exitosa!

Usuario 2 intenta hacer un retiro de $25 (usando versión obsoleta)...
✗ Usuario 2: CONFLICTO DE CONCURRENCIA!
  Esperaba versión 3
  El stream fue modificado por otro usuario.

Usuario 2: Reintentando con datos actualizados...
Nueva versión: 4
Nuevo balance: $100.00
✓ Usuario 2: Retry exitoso!

===== TIME TRAVEL DEMO =====
Consultando el estado de la cuenta en diferentes momentos...

Total de eventos en el stream: 5

Versión 1 @ 2025-11-03 15:23:45
  Evento: AccountCreated
  Balance: $0.00

Versión 2 @ 2025-11-03 15:23:46
  Evento: AccountCredited
  Balance: $100.00

Versión 3 @ 2025-11-03 15:23:47
  Evento: AccountDebited
  Balance: $50.00

Balance máximo: $100.00
Alcanzado en versión: 2
```

## Advanced Topics

### SQL Queries for Verification

**View all events:**
```sql
SELECT
    seq_id,
    stream_id,
    version,
    type,
    timestamp,
    data
FROM mt_events
ORDER BY timestamp DESC;
```

**View specific account state:**
```sql
SELECT
    id,
    data->>'Owner' AS owner,
    (data->>'Balance')::numeric AS balance,
    (data->>'IsClosed')::boolean AS is_closed
FROM mt_doc_account
WHERE id = '8f2c4b93-...';
```

**Monthly summary report:**
```sql
SELECT
    data->>'Month' AS month,
    (data->>'AccountsCreated')::int AS accounts_created,
    (data->>'AccountsClosed')::int AS accounts_closed,
    (data->>'TotalTransactions')::int AS total_transactions,
    (data->>'TotalDebited')::numeric AS total_debited,
    (data->>'TotalCredited')::numeric AS total_credited
FROM mt_doc_monthlytransactionsummary
ORDER BY id DESC;
```

**Event stream integrity check:**
```sql
SELECT
    stream_id,
    COUNT(*) AS event_count,
    MAX(version) AS max_version
FROM mt_events
GROUP BY stream_id
HAVING COUNT(*) <> MAX(version);  -- Should return 0 rows
```

**Audit trail for specific account:**
```sql
SELECT
    version,
    type,
    timestamp,
    data->>'Amount' AS amount,
    data->>'Description' AS description
FROM mt_events
WHERE stream_id = '8f2c4b93-...'
ORDER BY version;
```

### Extending the System

**Adding a new event type:**

1. Create the event class:
```csharp
public class AccountFrozen
{
    public Guid AccountId { get; set; }
    public string Reason { get; set; }
    public DateTimeOffset FrozenAt { get; set; }
}
```

2. Register in Marten configuration:
```csharp
_.Events.AddEventTypes(new[] { typeof(AccountFrozen) });
```

3. Add Apply handler to Account projection:
```csharp
public void Apply(AccountFrozen frozen)
{
    IsFrozen = true;
    FrozenReason = frozen.Reason;
}
```

## Troubleshooting

### Connection Issues

**Error:** `password authentication failed for user "marten_user"`

**Solution:** Verify password in connection string matches database:
```sql
ALTER USER marten_user WITH PASSWORD 'P@ssw0rd!';
```

### Permission Issues

**Error:** `permission denied for schema public`

**Solution:** Grant schema permissions:
```sql
GRANT ALL ON SCHEMA public TO marten_user;
ALTER DATABASE marten_bank OWNER TO marten_user;
```

### Concurrency Exceptions

**Error:** `EventStreamUnexpectedMaxEventIdException`

**Solution:** This is expected behavior for concurrent modifications. Implement retry logic:
```csharp
catch (EventStreamUnexpectedMaxEventIdException)
{
    var freshVersion = await session.Events.FetchStreamStateAsync(accountId);
    session.Events.Append(accountId, freshVersion.Version, @event);
    await session.SaveChangesAsync();
}
```

### Projection Not Updating

**Issue:** Async projections not reflecting latest events

**Solution:** Manually rebuild projection:
```csharp
using (var daemon = store.BuildProjectionDaemon())
{
    await daemon.RebuildProjectionAsync<YourProjection>(CancellationToken.None);
}
```

### Build Warnings

**Warning:** Obsolete API warnings (20 warnings)

**Note:** These are informational only. The code works correctly with Marten 7.x. Future versions will update to newer APIs.

## Contributing

Contributions are welcome! Please feel free to submit issues or pull requests.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Resources

### Documentation
- **[Marten Documentation](https://martendb.io/)** - Official Marten docs
- **[Event Sourcing Pattern](https://martinfowler.com/eaaDev/EventSourcing.html)** - Martin Fowler's explanation
- **[CQRS Pattern](https://martinfowler.com/bliki/CQRS.html)** - Command Query Responsibility Segregation
- **[marten-bank-sample.md](marten-bank-sample.md)** - Comprehensive technical documentation with SQL examples

### Learning Path
1. Read [marten-bank-sample.md](marten-bank-sample.md) for detailed explanations
2. Run the program and observe the output
3. Execute SQL queries in [marten-bank-sample.md](marten-bank-sample.md) to explore the database
4. Experiment with [ConcurrencyExample.cs](ConcurrencyExample.cs) and [TimeTravelExample.cs](TimeTravelExample.cs)
5. Add your own event types and projections

### Community
- **Marten Gitter**: [https://gitter.im/JasperFx/marten](https://gitter.im/JasperFx/marten)
- **JasperFx Discord**: [https://discord.gg/WMxrvegf8H](https://discord.gg/WMxrvegf8H)

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- Original repository by [Khalid Abuhakmeh](https://github.com/khalidabuhakmeh/marten-bank-sample)
- [Jeremy D. Miller](https://github.com/jeremydmiller) and the JasperFx team for creating Marten
- The .NET and PostgreSQL communities

---

**Made with Event Sourcing and CQRS** | **Powered by Marten and PostgreSQL**
# marten-bank-sample
