# Servicio de login de funcionarios (.NET) — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Servicio de autenticación en .NET 10 que cumple al pie de la letra `contratos-backend-auth.md` (login, renovar, logout; JWT de 7 días) y guarda sesiones e historial de ingresos en PostgreSQL.

**Architecture:** Un solo proyecto ASP.NET Core con Minimal APIs, organizado por funcionalidad (sin capas Clean Architecture: el dominio es chico). EF Core sobre PostgreSQL. Cada login crea una fila en `UserSessions` (una por dispositivo) y otra en `LoginRecords`. El JWT lleva `sid` (id de sesión): renovar extiende la sesión y emite token nuevo; logout la revoca.

**Tech Stack:** .NET 10 (SDK 10.0.404), ASP.NET Core Minimal APIs, EF Core 10 + Npgsql, PostgreSQL 17, xUnit, Docker Compose. Librerías externas: ver §0, **pendientes de tu aprobación**.

**Spec:** `docs/contratos-backend-auth.md` (copia del contrato de `vigila-app-funcionarios`, se agrega en Task 1).

## Decisiones ya tomadas contigo

| Tema | Decisión |
|---|---|
| Esquema de sesión | JWT 7 días del contrato + tabla `UserSessions` (logout real, revocación) |
| Rutas y JSON | Idénticos al contrato: `/api/v1/cuadrilla/...`, campo `funcionario`, roles `funcionario`/`cuadrilla`/`superadmin` |
| Base de datos | PostgreSQL |
| Alta de usuarios | Solo seed (sin endpoint de registro) |
| `/reportes*` | Fuera de alcance: viven en FastAPI. Se documenta cómo FastAPI valida el token |
| Rol vecino (`usuario`) | Existe en `Roles`. Login con clave correcta → **403 `sin_permiso`** |
| Email no verificado | Login → **401 `credenciales_invalidas`** |
| Historial de ingresos | Tabla `LoginRecords`: solo **login exitoso**, con fecha, IP y User-Agent. Sin `Users.LastLoginAt` |
| Librerías | Aprobadas: EF Core + Npgsql, JwtBearer, `PasswordHasher` de Identity.Core, xUnit + Mvc.Testing + Testcontainers |
| Administrador | Rol `superadmin`. Entra con el mismo `/cuadrilla/login` y usa rutas `/api/v1/admin/usuarios` |
| Alta por admin | Admin define clave inicial; `IsEmailVerified = true`. Solo puede crear roles `funcionario` y `cuadrilla` |
| Gestión por admin | Crear, listar, cambiar estado, resetear clave, ver historial de ingresos |
| Desactivar | Revoca todas las sesiones al instante. Login → 401 `credenciales_invalidas` (contrato). Renovar → 401 `cuenta_inactiva` |
| Reset de clave | Revoca todas las sesiones del usuario |
| Protección | Un superadmin no puede desactivarse a sí mismo ni a otro superadmin → 403 `sin_permiso` |
| Clave | Mínimo 8, máximo 128 caracteres. Sin otras reglas |
| Primer superadmin | Seed: en Development desde `appsettings.Development.json`; en producción desde variables `Seed__Admin__*`, solo si no existe ningún superadmin |
| Validación | 422 `datos_invalidos` |
| Ejecución | Native |

## Global Constraints

- Target `net10.0`. Nullable habilitado. `TreatWarningsAsErrors` en ambos proyectos.
- Base de rutas: `/api/v1/cuadrilla`. Errores siempre `{"codigo": "...", "mensaje": "..."}`; `mensaje` en español, sin detalles técnicos.
- JSON en `snake_case_lower` (System.Text.Json nativo), igual que el contrato: `access_token`, `token_type`, `expira_en_segundos`.
- `expira_en_segundos = VIGILA_CUADRILLA_SESION_DIAS * 86400` → `604800` con el valor por defecto `7`.
- JWT HS256 con `VIGILA_JWT_SECRETO` (mismo secreto que FastAPI). Claims: `sub`, `rol`, `iat`, `exp`, `jti`, `sid`. `ClockSkew = 0`.
- Nunca loguear tokens ni contraseñas.
- Hora siempre vía `TimeProvider` inyectado (nunca `DateTime.UtcNow` directo) para poder fijarla en tests.
- Conventional Commits en todos los commits (`feat:`, `fix:`, `test:`, `chore:`, `docs:`, `build:`).
- Ninguna librería fuera de las aprobadas en §0.
- Cada task termina con `dotnet test` en verde antes del commit.

## Review Focus

1. **Renovación con respuesta perdida:** la app renueva, el 200 se pierde por timeout y la app reintenta con el token viejo. Debe seguir funcionando (el token viejo vale hasta su `exp`). Test en Task 7: renovar dos veces con el mismo token → ambas 200.
2. **Borde exacto de vencimiento:** `ClockSkew` por defecto de .NET es 5 min; con él, un token de 7 días + 1 s pasaría. Test en Task 7 (caso 3 del contrato) con `ClockSkew = 0`.
3. **Email con mayúsculas/espacios:** `" Ana@Victoria.CL "` debe entrar igual que `ana@victoria.cl`. Test en Task 6.
4. **Token firmado por FastAPI** (mismo secreto) sin claim `sid`: no debe renovarse. Test en Task 7 → 401 `no_autenticado`.
5. **Correo inexistente vs clave incorrecta:** misma respuesta y se ejecuta el hash igual (sin atajo de tiempo). Test en Task 6 compara cuerpos y status.
6. **Admin desactivado con token vigente:** un superadmin desactivado no debe seguir creando usuarios con su token viejo. Las rutas admin validan sesión y estado en cada request. Test en Task 9.

---

## 0. Librerías externas — APROBADAS (opción recomendada en cada caso)

Todo lo demás (rate limiting, OpenAPI no, JSON, `TimeProvider`, health checks, forwarded headers) viene en el framework sin paquetes extra.

### 0.1 Acceso a datos — **recomiendo A**

| Opción | Paquetes | Ventajas | Desventajas |
|---|---|---|---|
| **A. EF Core + Npgsql** | `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design` (solo migraciones) + herramienta `dotnet-ef` | Migraciones versionadas, LINQ tipado, estándar en .NET | Más pesado; SQL generado menos visible |
| B. Dapper + Npgsql | `Dapper`, `Npgsql` | SQL explícito, rápido, liviano | Migraciones a mano (scripts SQL), mapeo manual |
| C. Npgsql puro (ADO.NET) | `Npgsql` | Un solo paquete | Mucho código repetitivo, migraciones a mano |

### 0.2 Validación de JWT — **recomiendo A**

| Opción | Paquetes | Ventajas | Desventajas |
|---|---|---|---|
| **A. JwtBearer de Microsoft** | `Microsoft.AspNetCore.Authentication.JwtBearer` | Oficial, integra con `[Authorize]`/`RequireAuthorization`, eventos para errores propios | Hay que personalizar el 401 para el formato `{codigo, mensaje}` |
| B. Solo `Microsoft.IdentityModel.JsonWebTokens` + validación manual en un filtro | ese paquete | Control total | Reinventa el middleware de auth |
| C. HMAC a mano con `System.Security.Cryptography` | ninguno | Cero dependencias | Riesgo alto de errores de seguridad. **No recomendado** |

### 0.3 Hash de contraseñas — **recomiendo A**

| Opción | Paquetes | Ventajas | Desventajas |
|---|---|---|---|
| **A. `PasswordHasher<T>` de Identity** | `Microsoft.Extensions.Identity.Core` (sin el resto de Identity ni sus tablas) | Oficial, PBKDF2-SHA512 100k iteraciones, formato versionado, avisa si hay que re-hashear | Paquete trae tipos de Identity que no usamos |
| B. BCrypt | `BCrypt.Net-Next` | Muy conocido, simple | Tercero; límite de 72 bytes de clave |
| C. Argon2id | `Konscious.Security.Cryptography.Argon2` | Algoritmo más moderno (recomendado por OWASP) | Tercero, menos mantenido; parámetros a elegir a mano |
| D. PBKDF2 propio con `Rfc2898DeriveBytes.Pbkdf2` | ninguno | Cero dependencias | Código cripto propio (~30 líneas) a mantener y testear |

Nota: **no** uso ASP.NET Core Identity completo: crea ~7 tablas (`AspNetUsers`, claims, logins, tokens…) que el modelo no pide. Sería sobreingeniería.

### 0.4 Tests — **recomiendo A + Testcontainers**

| Opción | Paquetes | Ventajas | Desventajas |
|---|---|---|---|
| **A. xUnit** | `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` (plantilla `dotnet new xunit`) | El más usado en .NET, plantilla oficial | — |
| B. NUnit / C. MSTest | equivalentes | Igual de capaces | Menos ejemplos en ASP.NET Core |

Más, para tests de integración:

| Opción | Paquetes | Ventajas | Desventajas |
|---|---|---|---|
| **`Microsoft.AspNetCore.Mvc.Testing`** (obligatorio con cualquier opción) | ese | Levanta la API en memoria y hace requests reales | — |
| **DB: Testcontainers** | `Testcontainers.PostgreSql` | PostgreSQL real y desechable por corrida | Docker Desktop debe estar encendido al correr tests |
| DB: Postgres del docker-compose | ninguno | Sin paquete extra | Hay que levantar compose antes; tests comparten DB y hay que limpiarla |
| DB: EF InMemory / SQLite | `Microsoft.EntityFrameworkCore.InMemory` o `.Sqlite` | Rápido, sin Docker | No se comporta como PostgreSQL (índices únicos, tipos). Falsos verdes |

Hora fija en tests: clase propia `FakeTime : TimeProvider` (10 líneas) en vez del paquete `Microsoft.Extensions.TimeProvider.Testing`.

---

## Estructura de archivos

```
dotNetBackend/
  AuthService.slnx
  .gitignore  .dockerignore  .env.example  docker-compose.yml  Dockerfile  README.md
  docs/
    contratos-backend-auth.md          # copia del contrato (spec)
    integracion-fastapi.md             # cómo FastAPI valida estos tokens
    insomnia/vigila-auth.json          # colección para importar en Insomnia
  src/AuthService/
    Program.cs                         # composición: servicios, middleware, rutas
    appsettings.json  appsettings.Development.json
    Domain/User.cs  Role.cs  RoleNames.cs  UserSession.cs  LoginRecord.cs
    Data/AuthDbContext.cs  DevSeeder.cs  Migrations/
    Common/ErrorCodes.cs  ApiErrors.cs
    Security/AuthOptions.cs  TokenIssuer.cs  TokenReader.cs  JwtSetup.cs
    Features/Cuadrilla/CuadrillaEndpoints.cs  Contracts.cs  SessionService.cs
    Features/Admin/AdminEndpoints.cs  Contracts.cs  UserAdminService.cs
  tests/AuthService.Tests/
    Infrastructure/ApiFactory.cs  FakeTime.cs  TestUsers.cs
    HealthTests.cs  DatabaseTests.cs  TokenIssuerTests.cs
    LoginTests.cs  RenewTests.cs  LogoutTests.cs
    AdminCreateListTests.cs  AdminManageTests.cs  RateLimitTests.cs
```

## Modelo de datos

| Tabla | Columnas |
|---|---|
| `Roles` | `Id int PK`, `Name varchar(32) UNIQUE`. Seed en migración: `funcionario`, `cuadrilla`, `superadmin`, `usuario` |
| `Users` | `Id uuid PK`, `Email varchar(254) UNIQUE` (guardado en minúsculas, sin espacios), `PasswordHash text`, `FirstName varchar(100)`, `LastName varchar(100)`, `IsEmailVerified bool`, `IsActive bool`, `CreatedAt timestamptz`, `RoleId int FK` |
| `UserSessions` | `Id uuid PK` (= claim `sid`), `UserId uuid FK`, `TokenHash char(64)` (SHA-256 hex del `jti` del último token emitido), `ExpiresAt timestamptz`, `RevokedAt timestamptz NULL`, `CreatedAt timestamptz`. Índice en `UserId` |
| `LoginRecords` | `Id bigint PK identity`, `UserId uuid FK`, `SessionId uuid FK`, `OccurredAt timestamptz`, `IpAddress varchar(45) NULL`, `UserAgent varchar(512) NULL` (truncado). Índice `(UserId, OccurredAt)` |

Un rol por usuario (FK), porque el claim `rol` del contrato es uno solo.

## Códigos de error (copy fija)

| `codigo` | Status | `mensaje` |
|---|---|---|
| `credenciales_invalidas` | 401 | `Correo o contraseña incorrectos.` |
| `no_autenticado` | 401 | `Debes iniciar sesión.` |
| `sesion_expirada` | 401 | `Tu sesión expiró. Inicia sesión nuevamente.` |
| `cuenta_inactiva` | 401 | `Tu cuenta fue desactivada.` |
| `sin_permiso` | 403 | `No tienes permiso para usar esta aplicación.` |
| `demasiados_intentos` | 429 | `Demasiados intentos. Espera un momento e inténtalo de nuevo.` |
| `datos_invalidos` | 422 | Por defecto `Ingresa tu correo y contraseña.`; en rutas admin, mensaje específico (ver Task 9) |
| `email_en_uso` | 409 | `Ya existe un usuario con ese correo.` |
| `usuario_no_encontrado` | 404 | `No se encontró el usuario.` |
| `error_interno` | 500 | `Ocurrió un error. Inténtalo más tarde.` |

En rutas admin, `sin_permiso` usa el mensaje `No tienes permiso para realizar esta acción.`

---

### Task 1: Esqueleto, git y health check

**Files:**
- Create: `AuthService.slnx`, `src/AuthService/AuthService.csproj`, `src/AuthService/Program.cs`, `tests/AuthService.Tests/AuthService.Tests.csproj`, `tests/AuthService.Tests/Infrastructure/ApiFactory.cs`, `tests/AuthService.Tests/HealthTests.cs`, `.gitignore`, `docs/contratos-backend-auth.md`
- Test: `tests/AuthService.Tests/HealthTests.cs`

**Interfaces:**
- Produces: `GET /health` → 200; `public partial class Program;` (para `WebApplicationFactory<Program>`); `ApiFactory : WebApplicationFactory<Program>`

- [ ] **Step 1:** `git init`, `dotnet new gitignore`, `dotnet new sln --format slnx -n AuthService`, `dotnet new web -o src/AuthService`, `dotnet new xunit -o tests/AuthService.Tests`, agregar ambos a la solución, referencia test → api, paquete `Microsoft.AspNetCore.Mvc.Testing`. Copiar el contrato a `docs/contratos-backend-auth.md`.
- [ ] **Step 2: Test que falla**

```csharp
[Fact] public async Task Health_responde_200()
{
    var res = await _factory.CreateClient().GetAsync("/health");
    Assert.Equal(HttpStatusCode.OK, res.StatusCode);
}
```

- [ ] **Step 3:** `dotnet test` → FAIL (404).
- [ ] **Step 4:** `builder.Services.AddHealthChecks()` + `app.MapHealthChecks("/health")`.
- [ ] **Step 5:** `dotnet test` → PASS.
- [ ] **Step 6:** Commits `chore: scaffold solution and test project` y `feat: add health check endpoint`.

### Task 2: Modelo de datos, migración y roles

**Files:**
- Create: `src/AuthService/Domain/*.cs`, `src/AuthService/Data/AuthDbContext.cs`, `src/AuthService/Data/Migrations/*`, `docker-compose.yml` (solo servicio `db` por ahora), `.env.example`
- Modify: `Program.cs`, `ApiFactory.cs` (contenedor PostgreSQL, aplica migraciones)
- Test: `tests/AuthService.Tests/DatabaseTests.cs`

**Interfaces:**
- Produces: entidades de la tabla de arriba; `RoleNames.Funcionario|Cuadrilla|Superadmin|Usuario` (strings) y `RoleNames.AppFuncionarios` (`IReadOnlySet<string>` con los tres primeros); `AuthDbContext` con `DbSet` `Users`, `Roles`, `UserSessions`, `LoginRecords`; connection string `ConnectionStrings:Auth`.

- [ ] **Step 1: Tests que fallan**
  - `Migracion_crea_los_cuatro_roles` → `Roles.Select(r => r.Name)` igual a `{funcionario, cuadrilla, superadmin, usuario}`.
  - `Email_duplicado_falla` → insertar dos `User` con el mismo email lanza `DbUpdateException`.
- [ ] **Step 2:** `dotnet test` → FAIL (no compila).
- [ ] **Step 3:** Entidades + `AuthDbContext.OnModelCreating` (largos, índices, `HasData` de roles con Ids 1–4). `dotnet ef migrations add InitialSchema`.
- [ ] **Step 4:** `dotnet test` → PASS.
- [ ] **Step 5:** Commit `feat: add database schema for users, roles, sessions and login records`.

### Task 3: Errores, configuración y JSON

**Files:**
- Create: `Common/ErrorCodes.cs`, `Common/ApiErrors.cs`, `Security/AuthOptions.cs`
- Modify: `Program.cs`
- Test: `tests/AuthService.Tests/ErrorHandlingTests.cs`

**Interfaces:**
- Produces: `ErrorCodes.*` (constantes de la tabla de códigos); `static IResult ApiErrors.Result(string codigo, string? mensaje = null)` (status y mensaje por defecto salen de la tabla; `mensaje` lo reemplaza); `static Task ApiErrors.WriteAsync(HttpContext ctx, string codigo)` (para middlewares); `AuthOptions { string JwtSecret; int SessionDays = 7; }` leído de `VIGILA_JWT_SECRETO` y `VIGILA_CUADRILLA_SESION_DIAS`, validado al arrancar (secreto ≥ 32 bytes UTF-8, días ≥ 1) con `ValidateOnStart()`.

- [ ] **Step 1: Tests que fallan**
  - `Excepcion_no_controlada_devuelve_500_error_interno` (endpoint de prueba registrado solo en el factory que lanza) → cuerpo `{"codigo":"error_interno","mensaje":"Ocurrió un error. Inténtalo más tarde."}`.
  - `Secreto_corto_impide_arrancar` → factory con secreto de 10 caracteres: `CreateClient()` lanza `OptionsValidationException`.
- [ ] **Step 2:** `dotnet test` → FAIL.
- [ ] **Step 3:** `UseExceptionHandler` que escribe `error_interno`; `ConfigureHttpJsonOptions` con `JsonNamingPolicy.SnakeCaseLower`; `AuthOptions` con validación.
- [ ] **Step 4:** `dotnet test` → PASS.
- [ ] **Step 5:** Commit `feat: add error format, json policy and auth options`.

### Task 4: Hash de contraseñas y seed de desarrollo

**Files:**
- Create: `Data/DevSeeder.cs`, `appsettings.Development.json` (sección `Seed:Users`), `tests/AuthService.Tests/Infrastructure/TestUsers.cs`
- Modify: `Program.cs`
- Test: `tests/AuthService.Tests/DatabaseTests.cs`

**Interfaces:**
- Consumes: `AuthDbContext`, `RoleNames`
- Produces: `IPasswordHasher<User>` registrado; `DevSeeder.SeedAsync(AuthDbContext db, IPasswordHasher<User> hasher, IReadOnlyList<SeedUser> users, TimeProvider time, CancellationToken ct)`, idempotente (no duplica si el email existe); `record SeedUser(string Email, string Password, string FirstName, string LastName, string Rol, bool IsActive = true, bool IsEmailVerified = true)`. Corre solo si `Seed:Enabled = true` (true en Development, false por defecto).
- Produces también: `DevSeeder.SeedAdminAsync(AuthDbContext db, IPasswordHasher<User> hasher, SeedUser? admin, TimeProvider time, CancellationToken ct)`: crea el superadmin de `Seed:Admin` (variables `Seed__Admin__Email`, `Seed__Admin__Password`, `Seed__Admin__FirstName`, `Seed__Admin__LastName`) **solo si no existe ningún superadmin**. Corre en todo ambiente cuando `Seed:Admin:Email` tiene valor.
- Produces también: `static string? PasswordRules.Validate(string? password)` → `null` si largo 8–128, si no el mensaje `La contraseña debe tener entre 8 y 128 caracteres.` El seeder lo aplica y falla al arrancar si una clave de seed no cumple.
- Usuarios de prueba (en `appsettings.Development.json` y `TestUsers.cs`): uno por rol (`funcionario`, `cuadrilla`, `superadmin`, `usuario`), uno inactivo y uno sin email verificado. Las claves de prueba quedan en ese archivo, no en el README.

- [ ] **Step 1: Tests que fallan**
  - `Seed_guarda_hash_y_no_la_clave` → `PasswordHash != password` y `hasher.VerifyHashedPassword(...) == Success`.
  - `Seed_es_idempotente` → correr dos veces deja la misma cantidad de usuarios.
  - `Seed_normaliza_email` → `" Ana@Victoria.CL "` queda `ana@victoria.cl`.
  - `SeedAdmin_crea_superadmin_si_no_hay` y `SeedAdmin_no_crea_si_ya_existe_un_superadmin`.
  - `PasswordRules` → `"1234567"` inválida, `"12345678"` válida, 129 caracteres inválida.
- [ ] **Step 2:** `dotnet test` → FAIL.
- [ ] **Step 3:** Implementar `DevSeeder` y `PasswordRules` (`Security/PasswordRules.cs`); normalización `email.Trim().ToLowerInvariant()` en un método `static string User.NormalizeEmail(string)`.
- [ ] **Step 4:** `dotnet test` → PASS.
- [ ] **Step 5:** Commit `feat: add password hashing and development seed users`.

### Task 5: Emisión de tokens

**Files:**
- Create: `Security/TokenIssuer.cs`, `tests/AuthService.Tests/Infrastructure/FakeTime.cs`
- Test: `tests/AuthService.Tests/TokenIssuerTests.cs`

**Interfaces:**
- Consumes: `AuthOptions`, `TimeProvider`
- Produces: `TokenIssuer.Issue(Guid userId, string rol, Guid sessionId) : IssuedToken`; `record IssuedToken(string AccessToken, string Jti, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt)`; `static string TokenIssuer.HashJti(string jti)` (SHA-256 hex minúscula); `FakeTime : TimeProvider` con `Now` asignable y `Advance(TimeSpan)`.

- [ ] **Step 1: Tests que fallan**
  - `Token_tiene_claims_del_contrato` → `sub`, `rol`, `sid`, `jti`, `iat`, `exp` presentes; `alg == HS256`.
  - `Exp_menos_iat_es_7_dias` → `exp - iat == 604800`.
  - `Dos_tokens_tienen_jti_distinto`.
- [ ] **Step 2:** `dotnet test` → FAIL.
- [ ] **Step 3:** Implementar con `JsonWebTokenHandler.CreateToken` (viene con JwtBearer) y la hora de `TimeProvider`.
- [ ] **Step 4:** `dotnet test` → PASS.
- [ ] **Step 5:** Commit `feat: add jwt issuer with 7 day sessions`.

### Task 6: `POST /api/v1/cuadrilla/login`

**Files:**
- Create: `Features/Cuadrilla/Contracts.cs`, `Features/Cuadrilla/SessionService.cs`, `Features/Cuadrilla/CuadrillaEndpoints.cs`
- Modify: `Program.cs`
- Test: `tests/AuthService.Tests/LoginTests.cs`

**Interfaces:**
- Consumes: `TokenIssuer`, `IPasswordHasher<User>`, `AuthDbContext`, `ApiErrors`, `TimeProvider`
- Produces:
  - `record LoginRequest(string? Email, string? Password)`
  - `record FuncionarioOut(Guid Id, string Email, string Nombre, string Rol, bool Activo)` (`Nombre = FirstName + " " + LastName`)
  - `record TokenResponse(string AccessToken, string TokenType, int ExpiraEnSegundos, FuncionarioOut Funcionario)` (`TokenType = "bearer"`)
  - `SessionService.LoginAsync(string email, string password, string? ip, string? userAgent, CancellationToken ct) : Task<SessionResult>`; `record SessionResult(TokenResponse? Token, string? ErrorCode)`
  - `static IEndpointRouteBuilder MapCuadrilla(this IEndpointRouteBuilder app)`

Orden de chequeo (fijo): datos vacíos → 422 · usuario no existe → se verifica contra un hash ficticio y 401 · clave mala → 401 · inactivo o email sin verificar → 401 · rol `usuario` → 403 · ok → crea `UserSession` + `LoginRecord` + token.

- [ ] **Step 1: Tests que fallan**
  - `Login_correcto_cumple_contrato` (caso 1) → 200, `expira_en_segundos == 604800`, `token_type == "bearer"`, `funcionario.rol == "funcionario"`, `exp - iat == 7 días`.
  - `Login_guarda_sesion_y_registro` → 1 `UserSession` con `ExpiresAt = ahora + 7 d` y 1 `LoginRecord` con `IpAddress` y `UserAgent` del request.
  - `Dos_logins_crean_dos_sesiones_y_dos_registros` (otro dispositivo).
  - `Clave_mala_y_email_inexistente_responden_igual` → ambos 401 con cuerpo idéntico `credenciales_invalidas`.
  - `Cuenta_inactiva_401`, `Email_no_verificado_401` → `credenciales_invalidas`.
  - `Vecino_con_clave_correcta_403` → `sin_permiso`, sin sesión ni registro creados.
  - `Superadmin_y_cuadrilla_pueden_entrar` → 200.
  - `Email_con_mayusculas_y_espacios_entra` (Review Focus 3).
  - `Login_sin_password_422` → `datos_invalidos`.
  - `Login_fallido_no_crea_registro`.
- [ ] **Step 2:** `dotnet test` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** `dotnet test` → PASS.
- [ ] **Step 5:** Commit `feat: add funcionario login endpoint with session tracking`.

### Task 7: Autenticación JWT y `POST /sesion/renovar`

**Files:**
- Create: `Security/JwtSetup.cs`
- Modify: `Program.cs`, `SessionService.cs`, `CuadrillaEndpoints.cs`
- Test: `tests/AuthService.Tests/RenewTests.cs`

**Interfaces:**
- Consumes: `TokenIssuer`, `AuthOptions`, `TimeProvider`
- Produces:
  - `static IServiceCollection AddFuncionarioAuth(this IServiceCollection s)`: JwtBearer, HS256, sin issuer/audience (compatibilidad con FastAPI), `ClockSkew = TimeSpan.Zero`, `LifetimeValidator` que usa `TimeProvider`, `MapInboundClaims = false`. Evento `OnChallenge` escribe `sesion_expirada` si la falla fue `SecurityTokenExpiredException`, si no `no_autenticado`. `OnForbidden` escribe `sin_permiso`.
  - Política `"Funcionario"`: rol ∈ `RoleNames.AppFuncionarios` (si no → 403 `sin_permiso`).
  - `SessionService.RenewAsync(Guid userId, Guid sessionId, CancellationToken ct) : Task<SessionResult>`: claim `sid` ausente o sesión inexistente/revocada/vencida → `no_autenticado`; usuario inactivo → `cuenta_inactiva`; ok → `ExpiresAt = ahora + 7 d`, `TokenHash` = hash del nuevo `jti`, token nuevo con el mismo `sid`. **No** crea `LoginRecord`.

- [ ] **Step 1: Tests que fallan**
  - `Renovar_dia_3_extiende_7_dias` (caso 2) → 200, `jti` distinto, `exp == dia3 + 7 d`.
  - `Renovar_vencido_por_1_segundo_401` (caso 3) → `sesion_expirada`.
  - `Renovar_sin_header_401` y `Renovar_token_inventado_401` (caso 4) → `no_autenticado`.
  - `Renovar_cuenta_desactivada_401` (caso 5) → `cuenta_inactiva`.
  - `Renovar_dos_veces_con_mismo_token_funciona` (Review Focus 1).
  - `Renovar_token_sin_sid_401` (Review Focus 4) → `no_autenticado`.
  - `Renovar_con_token_de_vecino_403` → `sin_permiso` (token emitido directo con `TokenIssuer`).
  - `Renovar_no_crea_login_record`.
- [ ] **Step 2:** `dotnet test` → FAIL.
- [ ] **Step 3:** Implementar; ruta con `.RequireAuthorization("Funcionario")`.
- [ ] **Step 4:** `dotnet test` → PASS.
- [ ] **Step 5:** Commit `feat: add session renewal endpoint`.

### Task 8: `POST /logout`

**Files:**
- Create: `Security/TokenReader.cs`
- Modify: `SessionService.cs`, `CuadrillaEndpoints.cs`
- Test: `tests/AuthService.Tests/LogoutTests.cs`

**Interfaces:**
- Consumes: `AuthOptions`, `AuthDbContext`
- Produces: `TokenReader.TryReadSessionId(string? authorizationHeader, out Guid sessionId) : bool` (valida firma, **ignora** vencimiento); `SessionService.LogoutAsync(Guid sessionId, CancellationToken ct) : Task` (pone `RevokedAt` si está nulo). Ruta anónima, siempre 204.

- [ ] **Step 1: Tests que fallan** (caso 9)
  - `Logout_valido_204` y luego `/sesion/renovar` con ese token → 401 `no_autenticado`.
  - `Logout_repetido_204`, `Logout_token_vencido_204` (y la sesión queda revocada), `Logout_sin_header_204`.
  - `Logout_no_afecta_otra_sesion_del_mismo_usuario` → la sesión del otro dispositivo sigue renovando.
- [ ] **Step 2:** `dotnet test` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** `dotnet test` → PASS.
- [ ] **Step 5:** Commit `feat: add logout endpoint revoking the session`.

### Task 9: Admin — autorización, crear y listar usuarios

**Files:**
- Create: `Features/Admin/AdminEndpoints.cs`, `Features/Admin/Contracts.cs`, `Features/Admin/UserAdminService.cs`
- Modify: `Security/JwtSetup.cs`, `SessionService.cs`, `Program.cs`
- Test: `tests/AuthService.Tests/AdminCreateListTests.cs`

**Interfaces:**
- Consumes: `SessionService`, `IPasswordHasher<User>`, `PasswordRules`, `ApiErrors`, `RoleNames`
- Produces:
  - Política `"Admin"`: rol `superadmin` **y** sesión `sid` vigente y no revocada **y** usuario activo (consulta DB en cada request, vía `SessionService.IsSessionActiveAsync(Guid userId, Guid sessionId, CancellationToken ct) : Task<bool>`). Falla de sesión/estado → 401 `no_autenticado`; rol distinto → 403 `sin_permiso`.
  - `static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)` bajo `/api/v1/admin/usuarios`.
  - `record CrearUsuarioRequest(string? Email, string? Password, string? Nombre, string? Apellido, string? Rol)`
  - `record UsuarioOut(Guid Id, string Email, string Nombre, string Apellido, string Rol, bool Activo, DateTimeOffset CreadoEn)`
  - `POST /api/v1/admin/usuarios` → 201 `UsuarioOut` + header `Location`. `IsEmailVerified = true`, `IsActive = true`.
  - `GET /api/v1/admin/usuarios?activo=&rol=` → 200 `UsuarioOut[]` ordenado por `CreadoEn`. Sin paginación (pocos funcionarios; YAGNI).
  - `UserAdminService.CreateAsync(CrearUsuarioRequest r, CancellationToken ct) : Task<(UsuarioOut? Usuario, string? ErrorCode, string? Mensaje)>`; `UserAdminService.ListAsync(bool? activo, string? rol, CancellationToken ct) : Task<IReadOnlyList<UsuarioOut>>`.
- Mensajes `datos_invalidos` en alta: email vacío o sin `@` → `Ingresa un correo válido.`; nombre/apellido vacíos o > 100 → `Ingresa nombre y apellido.`; clave → mensaje de `PasswordRules`; rol ∉ {`funcionario`, `cuadrilla`} → `El rol debe ser funcionario o cuadrilla.`

- [ ] **Step 1: Tests que fallan**
  - `Admin_crea_funcionario_201` → luego ese usuario hace login 200.
  - `Admin_crea_cuadrilla_201`.
  - `Crear_superadmin_o_usuario_422` → `datos_invalidos` con mensaje de rol.
  - `Crear_email_repetido_409` (también con mayúsculas distintas) → `email_en_uso`.
  - `Crear_clave_de_7_caracteres_422`.
  - `Funcionario_no_puede_usar_admin_403` → `sin_permiso`.
  - `Admin_sin_token_401` → `no_autenticado`.
  - `Admin_con_sesion_revocada_401` (tras logout) y `Admin_desactivado_401` (Review Focus 6; se desactiva directo en DB).
  - `Listar_filtra_por_activo_y_rol` y `Listar_no_expone_password_hash` (el JSON no tiene esa propiedad).
- [ ] **Step 2:** `dotnet test` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** `dotnet test` → PASS.
- [ ] **Step 5:** Commit `feat: add admin endpoints to create and list users`.

### Task 10: Admin — estado, reset de clave e historial

**Files:**
- Modify: `Features/Admin/AdminEndpoints.cs`, `Features/Admin/Contracts.cs`, `Features/Admin/UserAdminService.cs`
- Test: `tests/AuthService.Tests/AdminManageTests.cs`

**Interfaces:**
- Consumes: Task 9
- Produces:
  - `record CambiarEstadoRequest(bool? Activo)`; `PATCH /api/v1/admin/usuarios/{id}/estado` → 200 `UsuarioOut`. Desactivar pone `RevokedAt = ahora` en todas sus sesiones abiertas. Reactivar no revive sesiones.
  - `record ResetClaveRequest(string? Password)`; `POST /api/v1/admin/usuarios/{id}/clave` → 204, nuevo hash y revoca todas sus sesiones.
  - `record IngresoOut(DateTimeOffset Fecha, string? Ip, string? Dispositivo)`; `GET /api/v1/admin/usuarios/{id}/ingresos` → 200 `IngresoOut[]`, más reciente primero, máximo 100.
  - Regla de protección (estado **y** reset de clave): destino con rol `superadmin` (incluido uno mismo) → 403 `sin_permiso`. Id inexistente → 404 `usuario_no_encontrado`.

- [ ] **Step 1: Tests que fallan**
  - `Desactivar_revoca_sesiones` → el funcionario tenía 2 sesiones; tras desactivar, `/sesion/renovar` con cada token → 401 `cuenta_inactiva`; login → 401 `credenciales_invalidas`.
  - `Reactivar_permite_login_nuevo` → login 200; el token viejo sigue rechazado.
  - `Desactivarse_a_si_mismo_403` y `Desactivar_otro_superadmin_403`.
  - `Estado_sin_campo_activo_422`, `Estado_id_inexistente_404`.
  - `Reset_clave_204_revoca_sesiones` → renovar con token viejo 401 `no_autenticado`; login con clave vieja 401; con clave nueva 200.
  - `Reset_clave_corta_422`, `Reset_clave_de_superadmin_403`.
  - `Ingresos_en_orden_descendente` → 3 logins dan 3 filas, la primera es la más nueva, con IP y User-Agent.
- [ ] **Step 2:** `dotnet test` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** `dotnet test` → PASS.
- [ ] **Step 5:** Commit `feat: add admin endpoints for user status, password reset and login history`.

### Task 11: Límites de uso

**Files:**
- Modify: `Program.cs`, `CuadrillaEndpoints.cs`, `AuthOptions.cs` (límites configurables para tests)
- Test: `tests/AuthService.Tests/RateLimitTests.cs`

**Interfaces:**
- Produces: política `"login"`: ventana fija 10/min por IP; política `"renovar"`: ventana fija 30/min por claim `sub` (cae a IP si no hay). Rechazo → 429 `demasiados_intentos` (`OnRejected`). `UseForwardedHeaders` solo si `ForwardedHeaders:Enabled = true` (para Render).

- [ ] **Step 1: Tests que fallan** (caso 10)
  - `Once_logins_bloquean_el_siguiente` → intento 11 = 429 `demasiados_intentos`.
  - `Login_bloqueado_no_bloquea_renovar` → tras el 429, `/sesion/renovar` → 200.
- [ ] **Step 2:** `dotnet test` → FAIL.
- [ ] **Step 3:** `AddRateLimiter` del framework; `UseRateLimiter` después de `UseAuthentication`.
- [ ] **Step 4:** `dotnet test` → PASS (toda la suite).
- [ ] **Step 5:** Commit `feat: add rate limits for login and renewal`.

### Task 12: Docker

**Files:**
- Create: `Dockerfile` (multi-stage `sdk:10.0` → `aspnet:10.0`, usuario no root), `.dockerignore`
- Modify: `docker-compose.yml` (servicios `db` con healthcheck y volumen, `api` en puerto `8080`), `.env.example`, `Program.cs` (aplica migraciones al arrancar si `Database:MigrateOnStartup = true`)

- [ ] **Step 1:** `docker compose up --build -d` → ambos contenedores `healthy`.
- [ ] **Step 2: Prueba de humo** con `curl`: `/health` 200 → login 200 → renovar 200 → logout 204 → renovar 401. Admin: login superadmin → crear funcionario 201 → desactivar 200 → login del funcionario 401. Ver filas en `LoginRecords` y `UserSessions` con `docker compose exec db psql`.
- [ ] **Step 3:** `docker compose down` y `up` otra vez → datos persisten, seed no duplica.
- [ ] **Step 4:** Commit `build: add dockerfile and compose setup`.

### Task 13: Documentación e Insomnia

**Files:**
- Create: `README.md`, `docs/integracion-fastapi.md`, `docs/insomnia/vigila-auth.json`

- [ ] **Step 1:** `README.md`: requisitos, cómo correr tests, cómo levantar Docker, variables de entorno.
- [ ] **Step 2:** `docs/integracion-fastapi.md`: cómo FastAPI valida el token en `/reportes*` (mismo `VIGILA_JWT_SECRETO`, HS256, `exp`, `rol` ∈ tres roles, exigir `sid`); límite conocido: FastAPI no ve el logout ni la desactivación hasta que el token vence o la app renueva (casos 6–8 del contrato quedan del lado FastAPI).
- [ ] **Step 3:** Colección Insomnia (entorno `base_url = http://localhost:8080/api/v1`, `token` y `admin_token` guardados desde las respuestas de login): carpeta «Flujo de sesión» (login → renovar → logout → renovar rechazado), carpeta «Admin» (login superadmin → crear → listar → historial → reset clave → desactivar → reactivar) y carpeta «Errores» (clave mala, vecino, inactivo, sin token, funcionario en ruta admin). `README.md` documenta también las rutas admin.
- [ ] **Step 4:** Importar en Insomnia y correr el flujo (lo haces tú; yo verifico que el JSON importa sin error).
- [ ] **Step 5:** Commit `docs: add readme, fastapi integration guide and insomnia collection`.

---

## Por confirmar en tu revisión

1. **Reset de clave a otro superadmin:** el plan lo bloquea (403), con la misma regla que el cambio de estado. ¿OK?
2. **Admin sobre vecinos (`usuario`):** el plan deja que superadmin cambie el estado y la clave de un vecino, igual que la de un funcionario. ¿OK, o lo limito a `funcionario` y `cuadrilla`?
3. **Historial:** máximo 100 ingresos, sin paginación. ¿Basta?
