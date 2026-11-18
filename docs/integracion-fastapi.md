# Integración con FastAPI (`vigila-app/backend`)

Este servicio emite los tokens de los funcionarios. Las rutas `/reportes*` siguen en FastAPI y
deben validar esos tokens. Esto cubre los casos 6, 7 y 8 del contrato
([`contratos-backend-auth.md`](contratos-backend-auth.md) §7), que este servicio no implementa.

## Qué cambia en la app

La app de funcionarios apunta su URL base de auth a este servicio. Las rutas y el JSON son los
mismos del contrato, así que la app no cambia su código.

## Cómo validar el token en FastAPI

- Mismo secreto: `VIGILA_JWT_SECRETO` en ambos servicios.
- Algoritmo `HS256`, sin `iss` ni `aud`.
- Exigir `exp`, `sub`, `rol` y `sid`. **Rechazar tokens sin `sid`**: así un token de vecino o de
  admin emitido por FastAPI no sirve como token de funcionario.
- `rol` debe ser `funcionario`, `cuadrilla` o `superadmin`. Otro rol → 403 `sin_permiso`, como pide
  el caso 7 del contrato para `/reportes`. (Este servicio ya no tiene rol `usuario`: aquí un token
  con otro rol responde 401 `no_autenticado`.)
- Tolerancia de reloj pequeña (`leeway=10` segundos). PyJWT aplica la misma tolerancia a `exp`,
  `nbf` e `iat`. Con `leeway=0`, si el reloj de FastAPI va un segundo atrás, rechaza un token
  recién emitido (su `iat`/`nbf` queda «en el futuro») y la app cierra la sesión justo después
  del login. 10 s de más en `exp` no importan en una sesión de 7 días. El vencimiento exacto lo
  decide este servicio al renovar.

```python
import jwt
from fastapi import Depends, HTTPException, Request

ROLES_FUNCIONARIO = {"funcionario", "cuadrilla", "superadmin"}

def _error(status: int, codigo: str, mensaje: str) -> HTTPException:
    return HTTPException(status, detail={"codigo": codigo, "mensaje": mensaje})

def funcionario_actual(request: Request) -> dict:
    auth = request.headers.get("Authorization", "")
    if not auth.lower().startswith("bearer "):
        raise _error(401, "no_autenticado", "Debes iniciar sesión.")
    try:
        payload = jwt.decode(
            auth[7:],
            settings.VIGILA_JWT_SECRETO,
            algorithms=["HS256"],
            options={"require": ["exp", "sub", "rol", "sid"]},
            leeway=10,
        )
    except jwt.ExpiredSignatureError:
        raise _error(401, "sesion_expirada", "Tu sesión expiró. Inicia sesión nuevamente.")
    except jwt.InvalidTokenError:
        raise _error(401, "no_autenticado", "Debes iniciar sesión.")
    if payload["rol"] not in ROLES_FUNCIONARIO:
        raise _error(403, "sin_permiso", "No tienes permiso para usar esta aplicación.")
    return payload
```

Adaptar `_error` al manejador de errores que ya tiene FastAPI (`{"codigo", "mensaje"}`).

## Límite conocido

FastAPI valida solo la firma y el vencimiento, sin consultar la base de este servicio. Por eso no
ve al instante un logout, una desactivación ni un cambio de clave: el token sigue sirviendo en
`/reportes` hasta que vence o hasta que la app renueva (lo hace al abrirse y al volver a primer
plano, como máximo cada 5 minutos). En la renovación, este servicio sí rechaza la sesión revocada
o la cuenta inactiva, y la app cierra la sesión.

Si en el futuro hace falta corte inmediato, hay dos caminos: que FastAPI lea `UserSessions` en la
misma base, o agregar aquí un endpoint de validación que FastAPI consulte en cada request.
