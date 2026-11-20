# Modern Ops Suite

Aplicación de seguimiento de compras que integra C#, JavaScript y Python en un flujo operativo realista. El equipo registra órdenes en una interfaz web; la API las persiste en PostgreSQL; un worker calcula atrasos y vencimientos y publica indicadores autenticados para el panel.

## Componentes

- ASP.NET Core 10 Minimal API con Dapper y Npgsql: órdenes, métricas y salud.
- PostgreSQL 18: datos relacionales, índices, restricciones y snapshots históricos.
- Python 3.12 + HTTPX: worker periódico con timeout, reintento exponencial y logs JSON.
- JavaScript nativo: panel web adaptable, captura de órdenes y refresco periódico.
- Docker Compose: entorno local de tres servicios y volumen persistente.

## Inicio rápido

Copia .env.example a .env, asigna una contraseña local y un token aleatorio de al menos 16 caracteres. Inicia desde esta carpeta:

~~~sh
docker compose up --build
~~~

Abre http://localhost:5081. API OpenAPI: /openapi/v1.json. Estado de API y base: /health. El worker añade métricas aproximadamente cada 30 segundos. Requiere Docker Desktop con Compose.

Para conservar los datos al detener: docker compose down. Para borrar también la base demo: docker compose down -v.

## Seguridad y operación

El token de escritura de métricas se comparte solo entre API y worker y se compara en tiempo constante. El panel no recibe el secreto. Órdenes y API están expuestas solo al host local desde Compose; el ejemplo no incorpora identidad de usuarios. Antes de desplegar públicamente, añade OIDC, autorización por organización, TLS, secretos administrados, límites de solicitudes y retención de métricas.

Los datos semilla son inventados y el volumen ejecuta init.sql solo en su primera creación. Las métricas son snapshots, no predicciones; late indica una orden abierta cuyo compromiso temporal ya pasó y dueSoon indica una orden abierta con vencimiento en las próximas 24 horas.

## English

Local procurement operations suite with a JavaScript dashboard, .NET REST API, PostgreSQL persistence and a Python metrics worker. The worker computes late and due-soon indicators, publishes snapshots through a token-protected internal endpoint, and retries transient failures with bounded exponential backoff.
