<#
.SYNOPSIS
    Entrega de sOC Remote Connections Manager: version en el csproj, exe autocontenido + MSIX,
    OneDrive, commit, push y release de GitHub.
.DESCRIPTION
    El CHANGELOG se escribe antes a mano (la primera seccion son las notas de la release).
    Publica: sin marcha atras. La copia en OneDrive es para Josep; la release es lo que queda.
    Mismo guion que el de sOC Lucia (tools\entregar.ps1 alli): si se toca uno, mirar el otro.
.EXAMPLE
    .\tools\entregar.ps1 -Version 2026.9.23.0 -Mensaje "Al minimizar, al area de notificacion"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Mensaje
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz
[IO.Directory]::SetCurrentDirectory($raiz)

# 1. Version (csproj y manifiesto del MSIX, que usa el formato 2026.9.230.0).
$p = 'sOCRCManager.csproj'; $x = Get-Content $p -Raw
foreach ($t in 'Version', 'AssemblyVersion', 'FileVersion') { $x = [regex]::Replace($x, "<$t>[^<]*</$t>", "<$t>$Version</$t>") }
[IO.File]::WriteAllText($p, $x)
$parts = $Version.Split('.'); $msixVer = "$($parts[0]).$($parts[1]).$($parts[2])$($parts[3]).0"

# 2. Exe + MSIX. Solo se paran las instancias de esta carpeta (pruebas); la de OneDrive sigue.
Get-Process sOCRCManager -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$raiz*" } | Stop-Process -Force
.\tools\empaquetar-msix.ps1 2>&1 | Select-String "Paquete:|error|fall"
$exe = 'bin\Release\net10.0-windows\win-x64\publish\sOCRCManager.exe'
if (-not (Test-Path $exe)) { throw 'No hay exe publicado.' }

# 3. OneDrive (constitucion general, 8).
$d = 'C:\ID\OneDrive\RCManager'
New-Item -ItemType Directory -Force $d | Out-Null
Get-ChildItem $d -Include *.msix -Recurse | Remove-Item -Force
try { Copy-Item $exe $d -Force -ErrorAction Stop; Remove-Item "$d\sOCRCManager-nueva.exe" -ErrorAction SilentlyContinue }
catch {
    # El exe de OneDrive esta en uso (la aplicacion abierta): se deja al lado y se renombra al cerrarla.
    Copy-Item $exe "$d\sOCRCManager-nueva.exe" -Force
    Write-Warning "sOCRCManager.exe estaba en uso: la version nueva queda como sOCRCManager-nueva.exe"
}
Copy-Item bin\sOCRCManager.msix "$d\sOCRCManager-$msixVer.msix" -Force
$leeme = @"
sOC Remote Connections Manager
==============================

Version $Version

Que es
------
Un gestor de conexiones remotas para Windows: Escritorio remoto (RDP) y SSH en pestañas, con su
arbol de carpetas, y transferencia de ficheros por SFTP, SCP, FTP y FTPS con explorador de dos
paneles. Las conexiones se guardan en este PC o, si quieres, cifradas en tu Google Drive o tu
OneDrive.

Si hay un sOCRCManager-nueva.exe
--------------------------------
Es que la aplicacion estaba abierta al copiar la version nueva: cierrala, borra sOCRCManager.exe y
renombra sOCRCManager-nueva.exe a sOCRCManager.exe.

Como ejecutarlo
---------------
Basta con sOCRCManager.exe: copialo donde quieras y abrelo. Los datos van a
%LOCALAPPDATA%\sOCRCManager. Windows puede avisar de que el editor es desconocido ("Windows
protegio su PC"): pulsa "Mas informacion" y "Ejecutar de todas formas".

El fichero .msix
----------------
Es el paquete de instalacion; sin firmar, Windows no lo instala (sirve para la Store si algun dia
se envia). Para usar la aplicacion basta con el exe.

Software libre bajo licencia MIT. En español y en ingles, claro y oscuro.
"@
[IO.File]::WriteAllText("$d\LEEME.txt", $leeme, (New-Object Text.UTF8Encoding $false))
"OneDrive: " + ((Get-ChildItem $d | ForEach-Object { $_.Name }) -join ', ')

# 4. Git y release.
git add -A
git commit -q -m $Mensaje
git push -q origin HEAD 2>&1 | Select-Object -Last 1
python 'D:\sOCProjects\Mobile\Shared\release-github.py' "v$Version" $exe bin\sOCRCManager.msix 2>&1 | Select-Object -Last 1
