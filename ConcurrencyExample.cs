using marten_bank_sample.Models.Events;
using marten_bank_sample.Models.Projections;
using Marten;
using Marten.Exceptions;

namespace marten_bank_sample;

public class ConcurrencyExample
{
    public static async Task DemonstrateConcurrency(IDocumentStore store, Guid accountId)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("===== 🔒 CONCURRENCY DEMO =====");
        Console.WriteLine("Simulando dos usuarios modificando la misma cuenta simultáneamente...");
        Console.ResetColor();

        try
        {
            // Usuario 1: Lee la cuenta y su versión
            await using var session1 = store.LightweightSession();
            var account1 = await session1.LoadAsync<Account>(accountId);
            var version1 = await session1.Events.FetchStreamStateAsync(accountId);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n👤 Usuario 1 - Versión del stream: {version1?.Version}");
            Console.WriteLine($"   Balance actual: {account1?.Balance:C}");

            // Usuario 2: Lee la misma cuenta (mismo estado inicial)
            await using var session2 = store.LightweightSession();
            var account2 = await session2.LoadAsync<Account>(accountId);
            var version2 = await session2.Events.FetchStreamStateAsync(accountId);

            Console.WriteLine($"\n👤 Usuario 2 - Versión del stream: {version2?.Version}");
            Console.WriteLine($"   Balance actual: {account2?.Balance:C}");
            Console.ResetColor();

            // Usuario 1: Hace un depósito esperando estar en la versión actual
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("👤 Usuario 1 intenta hacer un depósito de $50...");
            Console.ResetColor();

            var credit1 = new AccountCredited
            {
                From = accountId,
                To = accountId,
                Amount = 50m,
                Description = "Depósito Usuario 1"
            };

            // ⭐ No especificar versión en la primera transacción (para que no falle)
            session1.Events.Append(accountId, credit1);
            await session1.SaveChangesAsync();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("   ✅ Usuario 1: Transacción exitosa!");
            Console.ResetColor();

            // Obtener la nueva versión después del commit
            var newVersion1 = await session1.Events.FetchStreamStateAsync(accountId);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"   📌 Nueva versión del stream: {newVersion1?.Version}");
            Console.ResetColor();

            // Usuario 2: Intenta hacer un retiro, pero basándose en la versión ANTERIOR
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("👤 Usuario 2 intenta hacer un retiro de $25 (usando versión obsoleta)...");
            Console.ResetColor();

            var debit2 = new AccountDebited
            {
                From = accountId,
                To = Guid.NewGuid(),
                Amount = 25m,
                Description = "Retiro Usuario 2"
            };

            // ⭐ Esto FALLARÁ porque intentamos usar version2 (la versión antigua)
            // pero el stream ya fue actualizado por Usuario 1
            session2.Events.Append(accountId, version2!.Version, debit2);

            try
            {
                await session2.SaveChangesAsync();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("   ✅ Usuario 2: Transacción exitosa!");
                Console.ResetColor();
            }
            catch (EventStreamUnexpectedMaxEventIdException)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("   ❌ Usuario 2: CONFLICTO DE CONCURRENCIA!");
                Console.WriteLine($"   📍 Esperaba versión {version2.Version}");
                Console.WriteLine($"   📍 Pero la versión actual es {newVersion1?.Version}");
                Console.WriteLine("   🔄 El stream fue modificado por otro usuario.");
                Console.ResetColor();

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("🛠️  Estrategia de resolución:");
                Console.WriteLine("   1️⃣  Recargar el stream con la versión actual");
                Console.WriteLine("   2️⃣  Re-evaluar la lógica de negocio");
                Console.WriteLine("   3️⃣  Reintentar la operación");
                Console.ResetColor();

                // Retry: Recargar y reintentar
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("🔄 Usuario 2: Reintentando con datos actualizados...");

                await using var retrySession = store.LightweightSession();
                var freshAccount = await retrySession.LoadAsync<Account>(accountId);
                var freshVersion = await retrySession.Events.FetchStreamStateAsync(accountId);

                Console.WriteLine($"   📌 Nueva versión: {freshVersion?.Version}");
                Console.WriteLine($"   💰 Nuevo balance: {freshAccount?.Balance:C}");
                Console.ResetColor();

                // Validar de nuevo con el estado actual
                if (freshAccount != null && freshAccount.Balance >= 25m)
                {
                    // ⭐ MEJOR: No especificar versión en el retry para simplicidad
                    // Marten manejará la concurrencia automáticamente
                    retrySession.Events.Append(accountId, debit2);
                    await retrySession.SaveChangesAsync();

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("   ✅ Usuario 2: Retry exitoso!");
                    Console.ResetColor();

                    // Mostrar el estado final
                    var finalAccount = await retrySession.LoadAsync<Account>(accountId);
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"   💰 Balance final: {finalAccount?.Balance:C}");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("   ❌ Usuario 2: Fondos insuficientes después de recargar datos.");
                    Console.ResetColor();
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Error inesperado: {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("===== 🔒 FIN CONCURRENCY DEMO =====\n");
        Console.ResetColor();
    }
}