# Plataforma académica HST — Blazor

Versión Blazor basada en el prototipo HST de Figma. Incluye panel, consulta y registro de transacciones, verificación simulada, alertas y trazabilidad con pruebas manuales.

## Abrir en Visual Studio

1. Abre `HstBlazor.sln`.
2. Espera a que Visual Studio restaure el proyecto.
3. Selecciona el perfil `https` o `http` y pulsa **F5**.

También puedes abrir una terminal en esta carpeta y ejecutar `dotnet run`.

La aplicación crea `hst.db` junto al proyecto. Si encuentra el archivo antiguo `hst-data.json`, importa sus transacciones y alertas una sola vez. Después, todos los cambios se guardan en SQLite; el botón ↻ restaura los ejemplos. No se usan datos bancarios reales ni servicios externos.

## Persistencia

La persistencia usa SQLite mediante `Microsoft.Data.Sqlite`. La cadena de conexión está en `appsettings.json`, bajo `ConnectionStrings:HstDatabase`; `HstDatabase` crea las tablas e importa el JSON heredado al iniciar. No hace falta instalar SQL Server Management Studio para ejecutar esta opción.

Al abrir por primera vez en Visual Studio, deja que restaure el paquete NuGet `Microsoft.Data.Sqlite`. Para consultar las tablas visualmente puedes abrir el archivo `hst.db` con un visor SQLite; SSMS no abre archivos SQLite.

La verificación compara contra referencias simuladas definidas en `Components/Pages/Home.razor`. Las pruebas disponibles se ejecutan al pulsar sus botones; los datos y resultados no representan operaciones bancarias reales.
