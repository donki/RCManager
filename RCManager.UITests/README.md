# Pruebas de interfaz (FlaUI)

Recorridos de la ventana de sOC Remote Connections Manager con [FlaUI](https://github.com/FlaUI/FlaUI)
(MIT) sobre UI Automation y xUnit. Lanzan el **exe Debug ya compilado** en modo aislado y lo manejan
como lo haría una persona: abrir Ajustes, crear, editar y borrar una conexión, importar un `.rdp`,
cambiar de idioma y abrir «Acerca de».

## Cómo se lanzan

```powershell
# 1. La aplicacion en Debug (las pruebas usan bin\Debug\net10.0-windows\sOCRCManager.exe)
dotnet build sOCRCManager.csproj -c Debug -m:1 -nodeReuse:false
# 2. Las pruebas
dotnet build RCManager.UITests\RCManager.UITests.csproj -c Debug -m:1 -nodeReuse:false
dotnet test RCManager.UITests\RCManager.UITests.csproj --no-build
```

Otro exe: variable `RCMANAGER_EXE` con su ruta (tiene que ser Debug: en Release no hay modo aislado).

Son 6 pruebas y tardan unos 10 s en total; corren una detrás de otra (`xunit.runner.json`), cada
una con su propia instancia de la aplicación y su carpeta de datos vacía.

## Modo aislado (`SOC_SANDBOX`)

Las pruebas arrancan la aplicación con `SOC_SANDBOX=<carpeta temporal>`
(`%TEMP%\sOCRCManager-uitests\<prueba>-<guid>`, que se borra al acabar). Solo en compilaciones
Debug (`Services/Sandbox.cs`), la aplicación entonces:

- guarda ajustes, conexiones, estado del árbol y registro de errores en esa carpeta, nunca en
  `%LOCALAPPDATA%\sOCRCManager`;
- **no conecta a nada**: conectar (botón, doble clic, Intro, arrastrar a las pestañas, `--open`)
  solo deja un aviso en la barra de estado;
- no entra en Google Drive ni en OneDrive (los botones de Ajustes no hacen nada);
- no se activa al abrir sus ventanas y pone `[SOC_SANDBOX]` en el título.

«Arrancar con Windows» no existe en esta aplicación, y no hay instancia única: la de pruebas convive
con la de verdad.

Aun así, en las pruebas **no** se hace doble clic en el árbol (conecta; editar es el botón Editar
o Ctrl + doble clic), ni se pulsa Conectar (constitución general 8.4). Los servidores de prueba son
`ejemplo.invalid` e `importado.invalid`.

## Capturas

Cada paso deja una captura PNG (PrintWindow con `PW_RENDERFULLCONTENT`: sale bien aunque la ventana
esté tapada) en `RCManager.UITests\artifacts\` (ignorada en git), con el nombre
`<prueba>-<paso>.png`. Ahí queda también `foco.log`: si la aplicación se quedó con el primer plano al
arrancar y al acabar cada prueba.

## Foco

Todo va por patrones de UI Automation (Invoke, Value, SelectionItem): no se mueve el ratón ni se
teclea, y las ventanas se abren sin activarse. Pero al pulsar un botón por UI Automation Windows le
da a la aplicación permiso para pasar al frente, y tras el primer botón la ventana de pruebas acaba
en primer plano (unos segundos por prueba, ~10 s la tanda entera). Mejor no escribir en otra ventana
mientras corren.

## Cómo se buscan los controles

Por `AutomationId`, que en WPF es el `x:Name` de cada control. Los botones de los diálogos pequeños
(`PromptWindow`, montados en código) llevan `OkButton` y `CancelButton`. El diálogo de abrir fichero
de Windows se maneja por sus identificadores de siempre: la casilla del nombre es `1148` y «Abrir»
es `1`.

Lo que no se puede probar así: el escritorio remoto es el control ActiveX de Windows (mstscax) y no
se conecta en modo aislado; las sesiones SSH y de ficheros tampoco.
