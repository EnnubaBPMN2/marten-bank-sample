# ?? Mejoras Implementadas - Marten Bank Sample

Este documento describe las mejoras implementadas para hacer la demo más visual y funcional.

---

## ?? Mejora 1: Limpieza Automática de Base de Datos

### ¿Qué hace?

Antes de cada ejecución, el programa limpia automáticamente todas las tablas de Marten para comenzar con datos frescos.

### Código

```csharp
private static async Task CleanDatabaseAsync()
{
    var connectionString = "host=localhost;database=marten_bank;password=P@ssw0rd!;username=marten_user";
    
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
```

### Resultado

**Antes:**
```
Month: 2025-11
Accounts Created: 12  ? Datos acumulados de múltiples ejecuciones
Accounts Closed: 6
Total Transactions: 21
```

**Después:**
```
?? Limpiando base de datos...
? Base de datos limpiada.

Month: 2025-11
Accounts Created: 2   ? Datos limpios de esta ejecución
Accounts Closed: 1
Total Transactions: 3
```

---

## ?? Mejora 2: Emojis y Colores Mejorados

### ¿Qué hace?

Agrega emojis significativos a toda la salida para hacer la demo más visual y fácil de seguir.

### Emojis Usados

| Emoji | Significado | Contexto |
|-------|-------------|----------|
| ?? | Limpieza | Limpiando base de datos |
| ? | Éxito | Operaciones exitosas |
| ? | Error/Fallo | Operaciones fallidas |
| ?? | Advertencia | Daemon deshabilitado, fondos insuficientes |
| ?? | Dinero/Balance | Mostrar balances |
| ?? | Débito | Retiros o transferencias salientes |
| ?? | Transacción | Operaciones generales |
| ?? | Usuario | Operaciones de usuarios en concurrency demo |
| ?? | Concurrencia | Demo de concurrency |
| ? | Time Travel | Demo de consultas históricas |
| ?? | Reportes | Proyecciones y resúmenes |
| ?? | Ledger | Historial de transacciones |
| ?? | Retry | Reintento después de conflicto |
| ?? | Versión | Número de versión del stream |
| ?? | Completado | Demo terminada |

### Ejemplo de Salida

**Program.cs:**
```
?? Limpiando base de datos...
? Base de datos limpiada.

??  Async daemon está deshabilitado en esta demo.

?? Bill está retirando todo su balance de $100.00 antes de cerrar...

?? Transaction ledger for Khalid Abuhakmeh

?? Monthly Transaction Summary (Async Projection)
?? Month: 2025-11
?? Accounts Created: 2
?? Accounts Closed: 1
?? Total Transactions: 3
?? Total Debited: $200.00
?? Total Credited: $100.00

?? Demo completado. Presiona Enter para salir...
```

**ConcurrencyExample.cs:**
```
===== ?? CONCURRENCY DEMO =====

?? Usuario 1 - Versión del stream: 2
   Balance actual: $900.00

?? Usuario 2 - Versión del stream: 2
   Balance actual: $900.00

?? Usuario 1 intenta hacer un depósito de $50...
   ? Usuario 1: Transacción exitosa!
   ?? Nueva versión del stream: 3

?? Usuario 2 intenta hacer un retiro de $25 (usando versión obsoleta)...
   ? Usuario 2: CONFLICTO DE CONCURRENCIA!
   ?? Esperaba versión 2
   ?? Pero la versión actual es 3
   ?? El stream fue modificado por otro usuario.

???  Estrategia de resolución:
   1??  Recargar el stream con la versión actual
   2??  Re-evaluar la lógica de negocio
   3??  Reintentar la operación

?? Usuario 2: Reintentando con datos actualizados...
   ?? Nueva versión: 3
   ?? Nuevo balance: $950.00
   ? Usuario 2: Retry exitoso!
   ?? Balance final: $925.00
```

**TimeTravelExample.cs:**
```
===== ? TIME TRAVEL DEMO =====

?? Total de eventos en el stream: 4

--- ?? Estado por Versión ---

?? Versión 1 @ 2025-11-03 23:59:57
   ?? Evento: Accounting.Events.AccountCreated
   ?? Owner: Khalid Abuhakmeh
   ?? Balance: $1,000.00
   ?? IsClosed: ? Activa

?? Versión 2 @ 2025-11-03 23:59:57
   ?? Evento: Accounting.Events.AccountDebited
   ?? Owner: Khalid Abuhakmeh
   ?? Balance: $900.00
   ?? IsClosed: ? Activa

--- ?? Balance Máximo Histórico ---
?? Balance máximo: $1,000.00
?? Alcanzado en versión: 1
?? Fecha: 2025-11-03 23:59:57
```

---

## ?? Mejora 3: Modo Async Daemon Habilitado

### ¿Cómo activarlo?

Cambia la línea 16 de `Program.cs`:

```csharp
// Cambiar de:
var useAsyncDaemon = false;

// A:
var useAsyncDaemon = true;
```

### ¿Qué cambia?

**Modo Deshabilitado (false - default):**
```
??  Async daemon está deshabilitado en esta demo.
   Las proyecciones se procesarán manualmente usando RebuildProjectionAsync().

----- ?? Procesando Proyección Asíncrona (Rebuild Manual) ------
? Proyección reconstruida exitosamente.
```

**Modo Habilitado (true):**
```
? Async daemon HABILITADO - Las proyecciones se procesarán automáticamente

[... operaciones bancarias ...]

----- ? Esperando a que el daemon procese las proyecciones... ------
? Daemon ha procesado los eventos en background.
```

### Flujo con Daemon Habilitado

```
???????????????????????????????????????????????????????????????
? 1. Programa inicia                                          ?
?    ??> BuildProjectionDaemonAsync()                         ?
?    ??> daemon.StartAllAsync() ?                            ?
???????????????????????????????????????????????????????????????
                         ?
                         ?
???????????????????????????????????????????????????????????????
? 2. Operaciones bancarias (crear cuentas, transferencias)   ?
?    ??> Eventos escritos en mt_events                       ?
???????????????????????????????????????????????????????????????
                         ?
                         ?
???????????????????????????????????????????????????????????????
? 3. Daemon detecta nuevos eventos (polling cada 2s)         ?
?    ??> Procesa MonthlyTransactionProjection                ?
?    ??> Actualiza mt_doc_monthlytransactionsummary          ?
???????????????????????????????????????????????????????????????
                         ?
                         ?
???????????????????????????????????????????????????????????????
? 4. Programa espera 2 segundos (Task.Delay)                 ?
?    ??> Permite al daemon procesar eventos                  ?
???????????????????????????????????????????????????????????????
                         ?
                         ?
???????????????????????????????????????????????????????????????
? 5. Consultar proyección (ya actualizada por daemon)        ?
???????????????????????????????????????????????????????????????
                         ?
                         ?
???????????????????????????????????????????????????????????????
? 6. Programa termina                                         ?
?    ??> daemon.StopAllAsync()                                ?
?    ??> daemon.Dispose()                                     ?
???????????????????????????????????????????????????????????????
```

### Comparación: Manual vs. Daemon

| Aspecto | Manual (false) | Daemon (true) |
|---------|----------------|---------------|
| **Procesamiento** | Explícito con `RebuildProjectionAsync()` | Automático en background |
| **Tiempo** | Instantáneo (procesa todo de golpe) | ~2 segundos (polling + procesamiento) |
| **Uso de CPU** | Pico alto al rebuild | Uso distribuido |
| **Realismo** | No realista para producción | Realista para producción |
| **Complejidad** | Simple para demos | Más complejo |
| **Recommended for** | Demos, testing | Producción, staging |

---

## ?? Resumen de Archivos Modificados

1. **Program.cs**
   - ? Agregado `CleanDatabaseAsync()` al inicio
   - ? Agregados emojis en toda la salida
   - ? Mejorada la lógica del daemon mode
   - ? Agregado `using Npgsql;`

2. **ConcurrencyExample.cs**
   - ? Agregados emojis descriptivos
   - ? Mejorado formato de salida
   - ? Colores más claros y consistentes

3. **TimeTravelExample.cs**
   - ? Agregados emojis descriptivos
   - ? Mejorado formato de salida
   - ? Estado "Activa/Cerrada" más visual

4. **marten-bank-sample.csproj**
   - ? Agregado paquete `Npgsql` (9.0.4)

---

## ?? Cómo Probar

### 1. Ejecutar en Modo Default (Daemon Deshabilitado)

```bash
dotnet run
```

**Resultado esperado:**
- ?? BD limpiada
- ?? Mensaje sobre daemon deshabilitado
- ?? Rebuild manual de proyección
- ? Todos los demos funcionan correctamente

### 2. Ejecutar en Modo Daemon Habilitado

**Paso 1:** Editar `Program.cs` línea 16:
```csharp
var useAsyncDaemon = true;
```

**Paso 2:** Ejecutar:
```bash
dotnet run
```

**Resultado esperado:**
- ?? BD limpiada
- ? Daemon iniciado
- ? Espera de 2 segundos para procesamiento
- ? Proyección actualizada automáticamente
- ? Daemon detenido al finalizar

### 3. Verificar en PostgreSQL

```sql
-- Ver eventos limpios (solo de esta ejecución)
SELECT COUNT(*) FROM mt_events;  -- Debe ser ~7

-- Ver proyección mensual limpia
SELECT * FROM mt_doc_monthlytransactionsummary;
-- Debe mostrar solo los datos de esta ejecución:
-- Accounts Created: 2
-- Accounts Closed: 1
-- Total Transactions: 3
```

---

## ?? Troubleshooting

### Problema: Error al limpiar la BD

**Error:**
```
? Error al limpiar la base de datos: relation "mt_events" does not exist
```

**Solución:**
La primera vez que ejecutas el programa, las tablas no existen. Simplemente ejecuta de nuevo y funcionará.

### Problema: Daemon no procesa proyecciones

**Síntomas:**
```
Month: 2025-11
Accounts Created: 0  ? Debería ser 2
Total Transactions: 0
```

**Solución:**
1. Verifica que `useAsyncDaemon = true`
2. Aumenta el delay: `await Task.Delay(5000);` (5 segundos)
3. Verifica logs de Marten en consola

### Problema: Emojis no se ven correctamente

**En Windows (Command Prompt):**
```
? Async daemon está deshabilitado  ? Emoji roto
```

**Solución:**
Usa Windows Terminal o PowerShell 7+. O ejecuta en Visual Studio Code integrado terminal.

---

## ?? Próximos Pasos

1. **Agregar más eventos de negocio:**
   - `AccountSuspended` (cuenta suspendida por fraude)
   - `InterestAccrued` (intereses acumulados)
   - `FeeCharged` (cargos por mantenimiento)

2. **Más proyecciones:**
   - `DailyBalanceSnapshot` (balance diario para gráficos)
   - `FraudAlert` (detección de patrones sospechosos)
   - `CustomerStatement` (estado de cuenta mensual)

3. **Agregar Web API:**
   Convertir esto en una API REST con endpoints como:
   ```
   POST /accounts          - Crear cuenta
   POST /accounts/{id}/deposit
   POST /accounts/{id}/withdraw
   GET  /accounts/{id}
   GET  /accounts/{id}/history
   GET  /reports/monthly
   ```

4. **Agregar Unit Tests:**
   ```csharp
   [Fact]
   public async Task AccountCreated_ShouldHaveCorrectInitialBalance()
   {
       // Given
       var created = new AccountCreated { StartingBalance = 1000m };
       
       // When
       var account = new Account();
       account.Apply(created);
       
       // Then
       account.Balance.Should().Be(1000m);
   }
   ```

---

## ?? Referencias

- [Marten Documentation](https://martendb.io/)
- [Async Daemon Production Guide](./ASYNC_DAEMON_PRODUCTION.md)
- [Event Sourcing Pattern](https://martinfowler.com/eaaDev/EventSourcing.html)
- [CQRS Pattern](https://martinfowler.com/bliki/CQRS.html)

---

**Generado:** 2025-11-03  
**Versión:** 2.0 (con mejoras visuales)
