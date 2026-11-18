# AuthService

Servicio de login para la app de funcionarios de Vigila. Cumple el contrato
[`docs/contratos-backend-auth.md`](docs/contratos-backend-auth.md): sesiones de 7 días, renovación y
cierre de sesión. Además, un superadmin gestiona a los funcionarios.

- .NET 10, ASP.NET Core Minimal APIs, EF Core + PostgreSQL.
- Roles: `funcionario`, `cuadrilla` y `superadmin`. Los vecinos usan otra app: un token con otro
  rol (por ejemplo `usuario`) se trata como inválido (401 `no_autenticado`).
- Cada login crea una sesión (una por dispositivo) en `UserSessions` y una fila en `LoginRecords`.
- El JWT lleva `sid` (id de sesión). Renovar extiende la sesión. Logout la revoca.

## Requisitos

- SDK .NET 10 (`dotnet --version` → 10.0.x)
- Docker Desktop encendido (tests de integración y entorno local)

## Tests

```powershell
dotnet test
```

Cada clase de tests levanta su propio PostgreSQL desechable con Testcontainers.

## Entorno local con Docker

```powershell
docker compose up --build -d
```

- API: `http://localhost:8080`. Health: `GET /health`.
- PostgreSQL: `localhost:5432` (credenciales en `.env.example`).
- Corre en modo `Development`: aplica migraciones y carga los usuarios de prueba de
  `src/AuthService/appsettings.Development.json` (funcionario, cuadrilla, uno inactivo, uno sin verificar y un superadmin).
- Para probar con Insomnia: importar [`docs/insomnia/vigila-auth.json`](docs/insomnia/vigila-auth.json).

Para cambiar valores, copiar `.env.example` a `.env`. Para detener: `docker compose down`
(agrega `-v` para borrar la base).

## Endpoints

Rutas del contrato (`/api/v1/cuadrilla`):

| Método | Ruta | Auth | Respuesta |
|---|---|---|---|
| POST | `/login` | — | 200 token · 401 `credenciales_invalidas` · 422 · 429 |
| POST | `/sesion/renovar` | Bearer | 200 token nuevo · 401 `no_autenticado` / `sesion_expirada` / `cuenta_inactiva` · 429 |
| POST | `/logout` | Bearer (opcional) | 204 siempre |

Gestión de usuarios (`/api/v1/admin/usuarios`, solo `superadmin`):

| Método | Ruta | Body | Respuesta |
|---|---|---|---|
| POST | `/` | `email, password, nombre, apellido, rol` (`funcionario` o `cuadrilla`) | 201 usuario · 409 `email_en_uso` · 422 |
| GET | `/?activo=&rol=` | — | 200 lista |
| PATCH | `/{id}/estado` | `activo` | 200 usuario. Desactivar revoca sus sesiones |
| POST | `/{id}/clave` | `password` | 204. Revoca sus sesiones |
| GET | `/{id}/ingresos` | — | 200 últimos 100 ingresos |

Un superadmin no puede desactivar ni cambiar la clave de un superadmin (ni la suya): 403.
Errores siempre con forma `{"codigo": "...", "mensaje": "..."}`. También una ruta inexistente
(404 `no_encontrado`) y un método equivocado (405 `metodo_no_permitido`).

Login y renovar bloquean la fila del usuario en la base: si un admin lo está
desactivando o cambiando su clave en ese momento, esperan y ven el cambio. Las rutas admin
revisan además, en cada request, que el rol en la base siga siendo `superadmin`.

## Configuración

| Variable | Uso | Defecto |
|---|---|---|
| `VIGILA_JWT_SECRETO` | Secreto HS256, mínimo 32 bytes. El mismo que usa FastAPI | — (obligatorio) |
| `VIGILA_CUADRILLA_SESION_DIAS` | Duración de la sesión | `7` |
| `ConnectionStrings__Auth` | Conexión a PostgreSQL | — |
| `Database__MigrateOnStartup` | Aplica migraciones al arrancar | `false` |
| `Seed__Admin__Email`, `__Password`, `__FirstName`, `__LastName` | Crea el primer superadmin si no existe ninguno | — |
| `Seed__Enabled` | Carga `Seed:Users` (solo desarrollo) | `false` |
| `RateLimits__LoginPerMinute` | Logins por minuto por IP | `10` |
| `RateLimits__RenewPerMinute` | Renovaciones por minuto por funcionario | `30` |
| `ForwardedHeaders__Enabled` | Lee la IP real de `X-Forwarded-For`. Activar **solo** detrás de un proxy (Render) | `false` |

## Producción (Render)

1. Definir `VIGILA_JWT_SECRETO` (igual al de FastAPI), `ConnectionStrings__Auth`,
   `Database__MigrateOnStartup=true` y `ForwardedHeaders__Enabled=true`.
2. Definir `Seed__Admin__*` para el primer superadmin. Después de entrar, se pueden borrar.
3. El superadmin crea a los funcionarios con `POST /api/v1/admin/usuarios`.

Cómo FastAPI valida estos tokens: [`docs/integracion-fastapi.md`](docs/integracion-fastapi.md).

## Migraciones

```powershell
dotnet tool restore
dotnet ef migrations add <Nombre> -p src/AuthService -o Data/Migrations
```

## Commits

[Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `test:`, `docs:`, `build:`, `chore:`.
