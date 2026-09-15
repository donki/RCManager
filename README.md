# sOC Remote Connections Manager (sOCRCManager)

Gestor de conexiones **RDP y SSH** para Windows, al estilo de Remote Desktop Manager: un árbol de
servidores organizados por carpetas a la izquierda, y cada sesión en su pestaña a la derecha.

## Dónde conseguirla

- **Releases de GitHub** (EXE autocontenido de cada versión): https://github.com/donki/RCManager/releases
- **Microsoft Store:** pendiente de reservar el nombre en Partner Center.

## Qué hace

- **Árbol de conexiones** con carpetas anidadas, buscador (por nombre, servidor, usuario, carpeta o
  notas), alta, edición, duplicado y borrado. Doble clic o Intro para conectar; Supr para borrar.
- **RDP** dentro de la pestaña, con el control de Escritorio remoto que trae Windows (el mismo que
  `mstsc.exe`): el escritorio se ajusta al tamaño de la pestaña, portapapeles compartido, dominio y
  puerto configurables.
- **SSH** dentro de la pestaña, con [SSH.NET](https://github.com/sshnet/SSH.NET) y un terminal
  propio: colores de 16 y 256, cursor, regiones de scroll, pantalla alternativa (vim, htop, less),
  historial (rueda del ratón o Mayús+RePág/AvPág), copiar y pegar (Ctrl+Mayús+C / Ctrl+Mayús+V, o
  botón derecho: pega si no hay selección y copia si la hay). El shell se redimensiona con la
  ventana. Se entra con contraseña o con clave privada (OpenSSH/PEM, con o sin frase).
- Si una conexión no tiene contraseña guardada, se pide al conectar y no se guarda.
- **Importar desde Remote Desktop Manager**: botón ⇥ de la barra del árbol; lee un `.rdm` exportado
  y conserva sus carpetas. Las contraseñas de RDM no se importan (van cifradas con su clave).
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
de nube aparecen desactivadas. Microsoft tiene registro propio en Entra desde el 2026-09-15; Google usa aún el cliente de Task Manager (misma cuenta de
desarrollador): su pantalla de consentimiento dice «TaskManager» hasta que se dé de alta un cliente
propio.

## Compilar

```
dotnet build
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Necesita el SDK de .NET 10 y Windows 8.1 o posterior (por el control RDP, `MsRdpClient9`). Los
ensamblados `Interop\MSTSCLib.dll` y `Interop\AxMSTSCLib.dll` vienen generados; para regenerarlos
(por ejemplo en otro equipo) está `tools\generar-interop.ps1`, que usa `aximp` del SDK de .NET
Framework 4.8 porque MSBuild de .NET Core no resuelve referencias COM.

## Qué puede romper

Nada fuera de su carpeta de datos. Borrar una carpeta del árbol borra las conexiones que tiene
dentro (se pide confirmación y queda el `.bak`).

## Pendiente

- Probar RDP contra un servidor real (el control de Windows no deja conectar con el propio equipo,
  y en la red de desarrollo no había otro).
- Arrastrar conexiones entre carpetas.
- Importar desde `.rdp` y desde otros gestores (RDM ya está).
- Paquete MSIX y ficha de Microsoft Store.
