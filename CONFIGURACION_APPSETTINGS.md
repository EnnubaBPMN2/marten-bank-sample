# ?? Configuración con appsettings.json

## ? Cambios Implementados

Se ha refactorizado el código para usar `appsettings.json` en lugar de hardcodear el connection string.

---

## ?? Archivos Creados

### 1. `appsettings.json` (Base)

```json
{
  "ConnectionStrings": {
    "Marten": "host=localhost;database=marten_bank;password=P@ssw0rd!;username=marten_user"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Marten": "Information"
    }
  },
  "MartenSettings": {
    "UseAsyncDaemon": false,
    "AutoCreateSchemaObjects": true
  }
}
```

**Propósito:** Configuración base que se usa en todos los ambientes.

---

### 2. `appsettings.Development.json` (Desarrollo)

```json
{
  "ConnectionStrings": {
    "Marten": "host=localhost;database=marten_bank;password=P@ssw0rd!;username=marten_user"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Information",
      "Marten": "Debug"
    }
  },
  "MartenSettings": {
    "UseAsyncDaemon": false,
    "AutoCreateSchemaObjects": true
  }
}
```

**Propósito:** Configuración específica para desarrollo (más logs).

---

## ?? Paquetes NuGet Agregados

```xml
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="9.0.10" />
<PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="9.0.10" />
<PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="9.0.10" />
```

**Total de paquetes:** 3 nuevos

---

## ?? Cambios en `Program.cs`

### Antes (Hardcoded)
```csharp
private static async Task CleanDatabaseAsync()
{
    var connectionString = "host=localhost;database=marten_bank;password=P@ssw0rd!;username=marten_user";
    // ...
}

var store = DocumentStore.For(_ =>
{
    _.Connection("host=localhost;database=marten_bank;password=P@ssw0rd!;username=marten_user");
    // ...
});
```

**Problemas:**
- ? Credenciales en el código fuente
- ? Difícil cambiar por ambiente
- ? Requiere recompilación para cambios

---

### Después (Configuración)
```csharp
// Cargar configuración desde appsettings.json
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

// Obtener connection string desde configuración
var connectionString = configuration.GetConnectionString("Marten")
    ?? throw new InvalidOperationException("Connection string 'Marten' not found in configuration");

// Obtener settings de Marten
var useAsyncDaemon = configuration.GetValue<bool>("MartenSettings:UseAsyncDaemon");
```

**Beneficios:**
- ? Credenciales separadas del código
- ? Fácil cambiar por ambiente
- ? Sin recompilación necesaria
- ? Soporte para variables de entorno

---

## ?? Configuración por Ambiente

### Desarrollo (Local)
```bash
# No hacer nada, usa appsettings.Development.json automáticamente
dotnet run
```

### Staging
```bash
# Opción 1: Variable de entorno
$env:ASPNETCORE_ENVIRONMENT="Staging"
dotnet run

# Opción 2: Crear appsettings.Staging.json
# con connection string diferente
```

### Producción
```bash
# Opción 1: Variable de entorno
$env:ASPNETCORE_ENVIRONMENT="Production"
$env:ConnectionStrings__Marten="host=prod-server;database=marten_bank;..."
dotnet run

# Opción 2: appsettings.Production.json (recomendado)
```

---

## ?? Ejemplo: `appsettings.Production.json`

```json
{
  "ConnectionStrings": {
    "Marten": "host=prod-db.example.com;database=marten_bank_prod;password=SecureP@ss!;username=marten_prod_user;SSL Mode=Require"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft": "Error",
      "Marten": "Warning"
    }
  },
  "MartenSettings": {
    "UseAsyncDaemon": true,
    "AutoCreateSchemaObjects": false
  }
}
```

**Diferencias con Development:**
- Connection string a servidor de producción
- Menos logging (Warning en vez de Debug)
- Daemon habilitado por defecto
- **No** auto-crear objetos (seguridad)

---

## ?? Seguridad

### ?? NO Commitear Credenciales

Agrega a `.gitignore`:
```gitignore
# Configuración local
appsettings.Development.json
appsettings.Local.json

# Secretos de producción
appsettings.Production.json
appsettings.Staging.json

# User secrets
secrets.json
```

### ? Usar User Secrets (Desarrollo)

```bash
# Inicializar user secrets
dotnet user-secrets init

# Agregar connection string
dotnet user-secrets set "ConnectionStrings:Marten" "host=localhost;database=marten_bank;password=P@ssw0rd!;username=marten_user"

# Listar secrets
dotnet user-secrets list
```

**Modificar `Program.cs`:**
```csharp
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .AddUserSecrets<Program>()  // ? Agregar esta línea
    .AddEnvironmentVariables()
    .Build();
```

---

## ?? Variables de Entorno

### Windows (PowerShell)
```powershell
# Temporal (sesión actual)
$env:ConnectionStrings__Marten="host=localhost;..."
$env:MartenSettings__UseAsyncDaemon="true"

# Permanente (usuario)
[Environment]::SetEnvironmentVariable("ConnectionStrings__Marten", "host=localhost;...", "User")
```

### Linux/Mac (Bash)
```bash
# Temporal
export ConnectionStrings__Marten="host=localhost;..."
export MartenSettings__UseAsyncDaemon="true"

# Permanente (~/.bashrc)
echo 'export ConnectionStrings__Marten="host=localhost;..."' >> ~/.bashrc
```

### Docker
```yaml
# docker-compose.yml
version: '3.8'
services:
  app:
    image: marten-bank-sample:latest
    environment:
      - ConnectionStrings__Marten=host=postgres;database=marten_bank;...
      - MartenSettings__UseAsyncDaemon=true
```

**Nota:** Usa `__` (doble guión bajo) para jerarquía en variables de entorno.

---

## ?? Comparación

| Aspecto | Antes (Hardcoded) | Después (Config) |
|---------|-------------------|------------------|
| **Seguridad** | ? Credenciales en código | ? Separadas |
| **Ambientes** | ? Difícil | ? Fácil (múltiples files) |
| **Cambios** | ? Requiere recompilación | ? Solo editar JSON |
| **CI/CD** | ? Complicado | ? Variables de entorno |
| **Secretos** | ? Expuestos en Git | ? Gitignore |
| **Mantenibilidad** | ? Baja | ? Alta |

---

## ?? Probar los Cambios

### 1. Verificar que appsettings.json existe
```bash
ls appsettings*.json
# Debe mostrar:
# appsettings.json
# appsettings.Development.json
```

### 2. Ejecutar la aplicación
```bash
dotnet run
```

### 3. Verificar que usa la configuración
La aplicación debe:
- ? Conectarse a PostgreSQL correctamente
- ? Mostrar el mensaje de limpieza de BD
- ? Ejecutarse sin errores

### 4. Cambiar configuración sin recompilar
```bash
# Editar appsettings.json
# Cambiar: "UseAsyncDaemon": false ? "UseAsyncDaemon": true

# Ejecutar de nuevo (sin dotnet build)
dotnet run
```

Deberías ver:
```
? Async daemon HABILITADO - Las proyecciones se procesarán automáticamente
```

---

## ?? Para Producción

### Opción 1: Azure App Service
```bash
# Configurar en Azure Portal
# Settings > Configuration > Application settings
# Key: ConnectionStrings__Marten
# Value: host=prod-db;database=...
```

### Opción 2: Kubernetes Secret
```yaml
apiVersion: v1
kind: Secret
metadata:
  name: marten-secrets
type: Opaque
stringData:
  connection-string: "host=prod-db;database=marten_bank;..."
---
apiVersion: v1
kind: Pod
metadata:
  name: marten-app
spec:
  containers:
  - name: app
    image: marten-bank-sample:latest
    env:
    - name: ConnectionStrings__Marten
      valueFrom:
        secretKeyRef:
          name: marten-secrets
          key: connection-string
```

### Opción 3: AWS Secrets Manager
```csharp
// Requiere: AWSSDK.SecretsManager
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddSecretsManager(region: RegionEndpoint.USEast1)
    .Build();
```

---

## ?? Recursos Adicionales

- [Configuration in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/)
- [Safe storage of app secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
- [Environment variables](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/#environment-variables)

---

## ? Checklist de Migración

- [x] Crear `appsettings.json`
- [x] Crear `appsettings.Development.json`
- [x] Instalar paquetes NuGet
- [x] Refactorizar `Program.cs`
- [x] Configurar copia de archivos en `.csproj`
- [x] Verificar compilación
- [x] Agregar a `.gitignore` (siguiente paso)
- [ ] Configurar User Secrets (opcional)
- [ ] Crear `appsettings.Production.json` (cuando sea necesario)

---

**Fecha:** 2025-11-03  
**Versión:** 2.1 (Configuración externalizada)
