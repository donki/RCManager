# Changelog — sOC Remote Connections Manager

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
