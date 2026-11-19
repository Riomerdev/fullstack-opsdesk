# Full-stack Systems Portfolio

Dos casos de negocio que integran los tres lenguajes del portafolio. Cada carpeta es ejecutable por separado y documenta sus flujos, contratos y límites.

| Sistema | JavaScript | C# | Python |
|---|---|---|---|
| [OpsDesk](OpsDesk/README.md) | Panel de tickets y SLA | API de solicitudes e historial | Métricas SLA y reintentos |
| [SupplyChain Control Tower](SupplyChain/README.md) | Panel de despacho | API de envíos y eventos | Riesgo ETA explicable |

## Requisitos

.NET 10 SDK, Python 3.10+ y un navegador moderno. No hace falta instalar paquetes Node o Python. Los pasos de cada app están en su propio README. GitHub Actions compila las APIs y comprueba la sintaxis de los scripts al actualizar main o abrir un pull request.

## Criterios comunes

Las interfaces envían JSON a sus APIs locales. Los servicios guardan datos en la carpeta data, que está excluida de Git; los workers publican métricas por HTTP y emiten logs JSON a stdout. Se usan versionado optimista y validación de transiciones para evitar sobrescribir cambios concurrentes.

Estas implementaciones están preparadas para demostración local, no para exposición pública: no incluyen autenticación ni datos reales. El README de cada sistema describe qué requiere un despliegue empresarial.