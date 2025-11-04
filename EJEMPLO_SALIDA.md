# ?? Ejemplo de Salida Mejorada

Este documento muestra cómo se ve la salida del programa con todas las mejoras implementadas.

---

## ?? Salida Completa del Programa

```
?? Limpiando base de datos...
? Base de datos limpiada.

Warning: The async daemon is disabled.
Projections Accounting.Projections.MonthlyTransactionProjection will not be executed without the async daemon enabled
??  Async daemon está deshabilitado en esta demo.
   Las proyecciones se procesarán manualmente usando RebuildProjectionAsync().
   Para producción, consulta ASYNC_DAEMON_PRODUCTION.md

Account created for Khalid Abuhakmeh with Balance of $1,000.00
Account created for Bill Boga with Balance of $0.00
Debiting Khalid Abuhakmeh ($100.00): Bill helped me out with some code.
Crediting Bill Boga $100.00: Bill helped me out with some code.
Bill Boga has insufficient funds for debit

----- Final Balance ------
Khalid Abuhakmeh (c08233d9-397f-43b3-99bc-fe384e689e21) : $900.00
Bill Boga (17eddb9d-836a-4500-a1eb-75310ebe8610) : $100.00

?? Bill está retirando todo su balance de $100.00 antes de cerrar...
Debiting Bill Boga ($100.00): Withdrawal before account closure
Account closed for Bill Boga. Reason: Customer requested closure - zero balance

----- Final Balance (Updated) ------
Khalid Abuhakmeh (c08233d9-397f-43b3-99bc-fe384e689e21) : $900.00
Bill Boga (17eddb9d-836a-4500-a1eb-75310ebe8610) : $0.00 [CLOSED]

?? Transaction ledger for Khalid Abuhakmeh
11/3/2025 6:59:54 PM -05:00 Created account for Khalid Abuhakmeh with starting balance of $1,000.00
11/3/2025 6:59:57 PM -05:00 Debited $100.00 to 17eddb9d-836a-4500-a1eb-75310ebe8610


?? Transaction ledger for Bill Boga
11/3/2025 6:59:54 PM -05:00 Created account for Bill Boga with starting balance of $0.00
11/3/2025 6:59:57 PM -05:00 Credited $100.00 From 17eddb9d-836a-4500-a1eb-75310ebe8610
11/3/2025 6:59:57 PM -05:00 Attempted Invalid Action: Overdraft
11/3/2025 6:59:57 PM -05:00 Debited $100.00 to 132187de-df34-4bb1-afcf-8cee5bc001df
11/3/2025 6:59:57 PM -05:00 Account closed with final balance of $0.00. Reason: Customer requested closure - zero balance


----- ?? Procesando Proyección Asíncrona (Rebuild Manual) ------
? Proyección reconstruida exitosamente.

----- ?? Monthly Transaction Summary (Async Projection) ------
?? Month: 2025-11
?? Accounts Created: 2
?? Accounts Closed: 1
?? Total Transactions: 3
?? Total Debited: $200.00
?? Total Credited: $100.00
??  Overdraft Attempts: 1
?? Last Updated: 11/3/2025 6:59:58 PM -05:00

===== ?? CONCURRENCY DEMO =====
Simulando dos usuarios modificando la misma cuenta simultáneamente...

?? Usuario 1 - Versión del stream: 2
   Balance actual: $900.00

?? Usuario 2 - Versión del stream: 2
   Balance actual: $900.00

?? Usuario 1 intenta hacer un depósito de $50...
Crediting Khalid Abuhakmeh $50.00: Depósito Usuario 1
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
Debiting Khalid Abuhakmeh ($25.00): Retiro Usuario 2
   ? Usuario 2: Retry exitoso!
   ?? Balance final: $925.00

===== ?? FIN CONCURRENCY DEMO =====


===== ? TIME TRAVEL DEMO =====
Consultando el estado de la cuenta en diferentes momentos...

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

?? Versión 3 @ 2025-11-03 23:59:58
   ?? Evento: Accounting.Events.AccountCredited
   ?? Owner: Khalid Abuhakmeh
   ?? Balance: $950.00
   ?? IsClosed: ? Activa

?? Versión 4 @ 2025-11-03 23:59:58
   ?? Evento: Accounting.Events.AccountDebited
   ?? Owner: Khalid Abuhakmeh
   ?? Balance: $925.00
   ?? IsClosed: ? Activa

--- ?? Estado en un Timestamp Específico ---

?? Consultando estado hasta: 2025-11-03 23:59:58
   ?? Owner: Khalid Abuhakmeh
   ?? Balance en ese momento: $950.00
   ?? Eventos procesados: 3

--- ?? Balance Máximo Histórico ---
?? Balance máximo: $1,000.00
?? Alcanzado en versión: 1
?? Fecha: 2025-11-03 23:59:57

===== ? FIN TIME TRAVEL DEMO =====


?? Demo completado. Presiona Enter para salir...
```

---

## ?? Análisis de la Salida

### 1. Limpieza de Base de Datos
```
?? Limpiando base de datos...
? Base de datos limpiada.
```
**Qué hace:** Trunca todas las tablas de Marten para empezar con datos limpios.

**Beneficio:** Los números en la proyección mensual ahora son exactos (2 cuentas creadas, no 12 acumuladas).

---

### 2. Configuración del Daemon
```
??  Async daemon está deshabilitado en esta demo.
   Las proyecciones se procesarán manualmente usando RebuildProjectionAsync().
   Para producción, consulta ASYNC_DAEMON_PRODUCTION.md
```
**Qué significa:** El daemon asíncrono no está corriendo en background. Las proyecciones se procesarán manualmente.

**Para cambiar:** Edita `Program.cs` línea 16: `var useAsyncDaemon = true;`

---

### 3. Operaciones Bancarias
```
Account created for Khalid Abuhakmeh with Balance of $1,000.00
Account created for Bill Boga with Balance of $0.00
```
**Eventos generados:**
- 2 × `AccountCreated` (seq_id 1, 2)

```
Debiting Khalid Abuhakmeh ($100.00): Bill helped me out with some code.
Crediting Bill Boga $100.00: Bill helped me out with some code.
```
**Eventos generados:**
- 1 × `AccountDebited` en stream de Khalid (seq_id 3)
- 1 × `AccountCredited` en stream de Bill (seq_id 4)

```
Bill Boga has insufficient funds for debit
```
**Evento generado:**
- 1 × `InvalidOperationAttempted` en stream de Bill (seq_id 5)

```
?? Bill está retirando todo su balance de $100.00 antes de cerrar...
Debiting Bill Boga ($100.00): Withdrawal before account closure
Account closed for Bill Boga. Reason: Customer requested closure - zero balance
```
**Eventos generados:**
- 1 × `AccountDebited` (retiro de Bill, seq_id 6)
- 1 × `AccountClosed` (cierre de cuenta, seq_id 7)

---

### 4. Ledgers (Historial Completo)
```
?? Transaction ledger for Khalid Abuhakmeh
11/3/2025 6:59:54 PM -05:00 Created account for Khalid Abuhakmeh with starting balance of $1,000.00
11/3/2025 6:59:57 PM -05:00 Debited $100.00 to 17eddb9d-836a-4500-a1eb-75310ebe8610
```
**Stream de Khalid:** 2 eventos
- Versión 1: AccountCreated
- Versión 2: AccountDebited

```
?? Transaction ledger for Bill Boga
11/3/2025 6:59:54 PM -05:00 Created account for Bill Boga with starting balance of $0.00
11/3/2025 6:59:57 PM -05:00 Credited $100.00 From 17eddb9d-836a-4500-a1eb-75310ebe8610
11/3/2025 6:59:57 PM -05:00 Attempted Invalid Action: Overdraft
11/3/2025 6:59:57 PM -05:00 Debited $100.00 to 132187de-df34-4bb1-afcf-8cee5bc001df
11/3/2025 6:59:57 PM -05:00 Account closed with final balance of $0.00. Reason: Customer requested closure - zero balance
```
**Stream de Bill:** 5 eventos
- Versión 1: AccountCreated
- Versión 2: AccountCredited
- Versión 3: InvalidOperationAttempted
- Versión 4: AccountDebited
- Versión 5: AccountClosed

---

### 5. Proyección Mensual (Con Datos Limpios)
```
----- ?? Monthly Transaction Summary (Async Projection) ------
?? Month: 2025-11
?? Accounts Created: 2        ? Khalid + Bill
?? Accounts Closed: 1         ? Bill
?? Total Transactions: 3      ? Khalid?Bill + Bill intento + Bill retiro
?? Total Debited: $200.00     ? $100 (Khalid?Bill) + $100 (Bill retiro)
?? Total Credited: $100.00    ? $100 (Bill recibe)
??  Overdraft Attempts: 1     ? Bill intenta gastar $1000
?? Last Updated: 11/3/2025 6:59:58 PM -05:00
```

**¿Por qué estos números?**

| Métrica | Cálculo |
|---------|---------|
| Accounts Created | 2 (Khalid + Bill) |
| Accounts Closed | 1 (Bill cerró su cuenta) |
| Total Transactions | 3 (Débito Khalid + Crédito Bill + Débito Bill retiro) |
| Total Debited | $200 ($100 + $100) |
| Total Credited | $100 (Bill recibe de Khalid) |
| Overdraft Attempts | 1 (Bill intenta gastar $1000) |

---

### 6. Demo de Concurrencia (El Más Interesante)
```
===== ?? CONCURRENCY DEMO =====

?? Usuario 1 - Versión del stream: 2
   Balance actual: $900.00

?? Usuario 2 - Versión del stream: 2
   Balance actual: $900.00
```
**Estado inicial:** Ambos usuarios leen la **misma versión** (2) del stream de Khalid.

```
?? Usuario 1 intenta hacer un depósito de $50...
   ? Usuario 1: Transacción exitosa!
   ?? Nueva versión del stream: 3
```
**Evento generado:** `AccountCredited` $50  
**Nuevo balance:** $900 + $50 = $950  
**Nueva versión:** 3

```
?? Usuario 2 intenta hacer un retiro de $25 (usando versión obsoleta)...
   ? Usuario 2: CONFLICTO DE CONCURRENCIA!
   ?? Esperaba versión 2
   ?? Pero la versión actual es 3
   ?? El stream fue modificado por otro usuario.
```
**¿Por qué falla?**  
Usuario 2 intenta agregar un evento especificando que espera versión 2, pero Usuario 1 ya modificó el stream a versión 3.

```
?? Usuario 2: Reintentando con datos actualizados...
   ?? Nueva versión: 3
   ?? Nuevo balance: $950.00
   ? Usuario 2: Retry exitoso!
   ?? Balance final: $925.00
```
**Retry exitoso:**
- Recarga el stream (versión 3, balance $950)
- Valida que hay fondos suficientes ($950 >= $25 ?)
- Agrega el evento sin especificar versión
- **Balance final:** $950 - $25 = $925

---

### 7. Time Travel Demo
```
===== ? TIME TRAVEL DEMO =====
?? Total de eventos en el stream: 4
```
**4 eventos en el stream de Khalid:**
1. AccountCreated ($1,000)
2. AccountDebited ($100 a Bill)
3. AccountCredited ($50 de Usuario 1)
4. AccountDebited ($25 de Usuario 2)

```
?? Versión 1 @ 2025-11-03 23:59:57
   ?? Balance: $1,000.00
```
**Estado inicial:** Cuenta creada con $1,000

```
?? Versión 2 @ 2025-11-03 23:59:57
   ?? Balance: $900.00
```
**Después de transferir $100 a Bill**

```
?? Versión 3 @ 2025-11-03 23:59:58
   ?? Balance: $950.00
```
**Después del depósito de Usuario 1 (+$50)**

```
?? Versión 4 @ 2025-11-03 23:59:58
   ?? Balance: $925.00
```
**Después del retiro de Usuario 2 (-$25)**

```
--- ?? Balance Máximo Histórico ---
?? Balance máximo: $1,000.00
?? Alcanzado en versión: 1
?? Fecha: 2025-11-03 23:59:57
```
**Análisis:** El balance máximo que tuvo Khalid fue $1,000 al momento de crear la cuenta (versión 1).

---

## ?? Diferencia Antes/Después de las Mejoras

### Antes (Sin Mejoras)
```
----- Final Balance ------
Khalid Abuhakmeh (c08233d9-397f-43b3-99bc-fe384e689e21) : $900.00

Transaction ledger for Khalid Abuhakmeh
11/3/2025 6:59:54 PM -05:00 Created account for Khalid Abuhakmeh with starting balance of $1,000.00

Month: 2025-11
Accounts Created: 12     ? Datos acumulados de múltiples ejecuciones
Total Transactions: 21

Usuario 1 - Versión del stream: 2
Usuario 1 - Balance actual: $900.00

Error inesperado: Unexpected starting version number for event stream...
```

### Después (Con Mejoras)
```
?? Limpiando base de datos...
? Base de datos limpiada.

----- Final Balance ------
Khalid Abuhakmeh (c08233d9-397f-43b3-99bc-fe384e689e21) : $900.00

?? Transaction ledger for Khalid Abuhakmeh
11/3/2025 6:59:54 PM -05:00 Created account for Khalid Abuhakmeh with starting balance of $1,000.00

?? Monthly Transaction Summary (Async Projection)
?? Accounts Created: 2      ? Datos limpios de esta ejecución
?? Total Transactions: 3

?? Usuario 1 - Versión del stream: 2
   Balance actual: $900.00

   ? Usuario 1: Transacción exitosa!
   ? Usuario 2: Retry exitoso!
   ?? Balance final: $925.00
```

**Mejoras visibles:**
- ? Datos limpios (no acumulados)
- ? Emojis descriptivos
- ? Colores claros y consistentes
- ? Sin errores en el demo de concurrencia
- ? Salida más profesional y fácil de seguir

---

## ?? Cómo Ejecutar

```bash
# Clonar el repositorio
git clone https://github.com/EnnubaBPMN2/marten-bank-sample
cd marten-bank-sample

# Restaurar paquetes
dotnet restore

# Ejecutar
dotnet run
```

**Resultado esperado:** La salida mostrada arriba con todos los emojis y colores.

---

**Nota:** La primera ejecución puede mostrar un warning sobre tablas no existentes en `CleanDatabaseAsync()`. Esto es normal. La segunda ejecución funcionará perfectamente.
