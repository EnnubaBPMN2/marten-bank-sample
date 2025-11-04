# ?? Mensaje de Commit - Configuración Externalizada

## Comando de Git

```bash
git add .
git commit -m "refactor: externalize configuration to appsettings.json

- Move hardcoded connection string to appsettings.json
- Add support for environment-specific configuration files
- Install Microsoft.Extensions.Configuration packages
- Update .gitignore to exclude sensitive configuration files
- Add comprehensive configuration documentation

Breaking Changes:
- Connection string now must be in appsettings.json
- Environment variable support added (ConnectionStrings__Marten)

New Files:
- appsettings.json (base configuration)
- appsettings.Development.json (dev-specific settings)
- CONFIGURACION_APPSETTINGS.md (configuration guide)

Updated Files:
- Program.cs (refactored to use IConfiguration)
- marten-bank-sample.csproj (added config packages + file copy)
- .gitignore (added rules for sensitive files)

Benefits:
- ? Credentials separated from source code
- ? Easy configuration per environment
- ? No recompilation needed for config changes
- ? Production-ready security best practices
- ? Support for User Secrets and Environment Variables

Packages Added:
- Microsoft.Extensions.Configuration.Json (9.0.10)
- Microsoft.Extensions.Configuration.Binder (9.0.10)
- Microsoft.Extensions.Configuration.EnvironmentVariables (9.0.10)"
```

---

## Alternativa: Commit Corto

```bash
git add .
git commit -m "refactor: externalize config to appsettings.json

- Move connection string from code to appsettings.json
- Add environment-specific config support
- Install Microsoft.Extensions.Configuration packages
- Update .gitignore for sensitive files
- Add CONFIGURACION_APPSETTINGS.md documentation"
```

---

## Alternativa: Commit Minimalista

```bash
git add .
git commit -m "refactor: move config to appsettings.json + docs"
```

---

## ?? Archivos Incluidos en este Commit

### Nuevos (3)
- `appsettings.json`
- `appsettings.Development.json`
- `CONFIGURACION_APPSETTINGS.md`

### Modificados (3)
- `Program.cs`
- `marten-bank-sample.csproj`
- `.gitignore`

---

## ? Verificación Antes de Commitear

```bash
# 1. Ver status
git status

# 2. Ver diferencias
git diff Program.cs
git diff .gitignore

# 3. Verificar que compila
dotnet build

# 4. Verificar que ejecuta
dotnet run

# 5. Verificar archivos a commitear
git status --short
# Debe mostrar:
# M .gitignore
# M Program.cs
# M marten-bank-sample.csproj
# A appsettings.json
# A appsettings.Development.json
# A CONFIGURACION_APPSETTINGS.md
```

---

## ?? Importante: Antes de Push

### Verificar que NO estás commiteando secretos:

```bash
# Buscar passwords en archivos staged
git diff --cached | grep -i "password"

# Verificar contenido de appsettings.json
cat appsettings.json

# Si encuentras credenciales reales:
# 1. git reset HEAD appsettings.json
# 2. Editar y poner credenciales de ejemplo
# 3. git add appsettings.json
```

---

## ?? Comandos Completos

```bash
# 1. Verificar cambios
git status
dotnet build
dotnet run

# 2. Agregar archivos
git add .

# 3. Commit (usa una de las opciones de arriba)
git commit -m "refactor: externalize configuration to appsettings.json

- Move hardcoded connection string to appsettings.json
- Add support for environment-specific configuration files
- Install Microsoft.Extensions.Configuration packages
- Update .gitignore to exclude sensitive configuration files
- Add comprehensive configuration documentation"

# 4. Verificar el commit
git show --stat

# 5. Push al remoto
git push origin main
```

---

## ?? Resumen del Cambio

| Aspecto | Antes | Después |
|---------|-------|---------|
| **Connection String** | Hardcoded en código | appsettings.json |
| **Seguridad** | ? Expuesto en Git | ? Configurable |
| **Ambientes** | ? 1 solo | ? Dev/Staging/Prod |
| **Cambios** | ? Recompilación | ? Solo editar JSON |
| **Paquetes** | 4 NuGet | 7 NuGet (+3) |
| **Documentación** | 9 archivos | 10 archivos (+1) |

---

**Fecha:** 2025-11-03  
**Versión:** 2.1 (Configuración externalizada)  
**Tipo:** Refactoring
