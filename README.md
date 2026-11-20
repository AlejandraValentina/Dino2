# Dino2 Desktop

Dino2 calcula curvas de rendimiento para motores monocilíndricos 4T y 2T de láminas. Permite comparar una configuración original A con una variante B y estudiar rangos de parámetros. También incluye el ciclo Otto ideal como referencia independiente.

## Empezar en Windows

Extraé todo el ZIP Windows x64 y abrí **Dino2.Desktop.exe**. El portable incluye el runtime de .NET 10. Al iniciar se carga un caso sintético 4T. Desde esa ventana se puede abrir **Motor 2T** u **Otto de referencia**.

- [Manual de Desktop](docs/manual-desktop.md)
- [Referencia científica y convenciones](docs/referencia-cientifica.md)
- [Guía para desarrolladores y publicación](docs/desarrollo.md)
- [Historial científico y documentación](docs/indice.md)
- [Auditoría, cambios y verificación de etapa 8](docs/etapa8.md)

## Compilar y comprobar

En Windows con SDK .NET 10:

~~~powershell
dotnet build Dino2.slnx -c Release
dotnet run --project src/Dino2.Desktop -c Release
.\scripts\verify.ps1
.\scripts\publish.ps1 -Destination artifacts/stage8-win-x64 -Archive artifacts/Dino2-stage8-win-x64.zip
~~~

Los scripts detectan un SDK local en **.tools/dotnet** si existe. Las pruebas usan ejecutores de consola. **verify.ps1** devuelve un error si falla una comprobación. Los binarios y resultados generados quedan en **artifacts**.

La evidencia disponible es de verificación numérica. Los modelos para motor real conservan parámetros estimados y la validación experimental sigue pendiente. [Evidencia 4T](docs/validacion-experimental.md) · [Evidencia 2T](docs/datos-experimentales-2t.md).
