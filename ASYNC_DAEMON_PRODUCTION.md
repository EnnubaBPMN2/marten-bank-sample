# Async Daemon en Producción - Guía Completa

## ¿Qué es el Async Daemon?

El **Async Daemon** es un proceso de background que monitorea continuamente el event store (`mt_events`) y procesa las proyecciones asíncronas automáticamente cuando detecta nuevos eventos.

## ¿Por qué está deshabilitado en tu demo?

```
Warning: The async daemon is disabled.
Projections Accounting.Projections.MonthlyTransactionProjection will not be executed without the async daemon enabled
```

**Razón:** Tu aplicación es una **consola que termina inmediatamente** después de ejecutar las operaciones. El daemon necesita un proceso de larga duración (long-running) para funcionar.

**Solución actual (demo):** Usas `RebuildProjectionAsync()` para procesar manualmente las proyecciones antes de salir.

---

## Comparación: Con y Sin Daemon

| Aspecto | Sin Daemon (Demo) | Con Daemon (Producción) |
|---------|-------------------|-------------------------|
| **Procesamiento** | Manual (`RebuildProjectionAsync`) | Automático en background |
| **Latencia** | Procesa cuando llamas rebuild | Procesa segundos después del evento |
| **Complejidad** | Simple para demos | Requiere infraestructura |
| **Escalabilidad** | No escalable | Escalable (HotCold mode) |
| **Uso de CPU** | Solo al rebuild | Polling continuo (configurable) |

---

## Estrategias para Producción

### Opción 1: Web API con Daemon Integrado ? (Recomendado)

Si tu aplicación es un **ASP.NET Core Web API** o servicio web:

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Configurar Marten con el daemon habilitado
builder.Services.AddMarten(options =>
{
    options.Connection(builder.Configuration.GetConnectionString("Marten"));
    
    options.Events.AddEventTypes(new[] {
        typeof(AccountCreated),
        typeof(AccountCredited),
        typeof(AccountDebited),
        typeof(AccountClosed)
    });

    options.Projections.Snapshot<Account>(SnapshotLifecycle.Inline);
    options.Projections.Add<MonthlyTransactionProjection>(ProjectionLifecycle.Async);
})
// ? ESTO HABILITA EL DAEMON
.AddAsyncDaemon(DaemonMode.HotCold);

var app = builder.Build();

// Tu API corre indefinidamente
app.MapGet("/accounts/{id}", async (Guid id, IDocumentSession session) =>
{
    return await session.LoadAsync<Account>(id);
});

app.Run();
```

**Ventajas:**
- ? Daemon corre en el mismo proceso que la API
- ? No requiere infraestructura adicional
- ? Monitoreo integrado

**Desventajas:**
- ?? Usa CPU/memoria de tu API
- ?? Si escalas horizontalmente (múltiples instancias), necesitas configurar líder (HotCold)

---

### Opción 2: Worker Service Dedicado ?? (Mejor para producción)

Crear un servicio separado que SOLO procese proyecciones:

**Proyecto nuevo:** `Accounting.ProjectionWorker`

```csharp
// Program.cs (Worker Service)
using Microsoft.Extensions.Hosting;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddMarten(options =>
        {
            options.Connection(context.Configuration.GetConnectionString("Marten"));
            
            options.Events.AddEventTypes(new[] {
                typeof(AccountCreated),
                typeof(AccountCredited),
                typeof(AccountDebited),
                typeof(AccountClosed)
            });

            options.Projections.Add<MonthlyTransactionProjection>(ProjectionLifecycle.Async);
        })
        .AddAsyncDaemon(DaemonMode.Solo); // Solo: es el único daemon

        services.AddHostedService<ProjectionDaemonWorker>();
    });

await builder.Build().RunAsync();
```

```csharp
// ProjectionDaemonWorker.cs
public class ProjectionDaemonWorker : BackgroundService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<ProjectionDaemonWorker> _logger;
    private IProjectionDaemon? _daemon;

    public ProjectionDaemonWorker(
        IDocumentStore store,
        ILogger<ProjectionDaemonWorker> logger)
    {
        _store = store;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting Marten Projection Daemon...");

        _daemon = await _store.BuildProjectionDaemonAsync();
        
        // Iniciar todas las proyecciones
        await _daemon.StartAllAsync();
        
        _logger.LogInformation("Projection Daemon started successfully.");

        // Mantener el daemon corriendo hasta que se cancele
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Projection Daemon shutdown requested.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Projection Daemon...");
        
        if (_daemon != null)
        {
            await _daemon.StopAllAsync();
            _daemon.Dispose();
        }
        
        await base.StopAsync(cancellationToken);
    }
}
```

**Deploy en Kubernetes:**

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: projection-worker
spec:
  replicas: 1  # Solo 1 instancia (o usar HotCold para múltiples)
  selector:
    matchLabels:
      app: projection-worker
  template:
    metadata:
      labels:
        app: projection-worker
    spec:
      containers:
      - name: worker
        image: your-registry/projection-worker:latest
        env:
        - name: ConnectionStrings__Marten
          value: "host=postgres;database=marten_bank;..."
        resources:
          requests:
            memory: "256Mi"
            cpu: "250m"
          limits:
            memory: "512Mi"
            cpu: "500m"
```

**Ventajas:**
- ? Separación de responsabilidades (API vs. proyecciones)
- ? Escalado independiente
- ? Reinicio del worker no afecta la API
- ? Monitoreo dedicado

**Desventajas:**
- ?? Requiere infraestructura adicional (1 servicio más)

---

### Opción 3: Scheduled Job (Menos recomendado)

Si las proyecciones no necesitan estar actualizadas en tiempo real (ej: reportes diarios):

```csharp
// Job que corre cada hora
public class RebuildProjectionsJob
{
    private readonly IDocumentStore _store;

    public async Task Execute()
    {
        using var daemon = await _store.BuildProjectionDaemonAsync();
        await daemon.RebuildProjectionAsync<MonthlyTransactionProjection>(CancellationToken.None);
    }
}
```

**Scheduler: Hangfire/Quartz**

```csharp
RecurringJob.AddOrUpdate<RebuildProjectionsJob>(
    "rebuild-projections",
    job => job.Execute(),
    Cron.Hourly
);
```

**Ventajas:**
- ? Simple de implementar
- ? No requiere daemon corriendo 24/7

**Desventajas:**
- ?? **Datos desactualizados** (eventual consistency extremo)
- ?? Rebuild completo consume CPU/IO
- ?? No recomendado para proyecciones críticas

---

## DaemonMode: Solo vs. HotCold

### DaemonMode.Solo
```csharp
.AddAsyncDaemon(DaemonMode.Solo)
```

**Uso:** Solo 1 instancia de tu aplicación corre el daemon.

**Ventajas:**
- Más simple
- No hay coordinación entre instancias

**Desventajas:**
- Si la instancia muere, no hay backup
- No escalable horizontalmente

---

### DaemonMode.HotCold ? (Recomendado)
```csharp
.AddAsyncDaemon(DaemonMode.HotCold)
```

**Uso:** Múltiples instancias, pero solo 1 (líder) procesa las proyecciones. Si el líder muere, otra instancia toma el control.

**Cómo funciona:**
1. Todas las instancias intentan adquirir un "lease" (lock) en PostgreSQL
2. Solo 1 instancia obtiene el lease y procesa proyecciones
3. Las demás esperan y monitorean si el líder muere
4. Si el líder no renueva el lease, otra instancia lo toma

**Ventajas:**
- ? Alta disponibilidad
- ? Escalado horizontal (múltiples réplicas de tu API)
- ? Failover automático

**Desventajas:**
- ?? Más complejo (requiere coordinación)
- ?? Usa tablas adicionales en PostgreSQL para leases

**Tabla de leases:**
```sql
-- Marten crea esta tabla automáticamente
SELECT * FROM mt_event_progression;
```

---

## Monitoreo del Daemon

### Health Checks

```csharp
// Program.cs
builder.Services.AddHealthChecks()
    .AddCheck<ProjectionHealthCheck>("projections");

// ProjectionHealthCheck.cs
public class ProjectionHealthCheck : IHealthCheck
{
    private readonly IDocumentStore _store;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Verificar si hay proyecciones con error o atrasadas
            using var session = _store.LightweightSession();
            var stats = await session.Query<EventProgressionStats>()
                .Where(x => x.LastException != null)
                .ToListAsync(cancellationToken);

            if (stats.Any())
            {
                return HealthCheckResult.Degraded(
                    $"Hay {stats.Count} proyecciones con errores"
                );
            }

            return HealthCheckResult.Healthy("Todas las proyecciones están al día");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Error al verificar proyecciones", ex);
        }
    }
}
```

### Logging

```csharp
// appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Marten": "Debug",  // ? Ver logs del daemon
      "Marten.Events.Daemon": "Information"
    }
  }
}
```

**Logs típicos:**
```
[12:00:01] Marten.Events.Daemon: Projection 'MonthlyTransactionProjection' processing batch of 50 events
[12:00:01] Marten.Events.Daemon: Projection 'MonthlyTransactionProjection' processed up to sequence 150
```

### Métricas (Prometheus)

```csharp
// Exponer métricas del daemon
app.MapGet("/metrics/projections", async (IDocumentStore store) =>
{
    using var session = store.LightweightSession();
    var stats = await session.Query<EventProgressionStats>().ToListAsync();

    return stats.Select(s => new
    {
        s.ShardName,
        s.LastSequence,
        EventsProcessed = s.EventsProcessed,
        LastUpdated = s.LastEncountered
    });
});
```

---

## Configuración Recomendada para Producción

```csharp
builder.Services.AddMarten(options =>
{
    options.Connection(connectionString);

    // Configuración del daemon
    options.Projections.Add<MonthlyTransactionProjection>(ProjectionLifecycle.Async);
    
    // Configurar workers/threads
    options.Projections.AsyncMode = DaemonMode.HotCold;
    
    // Configurar batch size y delays
    options.Events.Daemon.BatchSize = 100;  // Procesar 100 eventos por lote
    options.Events.Daemon.PollingInterval = TimeSpan.FromSeconds(2);  // Polling cada 2s
})
.AddAsyncDaemon(DaemonMode.HotCold)
.UseLightweightSessions(); // Optimización de performance
```

---

## Troubleshooting

### Problema: "Projection is lagging behind"

**Causa:** El daemon no procesa los eventos lo suficientemente rápido.

**Soluciones:**
1. Aumentar `BatchSize` (procesar más eventos por lote)
2. Reducir `PollingInterval` (polling más frecuente)
3. Optimizar la lógica de la proyección
4. Escalar el worker (más CPU/memoria)

### Problema: "Multiple daemons competing for leadership"

**Causa:** En HotCold, múltiples instancias compiten por el lease.

**Soluciones:**
1. Verificar que las instancias tengan acceso a PostgreSQL
2. Revisar logs para ver qué instancia es el líder
3. Ajustar `LeaseTime` si hay mucho cambio de líder

### Problema: "Projection has unhandled exception"

**Causa:** La lógica de la proyección lanzó una excepción.

**Soluciones:**
1. Revisar logs: `mt_event_progression` tabla contiene errores
2. Corregir el bug en la proyección
3. Re-ejecutar: `await daemon.RebuildProjectionAsync<TProjection>()`

---

## Resumen de Recomendaciones

### Para Demos/Local (tu caso actual)
? Daemon deshabilitado + `RebuildProjectionAsync()` manual
- Simple y funcional para aprendizaje

### Para APIs pequeñas (1-3 instancias)
? Daemon integrado con `AddAsyncDaemon(DaemonMode.HotCold)`
- Costo-efectivo, no requiere infraestructura extra

### Para Producción Enterprise
? Worker Service dedicado + `DaemonMode.Solo`
- Separación de responsabilidades
- Escalado independiente
- Monitoreo dedicado

### Para Reportes No-Críticos
?? Scheduled jobs (Hangfire/Quartz)
- Solo si eventual consistency extremo es aceptable

---

## Ejemplo Completo: Web API + Worker

**Estructura del proyecto:**
```
Solution/
??? Accounting.Domain/          # Eventos, Proyecciones, Modelos
??? Accounting.API/             # Web API (escribe eventos)
??? Accounting.ProjectionWorker/ # Worker Service (procesa proyecciones)
```

**Accounting.API (escribe eventos):**
```csharp
builder.Services.AddMarten(options =>
{
    options.Connection(connectionString);
    
    // Solo proyecciones INLINE (no async)
    options.Projections.Snapshot<Account>(SnapshotLifecycle.Inline);
});
// ?? NO agregar AddAsyncDaemon aquí
```

**Accounting.ProjectionWorker (procesa proyecciones):**
```csharp
builder.Services.AddMarten(options =>
{
    options.Connection(connectionString);
    
    // Solo proyecciones ASYNC
    options.Projections.Add<MonthlyTransactionProjection>(ProjectionLifecycle.Async);
})
.AddAsyncDaemon(DaemonMode.Solo);
```

**Beneficios:**
- API ligera (solo escribe eventos)
- Worker dedicado a procesamiento
- Escalado independiente

---

## Conclusión

**Para tu demo actual:** No es malo que el daemon esté deshabilitado. Usas rebuild manual que funciona perfecto.

**Para producción:** DEBES habilitar el daemon (opción 1 o 2) o las proyecciones nunca se actualizarán automáticamente.

**Cambio mínimo:** En tu `Program.cs`, cambia `bool useAsyncDaemon = false;` a `true` y verás el daemon funcionando en tiempo real.
