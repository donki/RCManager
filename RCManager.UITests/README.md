# Pruebas de interfaz (FlaUI)

Recorridos de la ventana de sOC Remote Connections Manager con [FlaUI](https://github.com/FlaUI/FlaUI)
(MIT) sobre UI Automation y xUnit. Lanzan el **exe Debug ya compilado** en modo aislado y lo manejan
como lo haría una persona: abrir Ajustes, crear, editar y borrar una conexión, copiar la configuración de otra, importar un `.rdp`,
cambiar de idioma y abrir «Acerca de»; sacar una pestaña a su propia ventana y devolverla (comprobando
que el control de Escritorio remoto es el mismo, con la misma ventana nativa: la sesión no se habría
cortado), varias ventanas sueltas y cerrar la principal (pregunta; cancelar y aceptar), y la instancia
única: una segunda instancia con `--open` trae la primera de la bandeja y abre la conexión allí, una con
`--tray` no enseña nada, y una que arranca con `--tray` sale al abrir otra.

## Cómo se lanzan

```powershell
# 1. La aplicacion en Debug (las pruebas usan bin\Debug\net10.0-windows\sOCRCManager.exe)
dotnet build sOCRCManager.csproj -c Debug -m:1 -nodeReuse:false
# 2. Las pruebas
dotnet build RCManager.UITests\RCManager.UITests.csproj -c Debug -m:1 -nodeReuse:false
dotnet test RCManager.UITests\RCManager.UITests.csproj --no-build
```

Otro exe: variable `RCMANAGER_EXE` con su ruta (tiene que ser Debug: en Release no hay modo aislado).

Son 11 pruebas y tardan unos 20 s en total; corren una detrás de otra (`xunit.runner.json`), cada
una con su propia instancia de la aplicación y su carpeta de datos vacía.

## Modo aislado (`SOC_SANDBOX`)

Las pruebas arrancan la aplicación con `SOC_SANDBOX=<carpeta temporal>`
(`%TEMP%\sOCRCManager-uitests\<prueba>-<guid>`, que se borra al acabar). Solo en compilaciones
Debug (`Services/Sandbox.cs`), la aplicación entonces:

- guarda ajustes, conexiones, estado del árbol y registro de errores en esa carpeta, nunca en
  `%LOCALAPPDATA%\sOCRCManager`;
- **no conecta a nada**: conectar (botón, doble clic, Intro, arrastrar a las pestañas, `--open`)
  abre la pestaña con el control de la sesión **sin conectar** (para probar las pestañas y las
  ventanas sueltas), sin pedir contraseña, y lo dice en la barra de estado;
- no entra en Google Drive ni en OneDrive (los botones de Ajustes no hacen nada);
- no se activa al abrir sus ventanas (tampoco al traerla al frente otra instancia) y pone
  `[SOC_SANDBOX]` en el título;
- al minimizar se esconde, pero sin poner icono en el área de notificación del Windows de verdad;
- la instancia única usa otros nombres (mutex y tubería) que dependen de la carpeta aislada: la de
  pruebas nunca encuentra a la de verdad ni al revés, y cada prueba tiene la suya.

«Arrancar con Windows» no existe en esta aplicación.

Aun así, en las pruebas **no** se hace doble clic en el árbol (conecta; editar es el botón Editar
o Ctrl + doble clic), ni se pulsa Conectar (constitución general 8.4). Los servidores de prueba son
`ejemplo.invalid` e `importado.invalid` (el dominio `.invalid` no existe por norma: aunque se
conectase, no llegaría a ninguna parte). Las pruebas de pestañas abren sus sesiones con `--open`
sobre un `connections.json` de prueba escrito en la carpeta aislada antes de arrancar.

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
se conecta en modo aislado; las sesiones SSH y de ficheros tampoco. Que la sesión sobrevive al mover
la pestaña se comprueba por el control: la misma ventana nativa antes, en la ventana suelta y de
vuelta (si se destruyese, la sesión se cortaría; la aplicación además lo apunta en su registro).
Arrastrar con el ratón tampoco entra en la tanda (mueve el ratón de verdad): las pruebas usan los
botones. Sacar una pestaña arrastrándola se probó aparte con un guion que solo mueve el ratón si
nadie ha tocado el equipo en el último minuto (funciona); devolverla arrastrando la barra de la
ventana suelta queda por confirmar así.
