Actúa como un desarrollador Senior Full-Stack en ASP.NET Core MVC y Bootstrap 5.

Necesito rediseñar la vista principal operativa (`Views/Home/Index.cshtml`) de mi sistema **ComdisAI**, un ERP comercial para PyMEs distribuidoras en México, reemplazando la pantalla actual por defecto ("Welcome / Learn about building Web apps with ASP.NET Core") por un Dashboard Operativo diario para el usuario autenticado.

### Contexto Técnico y Restricciones
- **Framework:** ASP.NET Core MVC (.NET) con Razor Views (`.cshtml`).
- **UI Framework:** Usar **exclusivamente Bootstrap 5** (clases nativas `container-fluid`, `row`, `col-*`, `card`, `table`, `nav-pills`, `badge`, `modal`) y **Bootstrap Icons** (`bi-*`). No usar Tailwind CSS.
- **Layout:** Mantener intacto `Views/Shared/_Layout.cshtml` (que ya contiene la barra de navegación superior con `ComdisAI`, `Home`, `Catalogs`, `Sales`, `Procurement`, el perfil `System Administrator — ADMIN`, `Sign out` y el footer `© 2026 - ComdisAI v1.0.324 - Privacy`). El contenido debe ir únicamente dentro de `Views/Home/Index.cshtml` y los estilos personalizados en `wwwroot/css/site.css` (o dentro de `@section Styles`).

### Requerimientos Funcionales y Visuales de `Views/Home/Index.cshtml`
1. **Encabezado Operativo:**
   - Saludo dinámico al usuario autenticado (`@User.Name`), fecha actual y contexto operativo.
   - Botón de búsqueda rápida con atajo de teclado `Ctrl + K` que abra un modal para filtrar productos (Code, descripción) y clientes (razón social, RFC).
   - Botones de acceso rápido (`+ New Sales Order` y `+ New Purchase Order`) usando Tag Helpers (`asp-controller`, `asp-action`).

2. **Fila de 4 KPIs Principales (Moneda en MXN):**
   - **Ventas del Mes (MXN):** Monto acumulado, porcentaje vs. mes anterior y meta mensual.
   - **Pedidos por Surtir:** Cantidad de órdenes pendientes de surtido/facturación en almacén (status approved).
   - **Compras en Tránsito:** Cantidad de órdenes de compra abiertas y valor total en MXN (status approved).

3. **Columna Principal (8 columnas en desktop): Operación Diaria con Tabs (`nav-pills`)**
   - **Tab 1 — Pedidos de Venta Activos:** Tabla responsiva con Folio (`SalesOrderHeader.OrderNumber`), Cliente (razón social y RFC), Fecha, Total en MXN, Estatus con badges de Bootstrap y acción rápida.
   - **Tab 2 — Órdenes de Compra (Procurement):** Tabla con Folio (`PurchaseOrderHeaders.OrderNumber`), Proveedor, Entrega estimada, Total en MXN y Estatus.
   - **Tab 3 — Top Productos Rotación:** Barra de progreso visual con los SKUs de mayor desplazamiento en el mes.


### Entregables Esperados
1. Código completo y limpio de `Views/Home/Index.cshtml` (incluyendo `@section Scripts` para el atajo `Ctrl+K` y filtrado en vivo del modal).
2. Reglas CSS mínimas y limpias para agregar en `wwwroot/css/site.css` (variables de color corporativo azul `#0d6efd`, fondo `#f8fafc`, bordes sutiles `#e2e8f0` y tipografía tabular para cifras monetarias).
3. Un ViewModel `HomeDashboardViewModel` en `Models/` y la actualización sugerida en `Controllers/HomeController.cs` con datos de ejemplo realistas en MXN listos para conectar después a Entity Framework Core.