using marten_bank_sample.Models.Events;
using marten_bank_sample.Models.Projections;
using Marten;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Weasel.Core;

namespace marten_bank_sample;

public class Program
{
    public static async Task Main(string[] args)
    {
        // 📋 Cargar configuración desde appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", false, true)
            .AddJsonFile(
                $"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json",
                true)
            .AddEnvironmentVariables()
            .Build();

        // Obtener connection string desde configuración
        var connectionString = configuration.GetConnectionString("Marten")
                               ?? throw new InvalidOperationException(
                                   "Connection string 'Marten' not found in configuration");

        // Obtener settings de Marten
        var useAsyncDaemon = configuration.GetValue<bool>("MartenSettings:UseAsyncDaemon");

        // 🧹 MEJORA 1: Limpiar la base de datos antes de cada ejecución
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("🧹 Limpiando base de datos...");
        await CleanDatabaseAsync(connectionString);
        Console.WriteLine("✅ Base de datos limpiada.\n");
        Console.ResetColor();

        var store = DocumentStore.For(_ =>
        {
            _.Connection(connectionString);

            _.AutoCreateSchemaObjects = AutoCreate.All;

            _.Events.AddEventTypes(new[]
            {
                typeof(AccountCreated),
                typeof(AccountCredited),
                typeof(AccountDebited),
                typeof(AccountClosed)
            });

            // Proyección INLINE (síncrona): se actualiza en la misma transacción
            _.Projections.Snapshot<Account>(SnapshotLifecycle.Inline);

            // Proyección ASÍNCRONA: se procesa en background por el daemon
            _.Projections.Add<MonthlyTransactionProjection>(ProjectionLifecycle.Async);
        });

        // NUEVO: Iniciar el daemon asíncrono si está habilitado
        IProjectionDaemon? daemon = null;
        if (useAsyncDaemon)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("✅ Async daemon HABILITADO - Las proyecciones se procesarán automáticamente");
            Console.WriteLine();
            daemon = await store.BuildProjectionDaemonAsync();
            await daemon.StartAllAsync();
        }
        else
        {
            // Nota: Marten puede generar su propia advertencia automáticamente
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("⚠️  Async daemon está deshabilitado en esta demo.");
            Console.WriteLine("   Las proyecciones se procesarán manualmente usando RebuildProjectionAsync().");
            Console.WriteLine("   Para producción, consulta ASYNC_DAEMON_PRODUCTION.md");
            Console.WriteLine();
        }

        Console.ResetColor();

        var khalid = new AccountCreated
        {
            Owner = "Khalid Abuhakmeh",
            AccountId = Guid.NewGuid(),
            StartingBalance = 1000m
        };

        var bill = new AccountCreated
        {
            Owner = "Bill Boga",
            AccountId = Guid.NewGuid()
        };

        await using (var session = store.LightweightSession())
        {
            // create banking accounts
            session.Events.Append(khalid.AccountId, khalid);
            session.Events.Append(bill.AccountId, bill);

            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            // load khalid's account
            var account = await session.LoadAsync<Account>(khalid.AccountId)
                          ?? throw new InvalidOperationException($"Account {khalid.AccountId} not found");
            // let's be generous
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
                session.Events.Append(give.From, give);
                session.Events.Append(give.To, give.ToCredit());
            }

            // commit these changes
            await session.SaveChangesAsync();
        }

        await using (var session = store.LightweightSession())
        {
            // load bill's account
            var account = await session.LoadAsync<Account>(bill.AccountId)
                          ?? throw new InvalidOperationException($"Account {bill.AccountId} not found");
            // let's try to over spend
            var amount = 1000m;
            var spend = new AccountDebited
            {
                Amount = amount,
                From = bill.AccountId,
                To = khalid.AccountId,
                Description = "Trying to buy that Ferrari"
            };

            if (account.HasSufficientFunds(spend))
            {
                // should not get here
                session.Events.Append(spend.From, spend);
                session.Events.Append(spend.To, spend.ToCredit());
            }
            else
            {
                session.Events.Append(account.Id, new InvalidOperationAttempted
                {
                    Description = "Overdraft"
                });
            }

            // commit these changes
            await session.SaveChangesAsync();
        }

        using (var session = store.LightweightSession())
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("----- Final Balance ------");

            var accounts = await session.LoadManyAsync<Account>(khalid.AccountId, bill.AccountId);

            foreach (var account in accounts) Console.WriteLine(account);
            Console.ResetColor();
        }

        // NUEVO: Bill retira todo su dinero antes de cerrar la cuenta
        await using (var session = store.LightweightSession())
        {
            var billAccount = await session.LoadAsync<Account>(bill.AccountId);

            if (billAccount != null && billAccount.Balance > 0)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(
                    $"💸 Bill está retirando todo su balance de {billAccount.Balance:C} antes de cerrar...");
                Console.ResetColor();

                var withdrawal = new AccountDebited
                {
                    From = bill.AccountId,
                    To = Guid.NewGuid(), // A una cuenta externa
                    Amount = billAccount.Balance,
                    Description = "Withdrawal before account closure"
                };

                session.Events.Append(bill.AccountId, withdrawal);
                await session.SaveChangesAsync();
            }
        }

        // Ahora cerrar la cuenta de Bill (balance cero)
        await using (var session = store.LightweightSession())
        {
            var billAccount = await session.LoadAsync<Account>(bill.AccountId);

            if (billAccount != null && billAccount.Balance == 0)
            {
                var closeEvent = new AccountClosed
                {
                    AccountId = bill.AccountId,
                    Reason = "Customer requested closure - zero balance",
                    FinalBalance = billAccount.Balance
                };

                session.Events.Append(bill.AccountId, closeEvent);
                await session.SaveChangesAsync();
            }
        }

        using (var session = store.LightweightSession())
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("----- Final Balance (Updated) ------");

            var accounts = await session.LoadManyAsync<Account>(khalid.AccountId, bill.AccountId);

            foreach (var account in accounts) Console.WriteLine(account);
            Console.ResetColor();
        }

        using (var session = store.LightweightSession())
        {
            foreach (var account in new[] { khalid, bill })
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"📋 Transaction ledger for {account.Owner}");
                Console.ResetColor();
                var stream = await session.Events.FetchStreamAsync(account.AccountId);
                foreach (var item in stream) Console.WriteLine(item.Data);
                Console.WriteLine();
            }
        }

        // PROYECCIÓN ASÍNCRONA: Comportamiento según daemon
        if (useAsyncDaemon && daemon != null)
        {
            // Con daemon habilitado: esperar a que procese los eventos
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("----- ⏳ Esperando a que el daemon procese las proyecciones... ------");

            // Dar tiempo al daemon para procesar
            await Task.Delay(2000);

            Console.WriteLine("✅ Daemon ha procesado los eventos en background.");
            Console.ResetColor();
        }
        else
        {
            // Sin daemon: procesar manualmente (rebuild)
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("----- 🔄 Procesando Proyección Asíncrona (Rebuild Manual) ------");

            using (var tempDaemon = await store.BuildProjectionDaemonAsync())
            {
                // Rebuild: procesa todos los eventos desde el inicio
                await tempDaemon.RebuildProjectionAsync<MonthlyTransactionProjection>(CancellationToken.None);
                Console.WriteLine("✅ Proyección reconstruida exitosamente.");
            }

            Console.ResetColor();
        }

        // Consultar el reporte mensual
        using (var session = store.LightweightSession())
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("----- 📊 Monthly Transaction Summary (Async Projection) ------");
            Console.ResetColor();

            // Query por el mes actual
            var currentMonthKey = $"{DateTime.UtcNow.Year}-{DateTime.UtcNow.Month:D2}";
            var summary = session.Query<MonthlyTransactionSummary>()
                .FirstOrDefault(x => x.Id == currentMonthKey);

            if (summary != null)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"📅 Month: {summary.Year}-{summary.Month:D2}");
                Console.WriteLine($"👥 Accounts Created: {summary.AccountsCreated}");
                Console.WriteLine($"🚪 Accounts Closed: {summary.AccountsClosed}");
                Console.WriteLine($"💳 Total Transactions: {summary.TotalTransactions}");
                Console.WriteLine($"💸 Total Debited: {summary.TotalDebited:C}");
                Console.WriteLine($"💰 Total Credited: {summary.TotalCredited:C}");
                Console.WriteLine($"⚠️  Overdraft Attempts: {summary.OverdraftAttempts}");
                Console.WriteLine($"🕐 Last Updated: {summary.LastUpdated}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠️  No summary found.");
                Console.ResetColor();
            }
        }

        // DEMOS DE CONCURRENCY Y TIME TRAVEL
        await ConcurrencyExample.DemonstrateConcurrency(store, khalid.AccountId);
        await TimeTravelExample.DemonstrateTimeTravel(store, khalid.AccountId);

        // Detener el daemon antes de salir
        if (daemon != null)
        {
            await daemon.StopAllAsync();
            daemon.Dispose();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("🎉 Demo completado. Presiona Enter para salir...");
        Console.ResetColor();
        Console.ReadLine();
    }

    /// <summary>
    ///     🧹 MEJORA 1: Limpia todas las tablas de Marten para comenzar con datos frescos
    /// </summary>
    private static async Task CleanDatabaseAsync(string connectionString)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(@"
                -- Limpiar tablas principales
                TRUNCATE TABLE mt_events CASCADE;
                TRUNCATE TABLE mt_streams CASCADE;
                
                -- Limpiar proyecciones
                TRUNCATE TABLE mt_doc_account CASCADE;
                TRUNCATE TABLE mt_doc_monthlytransactionsummary CASCADE;
                
                -- Limpiar metadata del daemon (si existe)
                DO $$ 
                BEGIN
                    IF EXISTS (SELECT FROM pg_tables WHERE tablename = 'mt_event_progression') THEN
                        TRUNCATE TABLE mt_event_progression CASCADE;
                    END IF;
                END $$;
            ", conn);

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Error al limpiar la base de datos: {ex.Message}");
            Console.ResetColor();
        }
    }
}