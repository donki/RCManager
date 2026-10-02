# sOC Remote Connections Manager (sOCRCManager)

Gestor de conexiones **RDP, SSH, SFTP/SCP y FTP/FTPS** para Windows, al estilo de Remote Desktop Manager: un árbol de
servidores organizados por carpetas a la izquierda, y cada sesión en su pestaña a la derecha.

## Dónde conseguirla

- **Releases de GitHub** (EXE autocontenido de cada versión): https://github.com/donki/RCManager/releases
- **Microsoft Store:** https://apps.microsoft.com/detail/9MXKDZMLCS99

## Qué hace

- **Árbol de conexiones** con carpetas anidadas, buscador (por nombre, servidor, usuario, carpeta o
  notas), alta, edición, duplicado y borrado. Doble clic (o arrastrar al área de pestañas) conecta; Ctrl + doble clic
  edita; el botón ⇒ o Intro conectan; Supr borra. Se arrastran conexiones y carpetas a otra carpeta. El árbol se conserva al cerrar (carpetas
  abiertas, selección, ancho del panel).
- **RDP** dentro de la pestaña, con el control de Escritorio remoto que trae Windows (el mismo que
  `mstsc.exe`) y **todas sus opciones**, en las mismas pestañas que mstsc: *Pantalla* (ajustar a la
  pestaña con resolución dinámica, o tamaño fijo; colores; todos los monitores; barra de conexión),
  *Recursos locales* (audio y micrófono, teclas de Windows, impresoras, portapapeles con ficheros,
  unidades del PC —«C en tu PC» en el remoto, para copiar y mover ficheros—, tarjetas
  inteligentes, puertos serie, dispositivos Plug and Play), *Experiencia* (efectos visuales, caché,
  reconexión automática) y *Avanzado* (certificado, sesión de administración, puerta de enlace de
  Escritorio remoto). Pantalla completa nativa con la barra de mstsc. Las conexiones importadas de
  `.rdm` reciben los valores por defecto.
- **SSH** dentro de la pestaña, con [SSH.NET](https://github.com/sshnet/SSH.NET) y un terminal
  propio: colores de 16 y 256, cursor, regiones de scroll, pantalla alternativa (vim, htop, less),
  historial (rueda del ratón o Mayús+RePág/AvPág), copiar y pegar (Ctrl+Mayús+C / Ctrl+Mayús+V, o
  botón derecho: pega si no hay selección y copia si la hay). El shell se redimensiona con la
  ventana. Se entra con contraseña o con clave privada (OpenSSH/PEM, con o sin frase).
- **SFTP / SCP** y **FTP / FTPS** en la pestaña, con un explorador de dos paneles como el de
  FileZilla: este equipo a la izquierda, el servidor a la derecha. Subir y bajar ficheros y carpetas
  enteras (botón, F5, doble clic o arrastrando al otro panel) con cola, progreso y cancelar; crear
  carpeta (F7), renombrar (F2), borrar (Supr), subir un nivel (Retroceso). SFTP con contraseña o
  clave privada, opción de transferir por SCP; FTPS explícito o implícito. [FluentFTP](https://github.com/robinrodricks/FluentFTP)
  (MIT) para FTP. En servidores Linux/Unix: columnas de permisos y propietario y un botón para
  cambiarlos (chmod por SFTP o `SITE CHMOD`; chown por id o, por nombre, con `chown` por SSH;
  en FTP `SITE CHOWN` si el servidor lo admite).
- **Transferencias a medida** por conexión: ficheros a la vez (cada uno con su conexión), avisar
  antes de sobrescribir (o sobrescribir/saltar), conservar fechas, ocultos, keep-alive, tiempo de
  espera, reintentos; FTP pasivo/activo y codificación. **Editor de texto integrado** (doble clic en
  un fichero de texto del servidor; Ctrl+S guarda en el servidor, Ctrl+F busca, pregunta al cerrar);
  lo demás se abre con el programa por defecto de Windows. En local, doble clic = programa por defecto.
- **Pestañas sueltas**: una pestaña se saca a su propia ventana arrastrándola fuera (o con su botón
  de la pestaña o el menú del botón derecho) y vuelve arrastrando la barra de esa ventana sobre la principal (o
  con su botón, o cerrándola con la X). **La sesión no se reconecta**: el control de la sesión se
  mueve tal cual de una ventana a otra (el escritorio remoto es el mismo control ActiveX, con la
  misma ventana nativa). Varias a la vez, cada una en su monitor; cada conexión recuerda dónde
  estuvo su ventana. Al cerrar la principal se cierran también, preguntando antes (se puede quitar
  en Ajustes).
- **Una sola instancia**: abrirla otra vez (desde el exe, el MSIX o un acceso directo) trae al frente
  la que ya estaba abierta, aunque esté escondida en el área de notificación, y le pasa lo pedido
  (`--open "Nombre"` abre esa conexión allí). Si la abierta no contesta en un par de segundos, la
  nueva arranca igual. Si la nueva es de una versión posterior, la vieja le deja el sitio (y, si
  tiene sesiones abiertas, pregunta antes de cortarlas).
- **Pantalla completa en el monitor que elijas** y **zoom** por pestaña (letra del terminal y de los
  paneles; escala 100–200 % del escritorio RDP).
- `sOCRCManager.exe --open "Nombre"` abre esa conexión al arrancar; `--edit "Nombre"` (y `--edit-tab N`)
  abre su editor; `--edit-file "Nombre" "/ruta"` abre ese fichero en el editor; `--size AnchoxAlto`
  fija el tamaño de la ventana (para capturas); `--tray` arranca escondida en el área de notificación.
- Si una conexión no tiene contraseña guardada, se pide al conectar, con la opción de recordarla
  (cifrada con DPAPI para el usuario de Windows).
- **Importar conexiones**: en Ajustes ⚙; de un `.rdm` exportado de Remote Desktop Manager (RDP,
  SSH, FTP/FTPS, SFTP/SCP, conservando sus carpetas) o de ficheros `.rdp` del cliente de Escritorio
  remoto (varios de golpe, cada uno con sus opciones de mstsc). Las contraseñas no se importan.
- **Dónde se guardan** (Ajustes ⚙): en este PC, o en Google Drive / OneDrive entrando con tu cuenta.
  En la nube el fichero va a la carpeta privada de la aplicación (ámbitos `drive.appdata` /
  `Files.ReadWrite.AppFolder`: sin acceso al resto de tus ficheros) y **cifrado con una frase que
  solo tú conoces** (AES-256-GCM con clave derivada por PBKDF2). La frase se guarda en este PC
  protegida por DPAPI; en otro PC hay que escribir la misma. Sin frase no se sube nada. Al arrancar
  se baja la copia de la nube si es más reciente; cada guardado se sube. Gana el último que guarda.
- Español e inglés (se cambia al momento), tema claro/oscuro siguiendo al de Windows, «Acerca de»
  canónico del catálogo.

## Dónde guarda las cosas y a qué accede

- `%LOCALAPPDATA%\sOCRCManager\connections.json`: las conexiones, legible a propósito (se puede
  copiar, versionar o arreglar a mano). Cada guardado deja el anterior en `connections.bak`. El
  botón de carpeta de la barra inferior lo abre en el Explorador.
- Las **contraseñas** van cifradas con DPAPI para el usuario de Windows (`dpapi1:…`): el fichero
  copiado a otro equipo o leído por otra cuenta no las revela. No hay contraseña maestra.
- Solo habla con los servidores que el usuario da de alta. Nada sale a ningún otro sitio: sin
  cuenta, sin telemetría, sin anuncios.

## Identificadores OAuth

Los clientes OAuth de Google y de Microsoft no van en el repositorio: se leen de
`oauth.local.props` (ignorado; ver `oauth.props`) o de las variables `RC_GOOGLE_CLIENT_ID`,
`RC_GOOGLE_CLIENT_SECRET` y `RC_MS_CLIENT_ID`. Sin ellos la aplicación compila igual y las opciones
de nube aparecen desactivadas. Microsoft tiene registro propio en Entra y Google un cliente de escritorio propio (proyecto «sOC
Remote Connections Manager»), ambos desde el 2026-09-15.

## Compilar

```
dotnet build
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Necesita el SDK de .NET 10 y Windows 8.1 o posterior (por el control RDP, `MsRdpClient9`). Los
ensamblados `Interop\MSTSCLib.dll` y `Interop\AxMSTSCLib.dll` vienen generados; para regenerarlos
(por ejemplo en otro equipo) está `tools\generar-interop.ps1`, que usa `aximp` del SDK de .NET
Framework 4.8 porque MSBuild de .NET Core no resuelve referencias COM.

## Pruebas

`RCManager.Tests` (xUnit): **961 pruebas**, todas en verde. Referencian la aplicación entera y
prueban la lógica (importadores .rdm/.rdp, modelo y árbol, almacén, ajustes, DPAPI y cifrado de la
nube, Google Drive/OneDrive y OAuth con un HTTP falso, errores de conexión, textos es/en, permisos,
FTP contra un servidor FTP falso en 127.0.0.1, SFTP/SCP contra uno en memoria, el intérprete y las
teclas del terminal, instancia única con su tubería de verdad, colocación de ventanas sueltas) y
también **las ventanas y controles**: la principal (árbol, búsqueda, arrastrar y soltar, sesiones,
pantalla completa, sacar y devolver pestañas, bandeja, arranque y otra instancia), el editor de
conexiones, Ajustes, Acerca de, los diálogos, el explorador de dos paneles, el editor de texto,
permisos, el terminal y la ventana suelta. Se manejan en un hilo STA propio, abiertas fuera de la
pantalla y sin activarse (`RCManager.Tests/UiThread.cs`); las modales las contesta la prueba. El
control RDP (el de Windows, creado de verdad pero **nunca conectado**), SSH.NET, SFTP, el icono de la
bandeja, los monitores y el ratón van detrás de interfaces con un doble. Nada toca los datos reales,
el registro, la nube ni ningún servidor (solo 127.0.0.1).

- Cobertura de lo instrumentado (`sOCRCManager.dll`, ReportGenerator): **99,3 %** de líneas.
- Cobertura sobre toda la app: **99,4 %** (4893 de 4924 líneas ejecutables en 57 ficheros `.cs`).
  Lo que queda sin cubrir es el pegamento con Windows que no se puede ejecutar en una prueba sin
  efectos fuera: `Shell_NotifyIcon` y el menú de la bandeja, conectar SSH de verdad, `Connect()`
  del control RDP, el diálogo de abrir ficheros del sistema y poco más.
- Tiempo del banco: **unos 67 s** (`dotnet test --no-build`, sin compilar). Fecha: 2026-10-03.

**Cómo se cuenta «toda la app»** (`tools/cobertura-app.py <carpeta-de-resultados> --app . --detalle`):
todos los `.cs` de la app, fuera de `obj/`, `bin/`, `*.g.cs`, `*.Designer.cs` y los proyectos de
pruebas. En lo que compila el banco (hoy, todo), cuentan las líneas que marca coverlet —que se ejecuta
**sin** excluir `CompilerGeneratedAttribute`, así que lambdas y métodos async cuentan; solo se excluye
`GeneratedCodeAttribute` (el `InitializeComponent` de las XAML)— quitando las de llaves sueltas. Lo
que el banco no compilase contaría entero como no cubierto, y solo sus sentencias: sin llaves
sueltas, `using`, declaraciones sin cuerpo como `static extern`, atributos, interfaces ni
comentarios. Por eso la cifra de toda la app puede salir unas décimas por encima de la de
ReportGenerator, que sí cuenta las llaves.

`RCManager.UITests` (FlaUI): **10 pruebas de interfaz** sobre el exe Debug en modo aislado, en
**unos 17 s**: arranque, Ajustes, crear/editar/borrar una conexión, importar un `.rdp`, idioma,
«Acerca de», sacar una pestaña a su ventana y devolverla, varias ventanas sueltas al cerrar la
principal, y la instancia única (segunda instancia con `--open` que trae la primera de la bandeja,
y arranque con `--tray`). Ver `RCManager.UITests/README.md`.

```
dotnet test RCManager.Tests
dotnet test RCManager.Tests --collect:"XPlat Code Coverage" --settings RCManager.Tests\coverage.runsettings
dotnet tool restore
dotnet tool run reportgenerator -reports:RCManager.Tests\TestResults\*\coverage.cobertura.xml -targetdir:cobertura -reporttypes:TextSummary
python tools\cobertura-app.py RCManager.Tests\TestResults --app . --detalle
```

## Qué puede romper

Nada fuera de su carpeta de datos. Borrar una carpeta del árbol borra las conexiones que tiene
dentro (se pide confirmación y queda el `.bak`).

## Pendiente

- Probar RDP contra un servidor real (el control de Windows no deja conectar con el propio equipo,
  y en la red de desarrollo no había otro).
- Sacar a una ventana y devolver una pestaña RDP **conectada** a un servidor real: se ha probado con
  el control sin conectar (modo aislado), comprobando que es el mismo control con la misma ventana
  nativa antes, en la ventana suelta y de vuelta; falta verlo con una sesión viva.
- Devolver una ventana suelta **arrastrando su barra** sobre la principal: el arrastre hacia fuera
  se probó con el ratón de verdad (modo aislado) y funciona; el de vuelta no se llegó a confirmar con
  el guion de ratón (se paró para no estorbar en el equipo). Volver con el botón o con la X sí está
  probado.
