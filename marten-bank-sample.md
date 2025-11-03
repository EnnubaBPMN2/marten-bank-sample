# Marten Bank Sample - Guía Completa de Event Sourcing

## Tabla de Contenidos
- [Introducción](#introducción)
- [Arquitectura General](#arquitectura-general)
- [Modelo de Eventos](#modelo-de-eventos)
- [Proyecciones](#proyecciones)
- [Flujo del Programa Principal](#flujo-del-programa-principal)
- [Concurrencia Optimista](#concurrencia-optimista)
- [Time Travel (Consultas Históricas)](#time-travel-consultas-históricas)
- [Verificación con SQL](#verificación-con-sql)
- [Conclusiones](#conclusiones)

---

## Introducción

Este proyecto demuestra **Event Sourcing** y **CQRS** (Command Query Responsibility Segregation) usando [Marten](https://martendb.io/), una librería .NET que convierte PostgreSQL en un potente event store y document database.

### ¿Qué es Event Sourcing?

En lugar de almacenar el **estado actual** de las entidades (como en EF Core tradicional), Event Sourcing almacena **todos los eventos** que han ocurrido. El estado actual se reconstruye aplicando (replay) estos eventos en secuencia.

**Comparación con EF Core tradicional:**

| Aspecto | EF Core Tradicional | Event Sourcing con Marten |
|---------|---------------------|----------------------------|
| Almacenamiento | Estado actual (UPDATE) | Secuencia de eventos (INSERT only) |
| Historia | Perdida (sin auditoría) | Completa e inmutable |
| Consultas | Directas sobre tablas | Proyecciones del event stream |
| Concurrencia | RowVersion/Timestamp | Versión del stream |
| Time Travel | Imposible sin snapshots | Nativo (replay hasta timestamp) |

---

## Arquitectura General

### Componentes Clave

```
┌─────────────────────────────────────────────────────────────┐
│                     WRITE SIDE (Commands)                    │
│  ┌──────────┐    ┌──────────┐    ┌──────────────────────┐  │
│  │ Command  │───▶│ Business │───▶│  Events (Append)     │  │
│  │ Handler  │    │  Logic   │    │  - AccountCreated    │  │
│  └──────────┘    └──────────┘    │  - AccountDebited    │  │
│                                    │  - AccountClosed     │  │
│                                    └──────────┬───────────┘  │
└───────────────────────────────────────────────┼──────────────┘
                                                 │
                                                 ▼
                              ┌──────────────────────────────┐
                              │   PostgreSQL Event Store     │
                              │  ┌────────────────────────┐  │
                              │  │   mt_events            │  │
                              │  │   mt_streams           │  │
                              │  └────────────────────────┘  │
                              └────────────┬─────────────────┘
                                           │
                     ┌─────────────────────┼─────────────────────┐
                     ▼                     ▼                     ▼
        ┌────────────────────┐  ┌──────────────────┐  ┌─────────────────┐
        │ READ SIDE (Queries)│  │  Inline          │  │  Async          │
        │                    │  │  Projections     │  │  Projections    │
        │  ┌──────────────┐  │  │  (Snapshot)      │  │  (Daemon)       │
        │  │   Account    │  │  │                  │  │                 │
        │  │   Balance    │◀─┼──│  mt_doc_account  │  │  mt_doc_        │
        │  │   IsClosed   │  │  │                  │  │  monthlysummary │
        │  └──────────────┘  │  └──────────────────┘  └─────────────────┘
        └────────────────────┘
```

### Tablas PostgreSQL (Marten Metadata)

| Tabla | Propósito | Equivalente EF Core |
|-------|-----------|---------------------|
| `mt_events` | Almacena todos los eventos (append-only) | Tabla de auditoría custom |
| `mt_streams` | Metadata de streams (versión, timestamp) | Índice/PK sobre agregados |
| `mt_doc_account` | Proyección inline del estado actual | Tabla `Accounts` |
| `mt_doc_monthlytransactionsummary` | Proyección asíncrona (reporte) | Vista materializada |

---

## Modelo de Eventos

Los eventos son **objetos inmutables** que describen **qué pasó** en el sistema. En Marten, los eventos se serializan como JSON y se almacenan en `mt_events`.

### Estructura de los Eventos

#### 1. `AccountCreated` - Evento de creación de cuenta

**Ubicación:** `Models/Events/AccountCreated.cs`

```csharp
public class AccountCreated
{
    public AccountCreated()
    {
        CreatedAt = DateTime.UtcNow;
    }

    public string Owner { get; set; }           // Propietario de la cuenta
    public Guid AccountId { get; set; }         // ID del stream (agregado)
    public DateTimeOffset CreatedAt { get; set; }
    public decimal StartingBalance { get; set; } = 0;

    public override string ToString()
    {
        return $"{CreatedAt} Created account for {Owner} with starting balance of {StartingBalance:C}";
    }
}
```

**Propósito:** Registra la creación de una nueva cuenta bancaria. El `AccountId` se convierte en el **stream ID** en Marten.

**Ejemplo en mt_events:**
```json
{
  "Owner": "Khalid Abuhakmeh",
  "AccountId": "823a7966-d2ba-40f2-901f-6cea3f2659cc",
  "CreatedAt": "2025-11-03T22:35:19.000839+00:00",
  "StartingBalance": 1000.0
}
```

---

#### 2. `AccountDebited` - Evento de débito (retiro/transferencia)

**Ubicación:** `Models/Events/AccountDebited.cs` (hereda de `Transaction`)

```csharp
public abstract class Transaction
{
    public Guid From { get; set; }
    public Guid To { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; }
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
}

public class AccountDebited : Transaction
{
    public void Apply(Account account)
    {
        account.Balance -= Amount;
    }

    public AccountCredited ToCredit()
    {
        return new AccountCredited
        {
            From = this.From,
            To = this.To,
            Amount = this.Amount,
            Description = this.Description,
            Time = this.Time
        };
    }
}
```

**Propósito:** Reduce el balance de una cuenta. Puede representar un retiro o el lado "envío" de una transferencia.

**Patrón de transferencia:** Cuando Khalid envía $100 a Bill:
1. Se registra `AccountDebited` en el stream de Khalid (versión 2)
2. Se registra `AccountCredited` en el stream de Bill (versión 2)

---

#### 3. `AccountCredited` - Evento de crédito (depósito)

```csharp
public class AccountCredited : Transaction
{
    public void Apply(Account account)
    {
        account.Balance += Amount;
    }
}
```

**Propósito:** Aumenta el balance de una cuenta.

---

#### 4. `InvalidOperationAttempted` - Evento de operación fallida

```csharp
public class InvalidOperationAttempted
{
    public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
    public string Description { get; set; }
}
```

**Propósito:** Registra intentos de operaciones inválidas (ej: overdraft) sin modificar el balance. Esto es clave en Event Sourcing: **los intentos fallidos también son eventos** que forman parte del historial de auditoría.

---

#### 5. `AccountClosed` - Evento de cierre de cuenta ⭐ (NUEVO)

**Ubicación:** `Models/Events/AccountClosed.cs`

```csharp
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
```

**Propósito:** Marca una cuenta como cerrada. Este evento demuestra cómo extender el sistema con nuevos tipos de eventos sin modificar el esquema de base de datos.

---

## Proyecciones

Las proyecciones transforman **eventos** en **read models** optimizados para consultas. Marten soporta dos tipos:

### 1. Proyección Inline (Síncrona) - `Account`

**Ubicación:** `Models/Projections/Account.cs`

```csharp
public class Account
{
    // Estado actual del agregado
    public Guid Id { get; set; }
    public string Owner { get; set; }
    public decimal Balance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Nuevas propiedades para AccountClosed
    public bool IsClosed { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? ClosureReason { get; set; }

    // Handlers de eventos (patrón Apply)
    public void Apply(AccountCreated created)
    {
        Id = created.AccountId;
        Owner = created.Owner;
        Balance = created.StartingBalance;
        CreatedAt = UpdatedAt = created.CreatedAt;
        Console.WriteLine($"Account created for {Owner} with Balance of {Balance:C}");
    }

    public void Apply(AccountDebited debit)
    {
        debit.Apply(this);  // Modifica Balance
        Console.WriteLine($"Debiting {Owner} ({debit.Amount:C}): {debit.Description}");
    }

    public void Apply(AccountCredited credit)
    {
        credit.Apply(this);  // Modifica Balance
        Console.WriteLine($"Crediting {Owner} {credit.Amount:C}: {credit.Description}");
    }

    public void Apply(AccountClosed closed)
    {
        IsClosed = true;
        ClosedAt = closed.ClosedAt;
        ClosureReason = closed.Reason;
        Console.WriteLine($"Account closed for {Owner}. Reason: {closed.Reason}");
    }

    // Lógica de negocio
    public bool HasSufficientFunds(AccountDebited debit)
    {
        var result = (Balance - debit.Amount) >= 0;
        if (!result)
        {
            Console.WriteLine($"{Owner} has insufficient funds for debit");
        }
        return result;
    }

    public override string ToString()
    {
        var status = IsClosed ? " [CLOSED]" : "";
        return $"{Owner} ({Id}) : {Balance:C}{status}";
    }
}
```

**Características:**
- **Síncrona:** Se actualiza en la misma transacción que los eventos
- **Consistencia inmediata:** Lees el estado actualizado de inmediato
- **Patrón Apply:** Marten invoca automáticamente `Apply(EventoX)` cuando encuentra ese tipo de evento
- **Stored in:** `mt_doc_account` (JSONB)

**Configuración en Program.cs:**
```csharp
_.Projections.Snapshot<Account>(SnapshotLifecycle.Inline);
```

---

### 2. Proyección Asíncrona - `MonthlyTransactionSummary`

**Ubicación:** `Models/Projections/MonthlyTransactionSummary.cs`

```csharp
// Read Model (documento JSONB)
public class MonthlyTransactionSummary
{
    public string Id { get; set; } = string.Empty;  // "2025-11"
    public int Year { get; set; }
    public int Month { get; set; }

    // Métricas agregadas
    public int TotalTransactions { get; set; }
    public decimal TotalDebited { get; set; }
    public decimal TotalCredited { get; set; }
    public int AccountsCreated { get; set; }
    public int AccountsClosed { get; set; }
    public int OverdraftAttempts { get; set; }

    public DateTimeOffset LastUpdated { get; set; }
}
```

**Ubicación del procesador:** `Models/Projections/MonthlyTransactionProjection.cs`

```csharp
// Proyección CROSS-STREAM: agrega eventos de TODOS los streams
public class MonthlyTransactionProjection : MultiStreamProjection<MonthlyTransactionSummary, string>
{
    public MonthlyTransactionProjection()
    {
        // Mapear eventos a claves (year-month)
        Identity<AccountCreated>(e => GetMonthKey(e.CreatedAt));
        Identity<AccountCredited>(e => GetMonthKey(e.Time));
        Identity<AccountDebited>(e => GetMonthKey(e.Time));
        Identity<AccountClosed>(e => GetMonthKey(e.ClosedAt));
        Identity<InvalidOperationAttempted>(e => GetMonthKey(e.Time));
    }

    private static string GetMonthKey(DateTimeOffset timestamp)
    {
        return $"{timestamp.Year}-{timestamp.Month:D2}";  // "2025-11"
    }

    // Crear el documento inicial
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

    // Handlers incrementales (modifican el documento existente)
    public void Apply(AccountCreated created, MonthlyTransactionSummary summary)
    {
        summary.AccountsCreated++;
        summary.LastUpdated = DateTimeOffset.UtcNow;
    }

    public void Apply(AccountCredited credited, MonthlyTransactionSummary summary)
    {
        summary.TotalTransactions++;
        summary.TotalCredited += credited.Amount;
        summary.LastUpdated = DateTimeOffset.UtcNow;
    }

    public void Apply(AccountDebited debited, MonthlyTransactionSummary summary)
    {
        summary.TotalTransactions++;
        summary.TotalDebited += debited.Amount;
        summary.LastUpdated = DateTimeOffset.UtcNow;
    }

    public void Apply(AccountClosed closed, MonthlyTransactionSummary summary)
    {
        summary.AccountsClosed++;
        summary.LastUpdated = DateTimeOffset.UtcNow;
    }

    public void Apply(InvalidOperationAttempted invalid, MonthlyTransactionSummary summary)
    {
        summary.OverdraftAttempts++;
        summary.LastUpdated = DateTimeOffset.UtcNow;
    }
}
```

**Características:**
- **Asíncrona:** Procesada en background por el ProjectionDaemon
- **Eventual consistency:** Puede haber un delay entre el evento y la actualización
- **Cross-stream:** Agrega eventos de múltiples streams (cuentas)
- **Stored in:** `mt_doc_monthlytransactionsummary` (JSONB)

**Configuración en Program.cs:**
```csharp
_.Projections.Add<MonthlyTransactionProjection>(ProjectionLifecycle.Async);
```

**Uso típico:** Dashboards, reportes, métricas que no necesitan consistencia inmediata.

---

## Flujo del Programa Principal

**Ubicación:** `Program.cs`

### Paso 1: Configurar el DocumentStore

```csharp
var store = DocumentStore.For(_ =>
{
    _.Connection("host=localhost;database=marten_bank;password=P@ssw0rd!;username=marten_user");
    _.AutoCreateSchemaObjects = AutoCreate.All;  // Crea tablas automáticamente

    // Registrar tipos de eventos
    _.Events.AddEventTypes(new[] {
        typeof(AccountCreated),
        typeof(AccountCredited),
        typeof(AccountDebited),
        typeof(AccountClosed)
    });

    // Proyecciones
    _.Projections.Snapshot<Account>(SnapshotLifecycle.Inline);
    _.Projections.Add<MonthlyTransactionProjection>(ProjectionLifecycle.Async);
});
```

**Nota:** `AutoCreateSchemaObjects = AutoCreate.All` hace que Marten cree automáticamente:
- `mt_events` (tabla de eventos)
- `mt_streams` (metadata de streams)
- `mt_doc_account` (tabla JSONB para proyección inline)
- `mt_doc_monthlytransactionsummary` (tabla JSONB para proyección async)
- Funciones PL/pgSQL auxiliares (mt_grams_*, mt_jsonb_*, etc.)

---

### Paso 2: Crear Cuentas (Append de eventos)

```csharp
var khalid = new AccountCreated
{
    Owner = "Khalid Abuhakmeh",
    AccountId = Guid.NewGuid(),
    StartingBalance = 1000m
};

var bill = new AccountCreated
{
    Owner = "Bill Boga",
    AccountId = Guid.NewGuid(),
    StartingBalance = 0m
};

using (var session = store.OpenSession())
{
    session.Events.Append(khalid.AccountId, khalid);
    session.Events.Append(bill.AccountId, bill);
    session.SaveChanges();
}
```

**¿Qué sucede internamente?**

1. **Marten inserta en `mt_events`:**
   - seq_id: 20 (global sequence)
   - stream_id: 823a7966-d2ba-40f2-901f-6cea3f2659cc (Khalid)
   - version: 1 (primer evento del stream)
   - type: "account_created"
   - data: JSON del evento

2. **Marten actualiza `mt_streams`:**
   - id: 823a7966-d2ba-40f2-901f-6cea3f2659cc
   - version: 1

3. **Proyección inline ejecuta `Account.Apply(AccountCreated)`**
4. **Marten inserta en `mt_doc_account`:**
   ```json
   {
     "Id": "823a7966-d2ba-40f2-901f-6cea3f2659cc",
     "Owner": "Khalid Abuhakmeh",
     "Balance": 1000.0,
     "IsClosed": false
   }
   ```

**Resultado en SQL:**
```sql
-- mt_events: 2 filas (seq_id 20, 21)
-- mt_streams: 2 filas (Khalid version=1, Bill version=1)
-- mt_doc_account: 2 filas
```

---

### Paso 3: Transferencia Khalid → Bill

```csharp
using (var session = store.OpenSession())
{
    var account = session.Load<Account>(khalid.AccountId);
    var amount = 100m;
    var give = new AccountDebited
    {
        Amount = amount,
        To = bill.AccountId,
        From = khalid.AccountId,
        Description = "Bill helped me out with some code."
    };

    if (account.HasSufficientFunds(give))
    {
        session.Events.Append(give.From, give);         // Débito en stream de Khalid
        session.Events.Append(give.To, give.ToCredit()); // Crédito en stream de Bill
    }
    session.SaveChanges();
}
```

**Resultado:**
- **mt_events:** 2 nuevos eventos (seq_id 22, 23)
  - seq_id 22: `account_debited` en stream de Khalid (version 2)
  - seq_id 23: `account_credited` en stream de Bill (version 2)
- **mt_streams:**
  - Khalid: version = 2
  - Bill: version = 2
- **mt_doc_account:**
  - Khalid: Balance = 900
  - Bill: Balance = 100

---

### Paso 4: Intento de Overdraft (Bill intenta gastar $1000)

```csharp
using (var session = store.OpenSession())
{
    var account = session.Load<Account>(bill.AccountId);
    var spend = new AccountDebited
    {
        Amount = 1000m,
        From = bill.AccountId,
        To = khalid.AccountId,
        Description = "Trying to buy that Ferrari"
    };

    if (account.HasSufficientFunds(spend))
    {
        // NO entra aquí
        session.Events.Append(spend.From, spend);
    }
    else
    {
        // Registra el intento fallido
        session.Events.Append(account.Id, new InvalidOperationAttempted {
            Description = "Overdraft"
        });
    }
    session.SaveChanges();
}
```

**Resultado:**
- **mt_events:** 1 nuevo evento (seq_id 24)
  - seq_id 24: `invalid_operation_attempted` en stream de Bill (version 3)
- **mt_streams:** Bill: version = 3
- **mt_doc_account:** Bill: Balance = 100 (sin cambio)

**Lección clave:** Los eventos fallidos también se registran. Esto es fundamental para auditoría y debugging.

---

### Paso 5: Cerrar Cuenta de Bill

```csharp
// Primero: retirar todo el balance
using (var session = store.OpenSession())
{
    var billAccount = session.Load<Account>(bill.AccountId);
    if (billAccount != null && billAccount.Balance > 0)
    {
        var withdrawal = new AccountDebited
        {
            From = bill.AccountId,
            To = Guid.NewGuid(),  // Cuenta externa
            Amount = billAccount.Balance,
            Description = "Withdrawal before account closure"
        };
        session.Events.Append(bill.AccountId, withdrawal);
        session.SaveChanges();
    }
}

// Luego: cerrar la cuenta
using (var session = store.OpenSession())
{
    var billAccount = session.Load<Account>(bill.AccountId);
    if (billAccount != null && billAccount.Balance == 0)
    {
        var closeEvent = new AccountClosed
        {
            AccountId = bill.AccountId,
            Reason = "Customer requested closure - zero balance",
            FinalBalance = billAccount.Balance
        };
        session.Events.Append(bill.AccountId, closeEvent);
        session.SaveChanges();
    }
}
```

**Resultado:**
- **mt_events:** 2 nuevos eventos (seq_id 25, 26)
  - seq_id 25: `account_debited` (retiro de $100) en stream de Bill (version 4)
  - seq_id 26: `account_closed` en stream de Bill (version 5)
- **mt_streams:** Bill: version = 5
- **mt_doc_account:**
  - Bill: Balance = 0, **IsClosed = true**, ClosedAt = timestamp, ClosureReason = "Customer requested..."

---

### Paso 6: Procesar Proyección Asíncrona

```csharp
// Rebuild: procesa TODOS los eventos desde el inicio
using (var daemon = await store.BuildProjectionDaemonAsync())
{
    await daemon.RebuildProjectionAsync<MonthlyTransactionProjection>(CancellationToken.None);
    Console.WriteLine("Proyección reconstruida exitosamente.");
}

// Consultar el resultado
using (var session = store.LightweightSession())
{
    var currentMonthKey = $"{DateTime.UtcNow.Year}-{DateTime.UtcNow.Month:D2}";
    var summary = session.Query<MonthlyTransactionSummary>()
        .FirstOrDefault(x => x.Id == currentMonthKey);

    if (summary != null)
    {
        Console.WriteLine($"Accounts Created: {summary.AccountsCreated}");
        Console.WriteLine($"Accounts Closed: {summary.AccountsClosed}");
        Console.WriteLine($"Total Debited: {summary.TotalDebited:C}");
        // ...
    }
}
```

**Resultado en `mt_doc_monthlytransactionsummary`:**
```json
{
  "Id": "2025-11",
  "Year": 2025,
  "Month": 11,
  "AccountsCreated": 2,
  "AccountsClosed": 1,
  "TotalTransactions": 3,
  "TotalDebited": 200.0,   // Khalid→Bill $100 + Bill retira $100
  "TotalCredited": 100.0,  // Bill recibe $100
  "OverdraftAttempts": 1
}
```

**Nota:** En producción, el ProjectionDaemon corre en background continuamente, procesando nuevos eventos a medida que llegan. Aquí usamos `RebuildProjectionAsync` para forzar el procesamiento completo para demostración.

---

## Concurrencia Optimista

**Ubicación:** `ConcurrencyExample.cs`

Event Sourcing maneja concurrencia mediante **versiones de stream**. Cada vez que agregas un evento, especificas la versión esperada del stream.

### Ejemplo: Dos usuarios modifican la misma cuenta simultáneamente

```csharp
public static async Task DemonstrateConcurrency(IDocumentStore store, Guid accountId)
{
    // Usuario 1: Lee la cuenta
    await using var session1 = store.LightweightSession();
    var account1 = await session1.LoadAsync<Account>(accountId);
    var version1 = await session1.Events.FetchStreamStateAsync(accountId);
    Console.WriteLine($"Usuario 1 - Versión del stream: {version1?.Version}");  // version = 2

    // Usuario 2: Lee la MISMA cuenta (mismo estado inicial)
    await using var session2 = store.LightweightSession();
    var account2 = await session2.LoadAsync<Account>(accountId);
    var version2 = await session2.Events.FetchStreamStateAsync(accountId);
    Console.WriteLine($"Usuario 2 - Versión del stream: {version2?.Version}");  // version = 2

    // Usuario 1: Hace un depósito
    var credit1 = new AccountCredited { Amount = 50m, Description = "Depósito Usuario 1" };
    session1.Events.Append(accountId, version1!.Version, credit1);  // Espera version=2
    await session1.SaveChangesAsync();
    Console.WriteLine("✓ Usuario 1: Transacción exitosa!");
    // Ahora el stream tiene version=3

    // Usuario 2: Intenta hacer un retiro (usando la versión OBSOLETA)
    var debit2 = new AccountDebited { Amount = 25m, Description = "Retiro Usuario 2" };
    session2.Events.Append(accountId, version2!.Version, debit2);  // Espera version=2, pero es 3!

    try
    {
        await session2.SaveChangesAsync();
        Console.WriteLine("✓ Usuario 2: Transacción exitosa!");
    }
    catch (EventStreamUnexpectedMaxEventIdException ex)
    {
        // CONFLICTO: La versión cambió
        Console.WriteLine($"✗ Usuario 2: CONFLICTO DE CONCURRENCIA!");
        Console.WriteLine($"  Esperaba versión {version2.Version}, pero el stream fue modificado");

        // ESTRATEGIA DE RESOLUCIÓN:
        // 1. Recargar el stream con la versión actual
        await using var retrySession = store.LightweightSession();
        var freshAccount = await retrySession.LoadAsync<Account>(accountId);
        var freshVersion = await retrySession.Events.FetchStreamStateAsync(accountId);

        // 2. Re-evaluar la lógica de negocio con el estado actualizado
        if (freshAccount != null && freshAccount.Balance >= 25m)
        {
            retrySession.Events.Append(accountId, freshVersion!.Version, debit2);
            await retrySession.SaveChangesAsync();
            Console.WriteLine("✓ Usuario 2: Retry exitoso!");
        }
    }
}
```

### Comparación con EF Core

| Aspecto | EF Core (RowVersion) | Marten (Stream Version) |
|---------|----------------------|-------------------------|
| Mecanismo | Columna `RowVersion` byte[] | Campo `version` en mt_streams |
| Check | `UPDATE WHERE RowVersion = @expected` | `INSERT INTO mt_events WHERE stream_version = @expected` |
| Excepción | `DbUpdateConcurrencyException` | `EventStreamUnexpectedMaxEventIdException` |
| Resolución | Re-fetch, merge, retry | Re-fetch, replay events, retry |

---

## Time Travel (Consultas Históricas)

**Ubicación:** `TimeTravelExample.cs`

Una de las ventajas clave de Event Sourcing es que puedes **reconstruir el estado de una entidad en cualquier punto del tiempo**.

### Ejemplo 1: Estado por Versión

```csharp
public static async Task DemonstrateTimeTravel(IDocumentStore store, Guid accountId)
{
    await using var session = store.LightweightSession();

    // Obtener todos los eventos del stream
    var events = await session.Events.FetchStreamAsync(accountId);
    Console.WriteLine($"Total de eventos: {events.Count}");

    // Reconstruir el estado en cada versión
    for (int version = 1; version <= events.Count; version++)
    {
        // AggregateStreamAsync aplica eventos hasta la versión especificada
        var accountAtVersion = await session.Events
            .AggregateStreamAsync<Account>(accountId, version: version);

        var evt = events[version - 1];
        Console.WriteLine($"\nVersión {version} @ {evt.Timestamp:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"  Evento: {evt.EventType}");
        Console.WriteLine($"  Balance: {accountAtVersion.Balance:C}");
    }
}
```

**Salida de ejemplo (Khalid):**
```
Versión 1 @ 2025-11-03 22:35:21
  Evento: AccountCreated
  Owner: Khalid Abuhakmeh
  Balance: $1,000.00

Versión 2 @ 2025-11-03 22:35:21
  Evento: AccountDebited
  Owner: Khalid Abuhakmeh
  Balance: $900.00
```

---

### Ejemplo 2: Estado en un Timestamp Específico

```csharp
var targetTimestamp = events[2].Timestamp;  // Después del 3er evento
var accountAtTime = await session.Events
    .AggregateStreamAsync<Account>(accountId, timestamp: targetTimestamp);

Console.WriteLine($"Balance al {targetTimestamp:yyyy-MM-dd HH:mm}: {accountAtTime.Balance:C}");
```

---

### Ejemplo 3: Análisis Histórico (Balance Máximo)

```csharp
decimal maxBalance = 0;
int versionAtMax = 0;

for (int version = 1; version <= events.Count; version++)
{
    var accountAtVersion = await session.Events
        .AggregateStreamAsync<Account>(accountId, version: version);

    if (accountAtVersion.Balance > maxBalance)
    {
        maxBalance = accountAtVersion.Balance;
        versionAtMax = version;
    }
}

Console.WriteLine($"Balance máximo: {maxBalance:C}");
Console.WriteLine($"Alcanzado en versión: {versionAtMax}");
```

### Casos de Uso de Time Travel

1. **Auditoría:** "¿Qué balance tenía el cliente el 1 de enero a las 3pm?"
2. **Debugging:** "¿Cuándo se sobrepasó el límite de $5000?"
3. **Compliance:** Recrear estados para reguladores/auditorías
4. **Análisis:** Tendencias históricas, reportes retroactivos

---

## Verificación con SQL

A continuación se presentan los resultados reales de las queries ejecutadas contra PostgreSQL:

### Query 1: Todos los Eventos (mt_events)

```sql
SELECT seq_id, stream_id, version, type, timestamp, data
FROM mt_events
ORDER BY seq_id;
```

**Resultado:**
| seq_id | stream_id | version | type | timestamp | data (resumen) |
|--------|-----------|---------|------|-----------|----------------|
| 20 | 823a7966... (Khalid) | 1 | account_created | 2025-11-03 22:35:21 | Owner: Khalid, Balance: 1000 |
| 21 | 2e1db87b... (Bill) | 1 | account_created | 2025-11-03 22:35:21 | Owner: Bill, Balance: 0 |
| 22 | 823a7966... (Khalid) | 2 | account_debited | 2025-11-03 22:35:21 | Amount: 100, To: Bill |
| 23 | 2e1db87b... (Bill) | 2 | account_credited | 2025-11-03 22:35:21 | Amount: 100, From: Khalid |
| 24 | 2e1db87b... (Bill) | 3 | invalid_operation_attempted | 2025-11-03 22:35:21 | Description: Overdraft |
| 25 | 2e1db87b... (Bill) | 4 | account_debited | 2025-11-03 22:35:21 | Amount: 100, Description: Withdrawal |
| 26 | 2e1db87b... (Bill) | 5 | **account_closed** | 2025-11-03 22:35:21 | Reason: Customer requested... |

**Observaciones:**
- **seq_id es global:** Incrementa secuencialmente para todos los streams
- **version es por stream:** Incrementa independientemente para cada cuenta
- **Bill tiene 5 eventos:** created (1) → credited (2) → invalid (3) → debited (4) → closed (5)
- **Khalid tiene 2 eventos:** created (1) → debited (2)

---

### Query 2: Evento AccountClosed en Detalle

```sql
SELECT
    seq_id,
    type,
    data->>'AccountId' as account_id,
    data->>'Reason' as reason,
    data->>'FinalBalance' as final_balance,
    data->>'ClosedAt' as closed_at,
    timestamp
FROM mt_events
WHERE type = 'account_closed';
```

**Resultado:**
| seq_id | type | account_id | reason | final_balance | closed_at | timestamp |
|--------|------|------------|--------|---------------|-----------|-----------|
| 26 | account_closed | 2e1db87b... | Customer requested closure - zero balance | 0.0 | 2025-11-03T22:35:21.278582+00:00 | 2025-11-03 22:35:21 |

---

### Query 3: Metadata de Streams (mt_streams)

```sql
SELECT id as account_id, version, is_archived, timestamp, created
FROM mt_streams
ORDER BY version DESC;
```

**Resultado:**
| account_id | version | is_archived | timestamp | created |
|------------|---------|-------------|-----------|---------|
| 2e1db87b... (Bill) | **5** | false | 2025-11-03 22:35:21 | 2025-11-03 22:35:21 |
| 823a7966... (Khalid) | 2 | false | 2025-11-03 22:35:21 | 2025-11-03 22:35:21 |

**Observación:** La versión del stream coincide con el número de eventos en ese stream.

---

### Query 4: Proyección Inline (mt_doc_account)

```sql
SELECT
    id,
    data->>'Owner' as owner,
    data->>'Balance' as balance,
    data->>'IsClosed' as is_closed,
    data->>'ClosedAt' as closed_at,
    data->>'ClosureReason' as closure_reason,
    mt_version
FROM mt_doc_account
ORDER BY data->>'Owner';
```

**Resultado:**
| id | owner | balance | is_closed | closed_at | closure_reason | mt_version |
|----|-------|---------|-----------|-----------|----------------|------------|
| 2e1db87b... | Bill Boga | 0.0 | **true** | 2025-11-03T22:35:21.278582+00:00 | Customer requested closure - zero balance | 5 |
| 823a7966... | Khalid Abuhakmeh | 900.0 | false | null | null | 2 |

**Observaciones:**
- **mt_version** indica hasta qué versión del stream se procesó
- Bill: IsClosed = **true** ✅
- Khalid: IsClosed = false (cuenta activa)

---

### Query 5: Proyección Asíncrona (mt_doc_monthlytransactionsummary)

```sql
SELECT
    id,
    data->>'AccountsCreated' as accounts_created,
    data->>'AccountsClosed' as accounts_closed,
    data->>'TotalTransactions' as total_transactions,
    data->>'TotalDebited' as total_debited,
    data->>'TotalCredited' as total_credited,
    data->>'OverdraftAttempts' as overdraft_attempts
FROM mt_doc_monthlytransactionsummary;
```

**Resultado:**
| id | accounts_created | accounts_closed | total_transactions | total_debited | total_credited | overdraft_attempts |
|----|------------------|-----------------|--------------------|--------------:|---------------:|-------------------:|
| 2025-11 | 2 | **1** | 3 | 200.0 | 100.0 | 1 |

**Desglose de TotalDebited ($200):**
- Khalid → Bill: $100
- Bill retira: $100

**Desglose de TotalCredited ($100):**
- Bill recibe de Khalid: $100

---

### Query 6: Timeline Completo de Bill

```sql
SELECT
    seq_id,
    version,
    type,
    timestamp,
    CASE
        WHEN type = 'account_created' THEN
            'Cuenta creada: ' || (data->>'Owner') || ' - Balance inicial: $' || (data->>'StartingBalance')
        WHEN type = 'account_credited' THEN
            'Crédito: +$' || (data->>'Amount') || ' - ' || (data->>'Description')
        WHEN type = 'account_debited' THEN
            'Débito: -$' || (data->>'Amount') || ' - ' || (data->>'Description')
        WHEN type = 'invalid_operation_attempted' THEN
            'Operación inválida: ' || (data->>'Description')
        WHEN type = 'account_closed' THEN
            'Cuenta cerrada: ' || (data->>'Reason')
    END as description
FROM mt_events
WHERE stream_id = '2e1db87b-45b5-4a21-8b73-b368bbca4438'
ORDER BY seq_id;
```

**Resultado:**
| seq_id | version | type | timestamp | description |
|--------|---------|------|-----------|-------------|
| 21 | 1 | account_created | 2025-11-03 22:35:21 | Cuenta creada: Bill Boga - Balance inicial: $0.0 |
| 23 | 2 | account_credited | 2025-11-03 22:35:21 | Crédito: +$100.0 - Bill helped me out with some code. |
| 24 | 3 | invalid_operation_attempted | 2025-11-03 22:35:21 | Operación inválida: Overdraft |
| 25 | 4 | account_debited | 2025-11-03 22:35:21 | Débito: -$100.0 - Withdrawal before account closure |
| 26 | 5 | account_closed | 2025-11-03 22:35:21 | Cuenta cerrada: Customer requested closure - zero balance |

**Historia completa de Bill:**
1. Cuenta creada con $0
2. Recibe $100 de Khalid
3. Intenta gastar $1000 (rechazado por overdraft)
4. Retira sus $100
5. Cuenta cerrada

---

### Query 7: Time Travel - Balance Histórico de Khalid

```sql
WITH khalid_stream AS (
    SELECT stream_id
    FROM mt_events
    WHERE data->>'Owner' = 'Khalid Abuhakmeh'
    LIMIT 1
),
events_ordered AS (
    SELECT
        version,
        type,
        timestamp,
        (data->>'StartingBalance')::numeric as starting_balance,
        (data->>'Amount')::numeric as amount
    FROM mt_events
    WHERE stream_id = (SELECT stream_id FROM khalid_stream)
    ORDER BY version
)
SELECT
    version,
    timestamp,
    type,
    SUM(
        CASE
            WHEN type = 'account_created' THEN starting_balance
            WHEN type = 'account_debited' THEN -amount
            WHEN type = 'account_credited' THEN amount
            ELSE 0
        END
    ) OVER (ORDER BY version) as balance_at_version
FROM events_ordered;
```

**Resultado:**
| version | timestamp | type | balance_at_version |
|---------|-----------|------|--------------------|
| 1 | 2025-11-03 22:35:21 | account_created | $1,000.00 |
| 2 | 2025-11-03 22:35:21 | account_debited | $900.00 |

**Interpretación:** Khalid empezó con $1000 y después del débito tiene $900. Podemos ver el balance exacto en cualquier versión.

---

### Query 8: Verificación de Integridad

```sql
SELECT
    s.id as stream_id,
    d.data->>'Owner' as owner,
    s.version as stream_version,
    d.mt_version as projection_version,
    (SELECT COUNT(*) FROM mt_events WHERE stream_id = s.id) as actual_event_count,
    CASE
        WHEN s.version = (SELECT COUNT(*) FROM mt_events WHERE stream_id = s.id)
        THEN '✓ OK'
        ELSE '✗ MISMATCH'
    END as integrity_check
FROM mt_streams s
LEFT JOIN mt_doc_account d ON s.id = d.id
ORDER BY owner;
```

**Resultado:**
| stream_id | owner | stream_version | projection_version | actual_event_count | integrity_check |
|-----------|-------|----------------|--------------------|--------------------|-----------------|
| 2e1db87b... | Bill Boga | 5 | 5 | 5 | ✓ OK |
| 823a7966... | Khalid Abuhakmeh | 2 | 2 | 2 | ✓ OK |

**Observación:** Todas las versiones coinciden, lo que confirma la integridad del event store.

---

### Query 9: Resumen Ejecutivo (Dashboard)

```sql
SELECT
    'Total Accounts' as metric,
    COUNT(*)::text as value
FROM mt_doc_account
UNION ALL
SELECT
    'Active Accounts',
    COUNT(*)::text
FROM mt_doc_account
WHERE (data->>'IsClosed')::boolean = false
UNION ALL
SELECT
    'Closed Accounts',
    COUNT(*)::text
FROM mt_doc_account
WHERE (data->>'IsClosed')::boolean = true
UNION ALL
SELECT
    'Total Events',
    COUNT(*)::text
FROM mt_events;
```

**Resultado:**
| metric | value |
|--------|-------|
| Total Accounts | 2 |
| Active Accounts | 1 |
| Closed Accounts | 1 |
| Total Events | 7 |

---

## Conclusiones

### Ventajas de Event Sourcing con Marten

1. **Auditoría Completa:** Cada cambio está registrado con timestamp, tipo de evento, y datos completos. No se puede borrar historia.

2. **Time Travel:** Puedes reconstruir el estado de cualquier entidad en cualquier punto del pasado.

3. **Debugging Facilitado:** Cuando hay un bug, puedes reproducir exactamente qué eventos causaron el estado incorrecto.

4. **Proyecciones Flexibles:**
   - Inline para consistencia inmediata
   - Async para reportes/dashboards con eventual consistency

5. **Escalabilidad de Lecturas:** Las proyecciones pueden escalar independientemente del event store.

6. **Concurrencia Optimista Built-in:** Marten maneja versiones de streams automáticamente.

7. **Inmutabilidad:** Los eventos nunca se modifican o borran, solo se agregan (append-only).

---

### Desventajas y Consideraciones

1. **Curva de Aprendizaje:** Event Sourcing requiere un cambio de mentalidad vs. CRUD tradicional.

2. **Complejidad de Queries:** Consultas complejas requieren proyecciones bien diseñadas.

3. **Migración de Eventos:** Si cambias la estructura de un evento, necesitas estrategias de migración (upcasting).

4. **Eventual Consistency:** Las proyecciones asíncronas no están sincronizadas inmediatamente.

5. **Tamaño de Almacenamiento:** Guardar todos los eventos consume más espacio que solo el estado actual.

---

### Patrones Clave Aprendidos

| Patrón | Descripción | Ubicación |
|--------|-------------|-----------|
| **Event Sourcing** | Almacenar eventos en vez de estado | Todo el proyecto |
| **CQRS** | Separar comandos (write) de queries (read) | Eventos vs. Proyecciones |
| **Aggregate** | Entidad raíz de consistencia (ej: Account) | Models/Projections/Account.cs |
| **Stream** | Secuencia de eventos para un agregado | mt_streams |
| **Projection** | Transformar eventos a read models | MonthlyTransactionProjection |
| **Optimistic Concurrency** | Detectar conflictos por versión | ConcurrencyExample.cs |
| **Event Replay** | Reconstruir estado aplicando eventos | TimeTravelExample.cs |

---

### Mapeo a Conceptos de EF Core

| Concepto Marten | Equivalente EF Core | Notas |
|-----------------|---------------------|-------|
| `IDocumentStore` | `DbContext` | Punto de entrada a la BD |
| `IDocumentSession` | `DbContext` + ChangeTracker | Unidad de trabajo |
| `Events.Append()` | `Add()` / `Update()` | Agregar cambios |
| `SaveChanges()` | `SaveChanges()` | Persistir en BD |
| `mt_events` | Tabla de auditoría custom | Event store |
| `mt_doc_account` | Tabla `Accounts` | Estado actual |
| Stream version | RowVersion / Timestamp | Concurrencia optimista |
| Inline projection | Trigger / Interceptor | Actualización síncrona |
| Async projection | Vista materializada | Eventual consistency |

---

### Próximos Pasos Sugeridos

1. **Explorar Snapshots:** Para streams con miles de eventos, usa snapshots para optimizar el replay.

2. **Implementar Sagas:** Coordinar transacciones entre múltiples agregados.

3. **Agregar Validaciones:** Implementar reglas de negocio más complejas en los handlers de eventos.

4. **Migrations de Eventos:** Aprender estrategias para cambiar la estructura de eventos sin romper el historial.

5. **Monitoring del Daemon:** Configurar alertas cuando las proyecciones asíncronas se atrasan.

6. **Testing:** Escribir tests basados en eventos (Given-When-Then):
   ```csharp
   // Given: AccountCreated con $1000
   // When: AccountDebited por $100
   // Then: Balance = $900
   ```

---

### Referencias

- **Documentación Marten:** https://martendb.io/
- **Event Sourcing:** https://martinfowler.com/eaaDev/EventSourcing.html
- **CQRS:** https://martinfowler.com/bliki/CQRS.html
- **Repositorio Original:** https://github.com/khalidabuhakmeh/marten-bank-sample

---

**Generado:** 2025-11-03
**Versión de Marten:** 7.31.2
**PostgreSQL:** 18
**.NET:** 8.0
