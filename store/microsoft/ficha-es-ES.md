# Descripción de Store — Español (España)

Todo lo de aquí es para pegar tal cual en el formulario de Partner Center.

Antes de enviar: **reservar el nombre** «sOC Remote Connections Manager» en Partner Center y
comprobar que la identidad del paquete (`tools\empaquetar-msix.ps1`, parámetro `-IdentityName`)
coincide con la que asigne *Product management › Product identity*.

---

## Nombre del producto

```
sOC Remote Connections Manager
```

## Descripción

```
sOC Remote Connections Manager guarda tus conexiones a servidores y las abre en pestañas: escritorio
remoto (RDP), terminal SSH, ficheros por SFTP/SCP y FTP/FTPS. Todo en una ventana, con carpetas.

Un árbol de conexiones con carpetas anidadas y buscador; cada sesión se abre en su pestaña y se
puede poner a pantalla completa. Doble clic para conectar, o arrastra la conexión al área de
pestañas.

ESCRITORIO REMOTO (RDP)
• El mismo control de Escritorio remoto que trae Windows, con todas sus opciones en las mismas
  pestañas que mstsc: pantalla, recursos locales, experiencia y avanzado (certificado, sesión de
  administración, puerta de enlace de Escritorio remoto).
• El escritorio se ajusta al tamaño de la pestaña (resolución dinámica) o va a un tamaño fijo.
• Portapapeles con texto, imágenes y ficheros; las unidades de tu PC dentro del remoto para copiar
  y mover ficheros con el Explorador; impresoras, audio, micrófono, tarjetas inteligentes.

TERMINAL SSH
• Terminal propio con colores de 16 y 256, regiones de scroll, pantalla alternativa (vim, htop),
  historial, copiar y pegar. Contraseña o clave privada (OpenSSH/PEM).

FICHEROS: SFTP/SCP Y FTP/FTPS
• Explorador de dos paneles, este PC a la izquierda y el servidor a la derecha: sube y baja
  ficheros y carpetas enteras arrastrando o con F5, con cola, progreso y cancelar.
• Nueva carpeta, renombrar, borrar; en servidores Linux, permisos (chmod) y propietario (chown).
• FTPS explícito o implícito.

TUS CONEXIONES, DONDE TÚ QUIERAS
• En este PC, o en tu Google Drive u OneDrive para tenerlas en todos tus equipos: van a la carpeta
  privada de la aplicación y cifradas con una frase que solo tú conoces (AES-256). Las contraseñas
  guardadas en el PC van cifradas con la protección de datos de Windows.
• Importa tus conexiones de Remote Desktop Manager (.rdm) conservando sus carpetas.

Sin cuenta, sin anuncios, sin rastreadores. Software libre bajo licencia MIT. En español y en
inglés, con modo claro y oscuro.
```

## Novedades de esta versión

Se deja **en blanco** en el primer envío.

## Características del producto

```
RDP, SSH, SFTP/SCP y FTP/FTPS en pestañas
Árbol de conexiones con carpetas y buscador
Todas las opciones del cliente de Escritorio remoto de Windows
Escritorio ajustado a la pestaña y pantalla completa
Ficheros entre el PC y el servidor con el Explorador
Terminal SSH con colores, historial y clave privada
Explorador de dos paneles para SFTP y FTP, con permisos en Linux
Conexiones en este PC o cifradas en Google Drive / OneDrive
Importación desde Remote Desktop Manager (.rdm)
Software libre, en español y en inglés, claro y oscuro
```

## Palabras clave

```
rdp, escritorio remoto, ssh, sftp, ftp, scp, terminal, conexiones, servidores, remote desktop manager
```

## Categoría

Productividad (o Herramientas de desarrollo).

## Capturas de pantalla

En `capturas/` (1600x900, con datos de demostración; se rehacen con
`scratchpad\rc_shots.ps1` y los parámetros `--size`, `--open`, `--edit`, `--edit-tab` de la
aplicación):

| Fichero | Qué se ve |
|---|---|
| `01-ficheros.png` | Árbol con carpetas y una sesión SFTP abierta: dos paneles, permisos y propietario |
| `02-opciones-rdp.png` | Editor de conexión, pestaña Avanzado: certificado y puerta de enlace |
| `03-recursos-locales.png` | Editor de conexión, pestaña Recursos locales |

## Logotipos

En `logos/`: póster 9:16 (720×1080 y 1440×2160), caja 1:1 (1080 y 2160), icono 300/150/71 y
superhéroe 16:9 (1920×1080 y 3840×2160, solo hace falta si se sube un tráiler).

## Dependencias de software (política 10.2.4.1)

No hay software no integrado que declarar: el escritorio remoto usa el control que trae Windows
(`mstscax`), el terminal SSH y los ficheros van con bibliotecas dentro del paquete (SSH.NET y
FluentFTP, MIT).

## Declaración de privacidad (para el campo «URL de la política de privacidad»)

Vale la misma página de política del catálogo (ver `constitution/CONSTITUCION-WEB.md`), con el
párrafo específico: «sOC Remote Connections Manager no recoge ningún dato. Las conexiones se guardan
en el PC o, si el usuario lo elige, en su propio Google Drive u OneDrive cifradas con su frase; la
aplicación no tiene servidor propio ni envía nada a terceros.»
