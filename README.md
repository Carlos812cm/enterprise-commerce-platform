# Enterprise Commerce Platform

Proyecto personal de backend para comercio electrónico, desarrollado con **C#, .NET y ASP.NET Core**. Su objetivo es explorar escenarios de retail B2C y compras B2B mediante módulos de negocio, APIs, persistencia relacional y procesamiento asíncrono.

**Estado: en desarrollo.** La implementación actual se concentra en el catálogo de productos y su infraestructura de integración. No es una tienda terminada ni se presenta como un sistema comercial en producción.

El alcance documentado corresponde al código integrado hasta **ECP-11G.2 · Catalog Outbox Processing**, [PR #40](https://github.com/Carlos812cm/enterprise-commerce-platform/pull/40). Las funcionalidades previstas y los cambios de ramas en progreso no se consideran entregados.

## Qué demuestra este proyecto

- **Programación orientada a objetos y reglas de negocio:** modelado de productos, opciones y variantes, con separación entre dominio, aplicación, infraestructura y API.
- **Desarrollo de APIs y bases de datos:** creación y consulta de productos, validaciones, autorización por permisos, persistencia con Entity Framework Core y PostgreSQL, migraciones y control de concurrencia.
- **Calidad y mantenimiento:** pruebas unitarias, de integración y de arquitectura; documentación técnica y operativa; control de versiones mediante ramas, commits y pull requests.
- **Integración de servicios:** procesamiento de mensajes con un Worker, RabbitMQ y Redis, incluyendo reintentos, registro de fallos y observabilidad.

## Funcionalidades implementadas

| Área | Alcance actual |
| --- | --- |
| Catálogo | Creación de productos en estado borrador y consulta administrativa por identificador. |
| Consulta pública | Lectura de productos publicados por `slug`, exposición de variantes activas y respuestas condicionales con ETag. |
| Persistencia | EF Core con PostgreSQL, migraciones, restricciones de unicidad y control de concurrencia optimista. |
| Autorización | Operaciones administrativas protegidas con Bearer token y permiso `catalog.products.write`; configuración local de identidad con Keycloak. |
| Caché | Caché de catálogo con HybridCache y Redis; propagación de invalidaciones entre procesos mediante Redis Pub/Sub. |
| Outbox | Persistencia transaccional de eventos e invalidaciones, procesamiento por lotes, leases, reintentos y estado dead-letter. Publicación en RabbitMQ con confirmaciones. |
| Operación y calidad | API y Worker en contenedores, health checks, trazas y métricas con OpenTelemetry, pruebas automatizadas y validaciones en GitHub Actions. |

### Endpoints de negocio disponibles

| Método | Ruta | Acceso y propósito |
| --- | --- | --- |
| `POST` | `/api/catalog/products` | Protegido. Crea un producto en borrador. |
| `GET` | `/api/catalog/products/{productId}` | Protegido. Consulta un producto por identificador. |
| `GET` | `/api/storefront/products/{slug}` | Público. Devuelve únicamente productos publicados y sus variantes activas. |

Los dos endpoints administrativos requieren `catalog.products.write`. Consulta los contratos de [creación de productos](docs/api/catalog/create-draft-product.md), [consulta por identificador](docs/api/catalog/get-product-by-id.md) y [consulta pública por slug](docs/api/storefront/get-published-product-by-slug.md).

**Límite importante:** existen reglas de publicación en el dominio y procesamiento de eventos de publicación, pero el flujo de publicación todavía no está expuesto como endpoint HTTP en esta versión. Crear un borrador no lo hace visible en la consulta pública.

## Arquitectura y tecnologías

El código se organiza por módulos de negocio. El catálogo separa dominio, casos de uso, contratos, persistencia y endpoints. `Commerce.Api` atiende peticiones HTTP; `Commerce.Worker` procesa la Outbox fuera de la transacción que guarda el producto.

La Outbox de PostgreSQL conserva la intención durable de envío. Redis Pub/Sub propaga invalidaciones, pero no sustituye ese registro durable. El procesamiento utiliza semántica **at-least-once**: los consumidores deben contemplar entregas duplicadas e idempotencia, no asumir entrega exactamente una vez.

| Capa | Tecnologías |
| --- | --- |
| Backend | C#, .NET 10, ASP.NET Core |
| Datos | Entity Framework Core, PostgreSQL |
| Caché e integración | HybridCache, Redis, RabbitMQ |
| Identidad local | Keycloak |
| Entorno y automatización | Docker Compose, Git, GitHub Actions, CodeQL |
| Pruebas y diagnóstico | Testcontainers, OpenTelemetry; configuración de Prometheus, Tempo y Grafana |

Las versiones de referencia están en [global.json](global.json), [Directory.Packages.props](Directory.Packages.props), [.env.example](.env.example) y [docker-compose.yml](docker-compose.yml).

### Organización del repositorio

```text
src/
  BuildingBlocks/        Componentes compartidos
  Hosts/
    Commerce.Api/        Host HTTP
    Commerce.Worker/     Procesamiento en segundo plano
    Commerce.ServiceDefaults/
  Modules/
    Catalog/             Módulo con funcionalidad implementada
    ...                  Límites y estructura de otros módulos

tests/
  Unit/
  Integration/
  Architecture/

docs/                    Contratos, persistencia, decisiones y operación
deploy/                  Configuración de identidad y observabilidad
scripts/ci/              Verificaciones de contenedores para CI
```

La presencia de carpetas para Pricing, Inventory, Cart, Checkout, Ordering, Payments o B2BProcurement no significa que sus flujos completos estén implementados.

## Ejecución local

La siguiente guía utiliza **PowerShell 7**, un checkout nuevo y los valores de desarrollo de `.env.example`. Ejecuta cada paso desde la raíz del repositorio y detente si un comando falla. Si ya tienes una copia de trabajo, no la sobrescribas ni cambies de rama con cambios pendientes.

### 1. Requisitos y configuración

Necesitas Git, Docker con soporte para contenedores Linux y Docker Compose, y un SDK compatible con `global.json` (versión de referencia: **10.0.302**). Docker debe estar en ejecución. Las descargas de paquetes e imágenes requieren acceso a Internet.

```powershell
git clone https://github.com/Carlos812cm/enterprise-commerce-platform.git
Set-Location enterprise-commerce-platform

if (-not (Test-Path -LiteralPath .env)) {
    Copy-Item -LiteralPath .env.example -Destination .env
}

dotnet --version
docker version
docker compose version
```

Los valores de ejemplo son exclusivamente para desarrollo local. No publiques `.env`, no reutilices esas credenciales en producción y no expongas este entorno a Internet. El Compose base publica puertos en el host sin restringirlos a loopback; utiliza una máquina de desarrollo y controles de red adecuados.

### 2. Iniciar dependencias y compilar

```powershell
docker compose up --detach postgres redis rabbitmq keycloak
docker compose ps

dotnet tool restore
dotnet restore EnterpriseCommercePlatform.slnx --locked-mode
dotnet build EnterpriseCommercePlatform.slnx --configuration Release --no-restore
```

Antes de continuar, PostgreSQL, Redis y RabbitMQ deben aparecer como `healthy`. Keycloak puede tardar más en iniciar y no tiene un health check definido en el Compose base. Para usar los endpoints protegidos, comprueba también que responde su configuración de identidad:

```powershell
Invoke-RestMethod "http://localhost:8080/realms/commerce/.well-known/openid-configuration"
```

### 3. Aplicar migraciones antes de iniciar API y Worker

La API y el Worker necesitan el esquema del catálogo, incluida `catalog.outbox_messages`. El siguiente bloque apunta explícitamente a la base local de ejemplo y restaura la variable de entorno al terminar:

```powershell
& {
    $ErrorActionPreference = 'Stop'
    $previousPostgres = $env:ConnectionStrings__Postgres
    $catalogProject = 'src/Modules/Catalog/Catalog.Infrastructure/Catalog.Infrastructure.csproj'

    try {
        $env:ConnectionStrings__Postgres = 'Host=localhost;Port=5432;Database=commerce;Username=commerce;Password=commerce_dev_password;Pooling=true'

        dotnet ef database update --project $catalogProject --startup-project $catalogProject --context CatalogDbContext --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) {
            throw 'No se pudieron aplicar las migraciones del catálogo.'
        }
    }
    finally {
        $env:ConnectionStrings__Postgres = $previousPostgres
    }
}
```

**Comprueba el destino antes de ejecutar:** si modificaste las credenciales, el nombre de base o `POSTGRES_PORT` en `.env`, ajusta también la cadena del bloque. La fábrica de EF Core lee `ConnectionStrings__Postgres`, no carga `.env` por sí misma. No ejecutes esta guía contra una base compartida o de producción.

### 4. Iniciar y comprobar la aplicación

```powershell
docker compose up --build --detach commerce-api commerce-worker
docker compose ps

Invoke-RestMethod "http://localhost:5000/health/live"
Invoke-RestMethod "http://localhost:5000/health/ready"
```

Los health checks pueden necesitar unos segundos después del arranque. Para diagnosticar fallos:

```powershell
docker compose logs --tail 100 commerce-api commerce-worker
```

Con los puertos de ejemplo, la API usa `localhost:5000`, Keycloak `localhost:8080` y la administración de RabbitMQ `localhost:15672`. El Worker no publica un puerto HTTP al host. Ajusta las direcciones si cambiaste los puertos en `.env`.

Estos pasos preparan servicios y esquema; no crean productos de demostración. Una respuesta `404` del endpoint público para un producto inexistente o en borrador es esperada. Consulta [seguridad](docs/security/) para la configuración de autenticación y el [runbook de Outbox](docs/operations/runbooks/catalog-outbox.md) para operación y diagnóstico.

Para detener el entorno sin solicitar el borrado de volúmenes persistentes:

```powershell
docker compose down
```

## Pruebas y validación

Desde la raíz del repositorio, después de restaurar dependencias y con Docker disponible:

```powershell
dotnet format EnterpriseCommercePlatform.slnx --verify-no-changes --no-restore
dotnet build EnterpriseCommercePlatform.slnx --configuration Release --no-restore
dotnet test EnterpriseCommercePlatform.slnx --configuration Release --no-restore
```

El repositorio incluye pruebas unitarias de dominio y aplicación, pruebas de integración con infraestructura real mediante Testcontainers y pruebas de límites arquitectónicos. Los escenarios de integración cubren persistencia, concurrencia, transacciones de Outbox, reintentos y adaptadores de Redis y RabbitMQ.

El workflow [PR Validation](.github/workflows/pr-validation.yml) también comprueba cambios pendientes del modelo EF, configuración de contenedores y observabilidad, construcción de imágenes y un smoke test que aplica migraciones antes de arrancar los hosts. [CodeQL](.github/workflows/codeql.yml) aporta análisis de código. El resultado vigente debe consultarse en los checks de cada commit o pull request; no se asume que una validación histórica garantiza una ejecución nueva.

## Alcance pendiente

El objetivo de evolución incluye el flujo HTTP completo de publicación, los escenarios B2C de carrito y checkout con ventas de alta concurrencia, y los escenarios B2B de catálogos privados y aprobaciones. No se presentan aquí como funcionalidades terminadas.

Tampoco se afirma disponer de una interfaz de tienda completa, integraciones de pago productivas, operación comercial real o resultados de rendimiento a escala. La base técnica permite trabajar hacia esos objetivos, pero no constituye evidencia de haberlos alcanzado.

## Guía de lectura del código

Para revisar el proyecto, comienza por el [módulo de catálogo](src/Modules/Catalog/) y sus contratos HTTP; continúa con la [persistencia](docs/persistence/catalog.md), el [runbook de Outbox](docs/operations/runbooks/catalog-outbox.md) y las [pruebas](tests/). Las [decisiones de arquitectura](docs/adr/) explican las elecciones técnicas.

La documentación ampliada está en [docs](docs/). El flujo de contribución, las convenciones de commits y las reglas de arquitectura se describen en [CONTRIBUTING.md](CONTRIBUTING.md). Los reportes de seguridad se rigen por [SECURITY.md](SECURITY.md).
