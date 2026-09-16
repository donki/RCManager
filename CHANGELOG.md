# Changelog — sOC Remote Connections Manager

## 2026.9.16.3 — Ver la contraseña

- Todas las casillas de contraseña (conexión, puerta de enlace, la que se pide al conectar y la
  frase de cifrado) llevan el botón del ojo para verla (constitución general 6).

## 2026.9.16.2 — El botón de conectar, arriba

- El botón de conectar pasa a la barra de arriba del árbol, el primero (antes estaba abajo, junto a
  los ajustes, y se perdía).

## 2026.9.16.1 — Corregido: «Avisar» del certificado no dejaba conectar

- El nivel de autenticación del servidor iba al revés en el control de Windows (1 = no conectar,
  2 = avisar): con la opción por defecto «Avisar» salía «no puede continuar porque se requiere
  autenticación» en servidores con certificado propio. Ahora avisa y deja seguir, como mstsc.

## 2026.9.16.0 — Doble clic conecta

- **Doble clic** sobre una conexión la abre; **Ctrl + doble clic** la edita (o renombra la carpeta).
  **Arrastrar** una conexión del árbol al área de pestañas también la abre.
- Paquete **MSIX** para la Microsoft Store (`tools\empaquetar-msix.ps1`), que se entrega junto al exe.

## 2026.9.15.8 — Permisos y propietario en servidores Linux

- En el panel remoto de SFTP y FTP, cuando el servidor es Unix (el listado trae `rwx`), salen las
  columnas *Permisos* y *Propietario* y un botón para cambiarlos: casillas leer/escribir/ejecutar
  por propietario, grupo y otros con el octal a la vista, propietario y grupo por nombre o id, y
  «aplicar a todo lo de dentro» en carpetas. SFTP cambia permisos por el propio protocolo y el
  propietario por id; por nombre lanza `chown` por SSH con las mismas credenciales. FTP usa
  `SITE CHMOD` (casi todos los servidores) y `SITE CHOWN` (solo algunos; si no, se avisa). En un
  servidor Windows no aparece nada de esto.

## 2026.9.15.7 — Ficheros: SFTP/SCP y FTP/FTPS

- **Dos tipos de conexión nuevos**: *SFTP / SCP* (por SSH, con contraseña o clave privada; opción
  de transferir por SCP) y *FTP / FTPS* (en claro, FTPS explícito —AUTH TLS, puerto 21— o
  implícito —puerto 990—; el certificado del servidor se acepta). Carpetas de inicio local y
  remota por conexión.
- **Explorador de dos paneles** en la pestaña, como FileZilla: este equipo a la izquierda (con
  «Este equipo» para cambiar de unidad), el servidor a la derecha. Subir y bajar ficheros y
  carpetas enteras (botón, F5, doble clic en un fichero o arrastrando al otro panel), con cola,
  progreso y cancelar; crear carpeta (F7), renombrar (F2), borrar (Supr), subir un nivel
  (Retroceso) y escribir una ruta directamente.
- `sOCRCManager.exe --open "Nombre"` abre esa conexión al arrancar (para accesos directos).
- Al buscar en el árbol solo salen las carpetas con conexiones que encajan.
- FluentFTP (MIT) para FTP; SSH.NET (MIT) ya estaba para SSH.

## 2026.9.15.6 — Todas las opciones del cliente de Escritorio remoto, y el árbol como se dejó

- **Editor de conexión con las pestañas de mstsc**: *General*, *Pantalla* (ajustar a la pestaña o
  tamaño fijo, colores, todos los monitores, barra de conexión), *Recursos locales* (audio y
  micrófono, teclas de Windows, impresoras, portapapeles, unidades, tarjetas inteligentes, puertos
  serie, dispositivos Plug and Play), *Experiencia* (fondo, suavizado de fuentes, composición,
  arrastre, animaciones, estilos visuales, caché de mapas de bits, reconexión automática) y
  *Avanzado* (certificado del servidor, sesión de administración, puerta de enlace de Escritorio
  remoto con sus credenciales o las mismas de la sesión). Valores por defecto: los de mstsc, salvo
  las unidades (compartidas) y el tamaño (ajustado a la pestaña). Las conexiones importadas de
  `.rdm` reciben esos valores por defecto.
- **El árbol se conserva** al cerrar: carpetas abiertas, conexión seleccionada y ancho del panel
  vuelven igual al abrir.
- **Doble clic** sobre una conexión o carpeta abre su edición (conectar: botón o Intro).

## 2026.9.15.5 — Ficheros entre el PC y el escritorio remoto

- **Unidades de este PC en el remoto** (opción por conexión, activada por defecto): en el escritorio
  remoto aparecen como «C en <tu PC>» en *Este equipo* (y como `\\tsclient\C`), con lo que se copian
  y mueven ficheros con el Explorador en los dos sentidos. Los USB que se enchufen durante la sesión
  también.
- El portapapeles compartido ya llevaba ficheros (Ctrl+C en un Explorador, Ctrl+V en el otro); ahora
  la opción lo dice.

## 2026.9.15.4 — El escritorio remoto sigue al tamaño de la pestaña

- Al cambiar el tamaño de la ventana (y al volver de pantalla completa) se pide al servidor la
  resolución que cabe en la pestaña, en píxeles físicos (resolución dinámica, RDP 8.1+): se ve
  nítido y sin barras. Antes, al volver de pantalla completa, el escritorio se quedaba a la
  resolución de la pantalla y no se escalaba. Si el servidor no admite el cambio en caliente, queda
  el escalado (SmartSizing) de siempre.

## 2026.9.15.3 — La barra de estado ya no se come la ventana

- Un error de la nube con el JSON entero (veinte líneas) hacía crecer la barra de estado hasta
  dejar la ventana inutilizable al arrancar. Ahora la barra es de una línea y los errores de Google
  Drive / OneDrive salen resumidos; un 401/403 se traduce a «vuelve a entrar y marca la casilla».

## 2026.9.15.2 — Aviso si Google entra sin el permiso de Drive

- Google enseña cada permiso como una casilla; si la de Drive se queda sin marcar, la entrada
  terminaba «bien» y la primera sincronización daba un 403. Ahora se comprueba lo concedido al
  volver del navegador y se avisa: «vuelve a entrar y marca la casilla de Google Drive».

## 2026.9.15.1 — Cliente propio de Google

- La entrada con Google (Drive) usa ya un cliente propio en el proyecto «sOC Remote Connections
  Manager»: su pantalla de permisos deja de decir «TaskManager». Quien ya había entrado tiene que
  volver a entrar una vez.

## 2026.9.15.0 — Registro propio en Microsoft

- La entrada con Microsoft (OneDrive) usa ya un registro de Entra propio: la pantalla de permisos
  dice «sOC Remote Connections Manager» en vez de «Task Manager». Quien ya había entrado tiene que
  volver a entrar una vez. (Google sigue con el cliente compartido hasta que haya uno propio.)

## 2026.9.14.2 — Pantalla completa de verdad, arrastrar en el árbol y contraseña al conectar

- **Pantalla completa** desde el botón de la pestaña. RDP usa la del propio control de Windows
  (toda la pantalla, con la barra de conexión de arriba que se esconde sola y trae restaurar y
  cerrar; el servidor cambia la resolución a la de la pantalla). SSH: la ventana entera, con una
  barra superior que se esconde sola y vuelve al llevar el ratón arriba, con el nombre de la sesión,
  salir de pantalla completa y desconectar. F11 entra y sale; Ctrl+Esc sale.
- **Arrastrar y soltar en el árbol**: una conexión o una carpeta entera se lleva a otra carpeta
  soltándola encima (sobre una conexión, a su carpeta). La carpeta de destino se tiñe al pasar.
- **Contraseña al conectar**: la ventana que la pide ofrece **recordarla en este PC** (cifrada con
  DPAPI para el usuario de Windows, igual que desde el editor).
- Corregido: la fila seleccionada del árbol no se veía bien (ahora índigo con texto blanco) y el
  árbol no se desplaza de lado (se recorta), así que las flechas de las carpetas no se pierden.
- Corregido: al editar una conexión, la carpeta salía vacía y no se podía escribir (el desplegable
  editable no tenía caja de texto).

## 2026.9.14.1 — En la nube si quieres, importación de RDM y nombre definitivo

- **Dónde se guardan las conexiones** (Ajustes ⚙): en este PC, en **Google Drive** o en **OneDrive**,
  entrando con la cuenta del usuario (navegador del sistema, PKCE). El fichero va a la carpeta
  privada de la aplicación en cada servicio (sin acceso al resto de ficheros) y **cifrado con una
  frase del usuario** (AES-256-GCM, PBKDF2) que no sale del PC. Al arrancar se baja si es más
  reciente; cada guardado se sube. El último que guarda gana; el `.bak` local queda de red.
- **Importar `.rdm`** de Remote Desktop Manager conservando su árbol de carpetas (RDP y SSH; las
  contraseñas de RDM van cifradas con su clave y no se importan: se piden al conectar).
- Nombre definitivo: sOC Remote Connections Manager (`sOCRCManager.exe`), icono propio.

## 2026.9.14.0 — Primera versión

- Árbol de conexiones con carpetas (anidadas), buscador, alta, edición, duplicado y borrado. Todo en
  `%LOCALAPPDATA%\sOCRCManager\connections.json` (con copia `.bak`), contraseñas cifradas con DPAPI.
- **RDP** en pestañas con el control de Escritorio remoto de Windows: escritorio ajustado a la
  pestaña, portapapeles compartido, dominio, puerto.
- **SSH** en pestañas con SSH.NET y un terminal propio (VT100/xterm: colores de 16 y 256, cursor,
  regiones de scroll, pantalla alternativa, historial con la rueda o Mayús+RePág), contraseña o
  clave privada, redimensionado del shell al cambiar la ventana, copiar/pegar (Ctrl+Mayús+C/V y
  botón derecho).
- Español e inglés, tema claro y oscuro siguiendo a Windows, «Acerca de» canónico.
