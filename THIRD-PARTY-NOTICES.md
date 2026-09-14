# Avisos de terceros — sOC Remote Connections Manager

| Componente | Uso | Licencia | Titular |
|---|---|---|---|
| SSH.NET (`Renci.SshNet`) 2025.0.0 | Cliente SSH: conexión, autenticación por contraseña o clave y shell interactivo. | MIT | Renci — <https://github.com/sshnet/SSH.NET> |
| Control de Escritorio remoto (`mstscax.dll`, `MsRdpClient9`) | Las sesiones RDP. Es un componente **del propio Windows**; no se distribuye. `Interop\MSTSCLib.dll` y `Interop\AxMSTSCLib.dll` son ensamblados de interop generados con `aximp` a partir de su biblioteca de tipos (`tools\generar-interop.ps1`). | API del sistema operativo | Microsoft |
| DPAPI (`ProtectedData`) | Cifrado de las contraseñas guardadas, ligado al usuario de Windows. | API del sistema operativo | Microsoft |

Todo el código del cliente (`*.cs`, `*.xaml`), incluido el emulador de terminal, es propio y va bajo MIT (`LICENSE`).
