using System;
using System.Linq;
using System.Threading.Tasks;
using Account.Events;
using Accounting.Events;
using Accounting.Projections;
using Marten;
using Weasel.Core;

namespace Accounting
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection("host=localhost;database=marten_bank;password=P@ssw0rd!;username=marten_user");

                _.AutoCreateSchemaObjects = AutoCreate.All;

                _.Events.AddEventTypes(new[] {
                    typeof(AccountCreated),
                    typeof(AccountCredited),
                    typeof(AccountDebited),
                    typeof(AccountClosed)
                });

                // Proyección INLINE (síncrona): se actualiza en la misma transacción
                _.Projections.Snapshot<Account>(Marten.Events.Projections.SnapshotLifecycle.Inline);

                // Proyección ASÍNCRONA: se procesa en background por el daemon
                _.Projections.Add<MonthlyTransactionProjection>(Marten.Events.Projections.ProjectionLifecycle.Async);
            });

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

            using (var session = store.OpenSession())
            {
                // create banking accounts
                session.Events.Append(khalid.AccountId, khalid);
                session.Events.Append(bill.AccountId, bill);

                session.SaveChanges();
            }

            using (var session = store.OpenSession())
            {
                // load khalid's account
                var account = session.Load<Account>(khalid.AccountId);
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
                session.SaveChanges();
            }

            using (var session = store.OpenSession())
            {
                // load bill's account
                var account = session.Load<Account>(bill.AccountId);
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
                } else {
                   session.Events.Append(account.Id, new InvalidOperationAttempted {
                        Description = "Overdraft" 
                    }); 
                }
                // commit these changes
                session.SaveChanges();
            }

            using (var session = store.LightweightSession())
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("----- Final Balance ------");

                var accounts = session.LoadMany<Account>(khalid.AccountId, bill.AccountId);

                foreach (var account in accounts)
                {
                    Console.WriteLine(account);
                }
            }

            // NUEVO: Bill retira todo su dinero antes de cerrar la cuenta
            using (var session = store.OpenSession())
            {
                var billAccount = session.Load<Account>(bill.AccountId);

                if (billAccount != null && billAccount.Balance > 0)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Bill está retirando todo su balance de {billAccount.Balance:C} antes de cerrar...");

                    var withdrawal = new AccountDebited
                    {
                        From = bill.AccountId,
                        To = Guid.NewGuid(), // A una cuenta externa
                        Amount = billAccount.Balance,
                        Description = "Withdrawal before account closure"
                    };

                    session.Events.Append(bill.AccountId, withdrawal);
                    session.SaveChanges();
                }
            }

            // Ahora cerrar la cuenta de Bill (balance cero)
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

            using (var session = store.LightweightSession())
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("----- Final Balance (Updated) ------");

                var accounts = session.LoadMany<Account>(khalid.AccountId, bill.AccountId);

                foreach (var account in accounts)
                {
                    Console.WriteLine(account);
                }
            }

            using (var session = store.LightweightSession())
            {
                foreach (var account in new[] { khalid, bill })
                {
                    Console.WriteLine();
                    Console.WriteLine($"Transaction ledger for {account.Owner}");
                    var stream = session.Events.FetchStream(account.AccountId);
                    foreach (var item in stream)
                    {
                        Console.WriteLine(item.Data);
                    }
                    Console.WriteLine();
                }
            }

            // PROYECCIÓN ASÍNCRONA: Procesar manualmente (rebuild) para esta demo
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("----- Procesando Proyección Asíncrona (Rebuild) ------");

            using (var daemon = await store.BuildProjectionDaemonAsync())
            {
                // Rebuild: procesa todos los eventos desde el inicio
                await daemon.RebuildProjectionAsync<MonthlyTransactionProjection>(System.Threading.CancellationToken.None);
                Console.WriteLine("Proyección reconstruida exitosamente.");
            }

            // Consultar el reporte mensual
            using (var session = store.LightweightSession())
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("----- Monthly Transaction Summary (Async Projection) ------");

                // Query por el mes actual
                var currentMonthKey = $"{DateTime.UtcNow.Year}-{DateTime.UtcNow.Month:D2}";
                var summary = session.Query<MonthlyTransactionSummary>()
                    .FirstOrDefault(x => x.Id == currentMonthKey);

                if (summary != null)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"Month: {summary.Year}-{summary.Month:D2}");
                    Console.WriteLine($"Accounts Created: {summary.AccountsCreated}");
                    Console.WriteLine($"Accounts Closed: {summary.AccountsClosed}");
                    Console.WriteLine($"Total Transactions: {summary.TotalTransactions}");
                    Console.WriteLine($"Total Debited: {summary.TotalDebited:C}");
                    Console.WriteLine($"Total Credited: {summary.TotalCredited:C}");
                    Console.WriteLine($"Overdraft Attempts: {summary.OverdraftAttempts}");
                    Console.WriteLine($"Last Updated: {summary.LastUpdated}");
                }
                else
                {
                    Console.WriteLine("No summary found.");
                }
            }

            // DEMOS DE CONCURRENCY Y TIME TRAVEL
            await ConcurrencyExample.DemonstrateConcurrency(store, khalid.AccountId);
            await TimeTravelExample.DemonstrateTimeTravel(store, khalid.AccountId);

            Console.ReadLine();
        }
    }
}
