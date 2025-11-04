using marten_bank_sample.Models.Projections;
using Marten;

namespace marten_bank_sample;

public class TimeTravelExample
{
    public static async Task DemonstrateTimeTravel(IDocumentStore store, Guid accountId)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("===== ⏰ TIME TRAVEL DEMO =====");
        Console.WriteLine("Consultando el estado de la cuenta en diferentes momentos...\n");
        Console.ResetColor();

        await using var session = store.LightweightSession();

        // 1. Obtener todos los eventos del stream
        var events = await session.Events.FetchStreamAsync(accountId);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"📊 Total de eventos en el stream: {events.Count}\n");
        Console.ResetColor();

        // 2. Reconstruir el estado en cada versión (cada evento)
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("--- 📜 Estado por Versión ---");
        Console.ResetColor();

        for (var version = 1; version <= events.Count; version++)
        {
            // AggregateStream reconstruye el estado hasta una versión específica
            var accountAtVersion = await session.Events
                .AggregateStreamAsync<Account>(accountId, version);

            if (accountAtVersion != null)
            {
                var evt = events[version - 1];
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n🔖 Versión {version} @ {evt.Timestamp:yyyy-MM-dd HH:mm:ss}");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"   📌 Evento: {evt.EventType}");
                Console.WriteLine($"   👤 Owner: {accountAtVersion.Owner}");
                Console.WriteLine($"   💰 Balance: {accountAtVersion.Balance:C}");
                Console.WriteLine($"   🚪 IsClosed: {(accountAtVersion.IsClosed ? "❌ Cerrada" : "✅ Activa")}");
                Console.ResetColor();
            }
        }

        // 3. Time Travel: Estado en un timestamp específico
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("--- 🕐 Estado en un Timestamp Específico ---");
        Console.ResetColor();

        if (events.Count >= 3)
        {
            var targetTimestamp = events[2].Timestamp; // Después del 3er evento
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n🔍 Consultando estado hasta: {targetTimestamp:yyyy-MM-dd HH:mm:ss}");
            Console.ResetColor();

            // Filtrar eventos hasta ese timestamp y reconstruir
            var eventsUntil = events.Where(e => e.Timestamp <= targetTimestamp).ToList();
            var accountAtTime = await session.Events
                .AggregateStreamAsync<Account>(accountId, timestamp: targetTimestamp);

            if (accountAtTime != null)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"   👤 Owner: {accountAtTime.Owner}");
                Console.WriteLine($"   💰 Balance en ese momento: {accountAtTime.Balance:C}");
                Console.WriteLine($"   📊 Eventos procesados: {eventsUntil.Count}");
                Console.ResetColor();
            }
        }

        // 4. Query: Encontrar el balance máximo histórico
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("--- 📈 Balance Máximo Histórico ---");
        Console.ResetColor();

        decimal maxBalance = 0;
        var versionAtMax = 0;
        DateTimeOffset? timeAtMax = null;

        for (var version = 1; version <= events.Count; version++)
        {
            var accountAtVersion = await session.Events
                .AggregateStreamAsync<Account>(accountId, version);

            if (accountAtVersion != null && accountAtVersion.Balance > maxBalance)
            {
                maxBalance = accountAtVersion.Balance;
                versionAtMax = version;
                timeAtMax = events[version - 1].Timestamp;
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"💎 Balance máximo: {maxBalance:C}");
        Console.WriteLine($"📍 Alcanzado en versión: {versionAtMax}");
        Console.WriteLine($"📅 Fecha: {timeAtMax:yyyy-MM-dd HH:mm:ss}");
        Console.ResetColor();

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("===== ⏰ FIN TIME TRAVEL DEMO =====\n");
        Console.ResetColor();
    }
}