# Regenera los ensamblados de interop del control RDP de Windows (mstscax.dll).
#
# MSBuild de .NET Core no resuelve <COMReference>, asi que se generan una vez con aximp del SDK de
# .NET Framework y se guardan en Interop\. Solo hace falta volver a ejecutarlo si Windows cambia la
# biblioteca de tipos (o para regenerarlos en otro equipo).
$aximp = "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\aximp.exe"
if (-not (Test-Path $aximp)) { throw "No esta aximp.exe (SDK de .NET Framework 4.8): $aximp" }
$destino = Join-Path $PSScriptRoot "..\Interop"
New-Item -ItemType Directory -Force $destino | Out-Null
Push-Location $destino
try { & $aximp "$env:SystemRoot\System32\mstscax.dll" }
finally { Pop-Location }
