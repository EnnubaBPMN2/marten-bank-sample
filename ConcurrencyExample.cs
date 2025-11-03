using System;
using System.Threading.Tasks;
using Account.Events;
using Accounting.Events;
using Marten;
using Marten.Exceptions;

namespace Accounting
{
    public class ConcurrencyExample
    {
        public static async Task DemonstrateConcurrency(IDocumentStore store, Guid accountId)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("===== CONCURRENCY DEMO =====");
            Console.WriteLine("Simulando dos usuarios modificando la misma cuenta simultáneamente...");

            try
            {
                // Usuario 1: Lee la cuenta y su versión
                await using var session1 = store.LightweightSession();
                var account1 = await session1.LoadAsync<Account>(accountId);
                var version1 = await session1.Events.FetchStreamStateAsync(accountId);

                Console.WriteLine($"Usuario 1 - Versión del stream: {version1?.Version}");
                Console.WriteLine($"Usuario 1 - Balance actual: {account1?.Balance:C}");

                // Usuario 2: Lee la misma cuenta (mismo estado inicial)
                await using var session2 = store.LightweightSession();
                var account2 = await session2.LoadAsync<Account>(accountId);
                var version2 = await session2.Events.FetchStreamStateAsync(accountId);

                Console.WriteLine($"Usuario 2 - Versión del stream: {version2?.Version}");
                Console.WriteLine($"Usuario 2 - Balance actual: {account2?.Balance:C}");

                // Usuario 1: Hace un depósito esperando estar en la versión actual
                Console.WriteLine("\nUsuario 1 intenta hacer un depósito de $50...");
                var credit1 = new AccountCredited
                {
                    From = accountId,
                    To = accountId,
                    Amount = 50m,
                    Description = "Depósito Usuario 1"
                };

                // IMPORTANTE: Especificamos la versión esperada para optimistic concurrency
                session1.Events.Append(accountId, version1!.Version, credit1);
                await session1.SaveChangesAsync();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Usuario 1: Transacción exitosa!");

                // Usuario 2: Intenta hacer un retiro, pero basándose en la versión ANTERIOR
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\nUsuario 2 intenta hacer un retiro de $25 (usando versión obsoleta)...");
                var debit2 = new AccountDebited
                {
                    From = accountId,
                    To = Guid.NewGuid(),
                    Amount = 25m,
                    Description = "Retiro Usuario 2"
                };

                // Esto FALLARÁ porque la versión cambió
                session2.Events.Append(accountId, version2!.Version, debit2);

                try
                {
                    await session2.SaveChangesAsync();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✓ Usuario 2: Transacción exitosa!");
                }
                catch (EventStreamUnexpectedMaxEventIdException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"✗ Usuario 2: CONFLICTO DE CONCURRENCIA!");
                    Console.WriteLine($"  Esperaba versión {version2.Version}");
                    Console.WriteLine($"  El stream fue modificado por otro usuario.");
                    Console.WriteLine($"  Detalles: {ex.Message}");
                    Console.WriteLine("\nEstrategia de resolución:");
                    Console.WriteLine("  1. Recargar el stream con la versión actual");
                    Console.WriteLine("  2. Re-evaluar la lógica de negocio");
                    Console.WriteLine("  3. Reintentar la operación");

                    // Retry: Recargar y reintentar
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("\nUsuario 2: Reintentando con datos actualizados...");
                    await using var retrySession = store.LightweightSession();
                    var freshAccount = await retrySession.LoadAsync<Account>(accountId);
                    var freshVersion = await retrySession.Events.FetchStreamStateAsync(accountId);

                    Console.WriteLine($"Nueva versión: {freshVersion?.Version}");
                    Console.WriteLine($"Nuevo balance: {freshAccount?.Balance:C}");

                    // Validar de nuevo con el estado actual
                    if (freshAccount != null && freshAccount.Balance >= 25m)
                    {
                        retrySession.Events.Append(accountId, freshVersion!.Version, debit2);
                        await retrySession.SaveChangesAsync();
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("✓ Usuario 2: Retry exitoso!");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error inesperado: {ex.Message}");
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("===== FIN CONCURRENCY DEMO =====\n");
        }
    }
}
