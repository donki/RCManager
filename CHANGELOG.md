# Changelog — sOC Remote Connections Manager

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
