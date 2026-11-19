# OpsDesk — Mesa de servicio

Aplicación integrada para un equipo interno de soporte. La interfaz JavaScript crea y filtra tickets; la API C# aplica estados, versiones y plazos SLA; un worker Python consulta la cola y publica métricas explicables. Los registros se conservan en JSON local para que el ejemplo pueda ejecutarse sin servicios externos.

## Tecnologías

- **C# / ASP.NET Core 10:** API REST, validación, máquina de estados y persistencia atómica.
- **JavaScript:** panel adaptable, actualizaciones periódicas y operaciones vía fetch.
- **Python 3.10+:** proceso de métricas, salida estructurada JSON y reintentos con backoff.
- **Automatización:** GitHub Actions compila .NET y verifica sintaxis de Python y JavaScript.

## Ejecutar en Windows

Terminal 1, API y panel en http://localhost:5201:

~~~powershell
dotnet run --project OpsDesk/Backend/OpsDesk.Api.csproj --urls http://localhost:5201
~~~

Terminal 2, worker SLA:

~~~powershell
$env:OPSDESK_API_URL = "http://127.0.0.1:5201"
python OpsDesk/Worker/sla_worker.py
~~~

## Flujos

Crea solicitudes con prioridades normal (24 h), high (4 h) y critical (1 h). Cada cambio de estado incluye expectedVersion para rechazar ediciones sobre una versión desactualizada. El worker genera un snapshot cada 30 s, registra logs JSON y reintenta fallos de red con retroceso exponencial. El panel combina tickets y el último cálculo del worker.

## Límites deliberados

Esta demo está pensada para ejecutarse localmente y usa persistencia JSON de una sola instancia. No tiene identidad multiusuario ni correo real. Antes de exponerla públicamente, añade OIDC, permisos por grupo, base de datos transaccional, rotación de eventos y métricas centralizadas; no almacenes información sensible de clientes.