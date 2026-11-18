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
| `ConnectionStrings__Auth` | Conexión a PostgreSQL en formato `Host=...;Database=...` | — |
| `DATABASE_URL` | Conexión en formato URL (`postgresql://user:clave@host/base?sslmode=require`), como la entregan Neon y Render. Se usa si falta `ConnectionStrings__Auth` | — |
| `Database__MigrateOnStartup` | Aplica migraciones al arrancar | `false` |
| `Seed__Admin__Email`, `__Password`, `__FirstName`, `__LastName` | Crea el primer superadmin si no existe ninguno | — |
| `Seed__Enabled` | Carga `Seed:Users` (solo desarrollo) | `false` |
| `RateLimits__LoginPerMinute` | Logins por minuto por IP | `10` |
| `RateLimits__RenewPerMinute` | Renovaciones por minuto por funcionario | `30` |
| `KeepAlive__Url` | URL pública a la que el servicio se llama a sí mismo (`<url>/health`). Si falta, usa `RENDER_EXTERNAL_URL`, que Render define solo. Sin ninguna, el keep-alive queda apagado | — |
| `KeepAlive__Interval` | Cada cuánto se llama. Debe ser menor a 15 min (Render duerme a los 15) | `00:10:00` |
| `ForwardedHeaders__Enabled` | Lee la IP real de `X-Forwarded-For`. Activar **solo** detrás de un proxy (Render) | `false` |

## Producción (Render + Neon)

La base va en **Neon** (plan Free): no caduca y se apaga sola cuando nadie la usa. La base gratis
de Render se borra a los 30 días, así que no conviene.

### 1. Base en Neon

1. Crear un proyecto en [neon.com](https://neon.com). Región: **AWS US East (N. Virginia)**, la
   misma que el servicio en Render, para que cada consulta tarde menos.
2. En **Connect**, dejar **Connection pooling apagado** (conexión directa, sin `-pooler` en el
   host) y copiar la URL: `postgresql://...neon.tech/neondb?sslmode=require&channel_binding=require`.

### 2. Servicio en Render

1. **New → Web Service →** este repo. Runtime **Docker**. Región **Virginia (US East)**.
2. **Health Check Path:** `/health`.
3. Variables de entorno:

| Variable | Valor |
|---|---|
| `PORT` | `8080` (el puerto donde escucha la app) |
| `DATABASE_URL` | La URL de Neon, tal cual |
| `VIGILA_JWT_SECRETO` | El mismo de FastAPI |
| `Database__MigrateOnStartup` | `true` |
| `ForwardedHeaders__Enabled` | `true` |
| `Seed__Admin__Email`, `Seed__Admin__Password`, `Seed__Admin__FirstName`, `Seed__Admin__LastName` | Primer superadmin |

No definir `ASPNETCORE_ENVIRONMENT` (queda en `Production` y no carga usuarios de prueba).

### 3. Después del primer deploy

1. Login con el superadmin y crear a los funcionarios con `POST /api/v1/admin/usuarios`.
2. Borrar las variables `Seed__Admin__*`.

### Límites de Neon Free

- 1 GB por proyecto y 100 CU-hours de cómputo al mes. Con el tamaño mínimo (0,25 CU) son unas
  400 horas con la base encendida.
- La base se apaga tras 5 minutos sin consultas y se enciende sola en la siguiente (la primera
  consulta tarda un poco más).
- Si se acaban las horas, Neon suspende la base hasta el mes siguiente.
- Por eso `/health` **no consulta la base**: el keep-alive mantiene despierto a Render sin
  gastar horas de Neon. No agregar un chequeo de base a `/health`.

**Keep-alive.** En Render, el servicio se llama solo a `GET /health` cada 10 minutos para no
dormirse. No hay que configurar nada: usa `RENDER_EXTERNAL_URL`. En local (Docker) queda apagado.

> Ojo con el plan gratuito: da 750 horas al mes por cuenta. Un servicio despierto todo el mes usa
> unas 744. Si FastAPI también se mantiene despierto en la misma cuenta, se pasan del límite y
> Render suspende **los dos** hasta el mes siguiente. Para evitarlo, deja el keep-alive solo en uno
> o usa un plan pago.

Cómo FastAPI valida estos tokens: [`docs/integracion-fastapi.md`](docs/integracion-fastapi.md).

## Migraciones

```powershell
dotnet tool restore
dotnet ef migrations add <Nombre> -p src/AuthService -o Data/Migrations
```

## Commits

[Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `test:`, `docs:`, `build:`, `chore:`.
