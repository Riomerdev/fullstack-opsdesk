# SupplyChain Control Tower

Panel de despacho para registrar envíos, anexar eventos operativos y revisar estimaciones ETA. La interfaz JavaScript consume una API C#; un proceso Python calcula un puntaje de riesgo reproducible y muestra qué señales influyeron.

## Tecnologías

- **C# / ASP.NET Core 10:** API REST, historial de eventos, reglas de transición y control de concurrencia por versión.
- **JavaScript:** tablero operativo adaptable, creación de envíos y registro de checkpoints.
- **Python 3.10+:** score de riesgo ETA basado en reglas explicables, llamadas con timeout y reintentos.
- **Persistencia:** archivos JSON locales con actualización atómica para una instalación de una instancia.

## Ejecutar en Windows

Terminal 1, API y panel en http://localhost:5202:

~~~powershell
dotnet run --project SupplyChain/Backend/SupplyChain.Api.csproj --urls http://localhost:5202
~~~

Terminal 2, evaluador ETA:

~~~powershell
$env:SUPPLYCHAIN_API_URL = "http://127.0.0.1:5202"
python SupplyChain/Worker/eta_worker.py
~~~

Registra una orden con referencia, transportista, origen, destino y ETA UTC. Elige un evento permitido en la línea de tiempo. El worker vuelve a calcular el riesgo cada minuto y el panel consulta el estado cada 30 segundos.

## Cómo se interpreta el score

El ejemplo suma señales visibles: estado demorado o excepcional, antigüedad del último checkpoint y proximidad o vencimiento del ETA. El puntaje se limita de 0 a 100; los umbrales generan una demora estimada y una confianza declarada. No es un modelo entrenado ni una promesa logística: es una regla base que puede compararse con datos históricos reales.

## Alcance y seguridad

El panel y la API no implementan autenticación, firma de eventos externos ni datos de GPS; están pensados para demostración local. Para producción, añade identidad y permisos, ingestión firmada, trazabilidad de cambios, base transaccional, política de retención y calibración del cálculo con datos de despacho.