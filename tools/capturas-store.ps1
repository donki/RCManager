# Capturas de la ficha de la Store con datos de demostracion (antes: python tools\logos-y-demo-store.py),
# sin tocar el escritorio del usuario:
# la app arranca minimizada, se muestra sin activarla (SW_SHOWNOACTIVATE) y se captura con
# PrintWindow. Las ventanas secundarias (editor, ajustes) se abren por la propia app con --shot.
$ErrorActionPreference = 'Stop'
$R = 'D:\sOCProjects\Tools\RCManager'
$store = "$env:LOCALAPPDATA\sOCRCManager\connections.json"
$settings = "$env:LOCALAPPDATA\sOCRCManager\settings.json"
$out = "$R\store\microsoft\capturas"
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public class SW {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool r);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; }
}
'@
function Windows($procId) {
  $list = New-Object System.Collections.ArrayList
  $cb = [SW+EnumProc]{ param($h,$l) $p2=0; [SW]::GetWindowThreadProcessId($h,[ref]$p2) | Out-Null
    if ($p2 -eq $procId -and [SW]::IsWindowVisible($h)) { $sb = New-Object System.Text.StringBuilder 256; [SW]::GetWindowText($h,$sb,256) | Out-Null; [void]$list.Add(@{H=$h; T=$sb.ToString()}) }; $true }
  [SW]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
  return $list
}
function Shot($h, $file) {
  $r = New-Object SW+R; [SW]::GetWindowRect($h,[ref]$r) | Out-Null
  $b = New-Object System.Drawing.Bitmap(($r.Rt-$r.L),($r.B-$r.T)); $g=[System.Drawing.Graphics]::FromImage($b)
  $dc=$g.GetHdc(); [SW]::PrintWindow($h,$dc,2) | Out-Null; $g.ReleaseHdc($dc); $b.Save($file); $b.Dispose()
}

# --- datos de demostracion (copia de los reales antes)
Get-Process sOCRCManager -ErrorAction SilentlyContinue | Stop-Process -Force
Copy-Item $store "$env:TEMP\rc_demo\connections.real.json" -Force
Copy-Item $settings "$env:TEMP\rc_demo\settings.real.json" -Force
# la conexion SFTP de demostracion lleva la contraseña guardada (DPAPI) para que no la pida
Add-Type -AssemblyName System.Security
$pw = "dpapi1:" + [Convert]::ToBase64String([System.Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes("secreto"), [Text.Encoding]::UTF8.GetBytes("sOCRCManager"), 'CurrentUser'))
$demo = Get-Content "$env:TEMP\rc_demo\connections.json" -Raw -Encoding UTF8 | ConvertFrom-Json
foreach ($c in $demo.Connections) { if ($c.Name -eq 'Web de la empresa (SFTP)') { $c.PasswordProtected = $pw } }
$demo | ConvertTo-Json -Depth 6 | Set-Content $store -Encoding UTF8
$s = Get-Content $settings -Raw | ConvertFrom-Json; $s.ExpandedFolders = @('Oficina','Clientes','Clientes/Acme','Casa'); $s.SelectedConnectionId = $null; $s.TreeWidth = 300; $s.Storage = 'Local'
$s | ConvertTo-Json -Depth 5 | Set-Content $settings -Encoding UTF8

# --- servidor SFTP de prueba
$srv = Start-Process -WindowStyle Hidden python -ArgumentList "$PSScriptRoot\servidores-prueba.py" -PassThru -RedirectStandardOutput "$env:TEMP\fs.log" -RedirectStandardError "$env:TEMP\fs.err"
Start-Sleep 4

try {
  $exe = "$R\bin\Release\net10.0-windows\win-x64\publish\sOCRCManager.exe"
  $p = Start-Process $exe -ArgumentList '--size','1600x900','--open','"Web de la empresa (SFTP)"' -WindowStyle Minimized -PassThru
  Start-Sleep 9
  $h = $p.MainWindowHandle
  [SW]::ShowWindowAsync($h, 4) | Out-Null; Start-Sleep 1
  [SW]::MoveWindow($h, 100, 100, 1600, 900, $true) | Out-Null; Start-Sleep 3
  Shot $h "$out\01-ficheros.png"
  Stop-Process -Id $p.Id -Force; Start-Sleep 1

  $p = Start-Process $exe -ArgumentList '--size','1600x900','--edit','"Terminal Server"','--edit-tab','5' -WindowStyle Minimized -PassThru
  Start-Sleep 6
  $h = $p.MainWindowHandle; [SW]::ShowWindowAsync($h, 4) | Out-Null; Start-Sleep 1; [SW]::MoveWindow($h, 100, 100, 1600, 900, $true) | Out-Null; Start-Sleep 2
  $dlg = (Windows $p.Id | Where-Object { $_.T -eq 'Conexión' } | Select-Object -First 1)
  if ($dlg) { [SW]::ShowWindowAsync($dlg.H, 4) | Out-Null; Start-Sleep 1; Shot $dlg.H "$out\02-editor.png" }
  Shot $h "$out\02-arbol.png"
  Stop-Process -Id $p.Id -Force; Start-Sleep 1

  $p = Start-Process $exe -ArgumentList '--size','1600x900','--edit','"Terminal Server"','--edit-tab','3' -WindowStyle Minimized -PassThru
  Start-Sleep 6
  $h = $p.MainWindowHandle; [SW]::ShowWindowAsync($h, 4) | Out-Null; Start-Sleep 1; [SW]::MoveWindow($h, 100, 100, 1600, 900, $true) | Out-Null; Start-Sleep 2
  $dlg = (Windows $p.Id | Where-Object { $_.T -eq 'Conexión' } | Select-Object -First 1)
  if ($dlg) { [SW]::ShowWindowAsync($dlg.H, 4) | Out-Null; Start-Sleep 1; Shot $dlg.H "$out\03-editor.png" }
  Stop-Process -Id $p.Id -Force
}
finally {
  Copy-Item "$env:TEMP\rc_demo\connections.real.json" $store -Force
  Copy-Item "$env:TEMP\rc_demo\settings.real.json" $settings -Force
  Stop-Process -Id $srv.Id -Force -ErrorAction SilentlyContinue
  "restaurado"
}
