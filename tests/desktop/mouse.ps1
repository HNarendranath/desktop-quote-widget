param([string]$Exe, [string]$Root)
# Real mouse input on the widget, floated on top so clicks can only land on it. Restores the cursor.
# Uses the same window styles and click handling as the pinned widget. Never clicks Copy.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class M {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", EntryPoint="PostMessageW")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static IntPtr MenuOf(uint procId) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { var s = new StringBuilder(32); GetClassName(h, s, 32); uint p; GetWindowThreadProcessId(h, out p);
      if (s.ToString() == "#32768" && p == procId && IsWindowVisible(h)) { found = h; return false; } return true; }, IntPtr.Zero);
    return found; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
}
"@
[M]::SetProcessDPIAware() | Out-Null
$script:pass=0; $script:fail=0
function Check($n,[bool]$ok,$d="") { if ($ok) { $script:pass++; "PASS  $n" } else { $script:fail++; "FAIL  $n  $d" } }
$utf8 = New-Object Text.UTF8Encoding $false
$future = (Get-Date).AddHours(5).ToString("o")
$quiet = '"zenquotes":{"lastAttemptAt":"F","nextAttemptAt":"F"},"stoic":{"lastAttemptAt":"F","nextAttemptAt":"F"},"quotable":{"lastAttemptAt":"F","nextAttemptAt":"F"},"favqs":{"lastAttemptAt":"F","nextAttemptAt":"F"}'.Replace('F',$future)
function Rect($h) { $r=New-Object M+RECT; [M]::GetWindowRect($h,[ref]$r) | Out-Null; $r }
function Hwnd($id) { $script:w=[IntPtr]::Zero; $cb=[M+EnumProc]{ param($h,$x) $i=0; [M]::GetWindowThreadProcessId($h,[ref]$i) | Out-Null
  if ($i -eq $id -and [M]::IsWindowVisible($h)) { $r=Rect $h; if (($r.R-$r.L) -gt 100) { $script:w=$h; return $false } }; return $true }; [M]::EnumWindows($cb,[IntPtr]::Zero) | Out-Null; $script:w }
function Button($h,$id) { $el=[Windows.Automation.AutomationElement]::FromHandle($h); $el.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),$id)) }
function ClickButton($h,$id) { $b=(Button $h $id).Current.BoundingRectangle; [M]::SetCursorPos([int]($b.Left+$b.Width/2),[int]($b.Top+$b.Height/2)) | Out-Null; Start-Sleep -Milliseconds 350
  [M]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [M]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 500 }
function Own($d) { $f=Join-Path $d 'my-quotes.json'; if (-not (Test-Path $f)) { return ,@() }; $t=[IO.File]::ReadAllText($f,[Text.Encoding]::UTF8).Trim(); if ($t -eq '[]') { return ,@() }; $parsed = ConvertFrom-Json $t; return ,@($parsed | ForEach-Object { $_ }) }
function Drag([int]$fromX,[int]$fromY,[int]$dx,[int]$dy) {
  [M]::SetCursorPos($fromX,$fromY) | Out-Null; Start-Sleep -Milliseconds 300
  [M]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 100
  foreach ($s in 1..12) { [M]::SetCursorPos($fromX+[int]($dx*$s/12),$fromY+[int]($dy*$s/12)) | Out-Null; Start-Sleep -Milliseconds 25 }
  Start-Sleep -Milliseconds 100; [M]::mouse_event(4,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 500 }

$orig = New-Object M+POINT; [M]::GetCursorPos([ref]$orig) | Out-Null
$fg = [M]::GetForegroundWindow()
try {
foreach ($look in 'BareText','Frosted') {
  "--- $look"
  $d = Join-Path $Root "mouse-$look"; if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory $d | Out-Null
  [IO.File]::WriteAllText("$d\settings.json","{`"look`":`"$look`",`"ownShare`":0}",$utf8)
  $q='{"text":"A quote from somewhere online, for testing the star button.","author":"An Author","source":"zenquotes"}'
  [IO.File]::WriteAllText("$d\cache.json",'[' + $q + ',{"text":"Another online quote.","author":"Other","source":"zenquotes"}]',$utf8)
  [IO.File]::WriteAllText("$d\state.json",'{"rotation":{"current":' + $q + ',"nextChangeAt":"' + $future + '","seen":[]},"sources":{' + $quiet + '}}',$utf8)
  $p = Start-Process -FilePath $Exe -ArgumentList '--debug-float','--data-dir',$d -PassThru
  try {
    Start-Sleep -Seconds 3
    $h = Hwnd $p.Id
    $fg = [M]::GetForegroundWindow()
    $r0 = Rect $h
    $cx0 = ($r0.L+$r0.R)/2; $cy0 = ($r0.T+$r0.B)/2

    # Grab the quote text itself, as a person would.
    Drag ([int]($r0.L+($r0.R-$r0.L)*0.3)) ([int]($r0.T+($r0.B-$r0.T)*0.25)) -300 -200
    $r1 = Rect $h
    Check "dragging moves it by the distance dragged" (($r1.L -eq $r0.L-300) -and ($r1.T -eq $r0.T-200)) "moved $($r1.L-$r0.L),$($r1.T-$r0.T)"
    Check "dragging doesn't take focus" ([M]::GetForegroundWindow() -eq $fg)
    $s = [IO.File]::ReadAllText("$d\settings.json") | ConvertFrom-Json
    $saved = $s.centers.PSObject.Properties | Select-Object -First 1
    Check "position is saved after a drag" ($null -ne $saved)

    # A real hover should reveal the buttons, then a real click stars it.
    $sx=[int]($r1.L+($r1.R-$r1.L)*0.3); $sy=[int]($r1.T+($r1.B-$r1.T)*0.25); [M]::SetCursorPos($sx,$sy) | Out-Null; Start-Sleep -Milliseconds 500
    $b = New-Object System.Drawing.Bitmap ($r1.R-$r1.L),($r1.B-$r1.T); $g=[System.Drawing.Graphics]::FromImage($b)
    [M]::SetCursorPos($r1.L+5,$r1.T+5) | Out-Null; Start-Sleep -Milliseconds 400
    $g.CopyFromScreen($r1.L,$r1.T,0,0,(New-Object System.Drawing.Size ($r1.R-$r1.L),($r1.B-$r1.T))); $b.Save((Join-Path $Root "hover-$look.png")); $g.Dispose(); $b.Dispose()

    ClickButton $h 'StarButton'
    $own = Own $d
    Check "real click on the star saves it as starred" ((@($own).Count -eq 1) -and ($own[0].source -eq 'starred'))
    Check "clicking the star doesn't take focus" ([M]::GetForegroundWindow() -eq $fg)
    ClickButton $h 'StarButton'
    Check "real click on a filled star removes it" (([IO.File]::ReadAllText("$d\my-quotes.json")).Trim() -eq '[]')

    $before = ([IO.File]::ReadAllText("$d\state.json",[Text.Encoding]::UTF8) | ConvertFrom-Json).rotation.current.text
    ClickButton $h 'NextButton'; Start-Sleep -Milliseconds 500
    $after = ([IO.File]::ReadAllText("$d\state.json",[Text.Encoding]::UTF8) | ConvertFrom-Json).rotation.current.text
    Check "real click on Next changes the quote" ($before -ne $after)

    $r2 = Rect $h
    Check "clicking a button doesn't move the widget" ([Math]::Abs(($r2.L+$r2.R)/2 - ($r1.L+$r1.R)/2) -le 1)

    ClickButton $h 'HoldButton'
    $menu = [M]::MenuOf([uint32]$p.Id)
    Check "real click on Hold opens its menu" ($menu -ne [IntPtr]::Zero)
    if ($menu -ne [IntPtr]::Zero) {
      [M]::PostMessage($menu,0x100,[IntPtr]0x28,[IntPtr]0) | Out-Null; Start-Sleep -Milliseconds 150  # Down to the first item
      [M]::PostMessage($menu,0x100,[IntPtr]0x0D,[IntPtr]0) | Out-Null; Start-Sleep -Milliseconds 700  # Enter: For 1 hour
      $st = ([IO.File]::ReadAllText("$d\state.json",[Text.Encoding]::UTF8) | ConvertFrom-Json).rotation
      Check "choosing a hold duration holds the quote" ($st.held -and ([DateTimeOffset]$st.heldUntil -gt (Get-Date).AddMinutes(50)))
      Check "pin button is filled while held" ((Button $h 'HoldButton').Current.Name -eq [string][char]0xE842)
    }
    Check "focus is back where it started" ([M]::GetForegroundWindow() -eq $fg)

    # Dropping it past the bottom of the screen must pull it back above the taskbar.
    $r3 = Rect $h
    Drag ([int]($r3.L+($r3.R-$r3.L)*0.3)) ([int]($r3.T+($r3.B-$r3.T)*0.25)) 0 3000
    $r4 = Rect $h
    Add-Type -AssemblyName System.Windows.Forms
    $wa = [System.Windows.Forms.Screen]::FromHandle($h).WorkingArea
    Check "dropping below the screen snaps back above the taskbar" ($r4.B -le $wa.Bottom) "bottom $($r4.B) vs $($wa.Bottom)"
    if (Test-Path "$d\error.log") { "ERROR LOG:"; Get-Content "$d\error.log" | Select-Object -First 8; $script:fail++ }
  } finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
}

"--- Locked position"
$d = Join-Path $Root "mouse-locked"; if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory $d | Out-Null
[IO.File]::WriteAllText("$d\settings.json",'{"positionLocked":true,"ownShare":0}',$utf8)
[IO.File]::WriteAllText("$d\cache.json",'[{"text":"A locked quote that should stay exactly where it is.","author":"An Author","source":"zenquotes"},{"text":"Another online quote.","author":"Other","source":"zenquotes"}]',$utf8)
[IO.File]::WriteAllText("$d\state.json",'{"rotation":{"nextChangeAt":"' + $future + '","seen":[]},"sources":{' + $quiet + '}}',$utf8)
$p = Start-Process -FilePath $Exe -ArgumentList '--debug-float','--data-dir',$d -PassThru
try {
  Start-Sleep -Seconds 3
  $h = Hwnd $p.Id
  $r0 = Rect $h
  Drag ([int]($r0.L+($r0.R-$r0.L)*0.3)) ([int]($r0.T+($r0.B-$r0.T)*0.25)) -300 -200
  $r1 = Rect $h
  Check "dragging doesn't move a locked widget" (($r1.L -eq $r0.L) -and ($r1.T -eq $r0.T)) "moved $($r1.L-$r0.L),$($r1.T-$r0.T)"
  $before = ([IO.File]::ReadAllText("$d\state.json",[Text.Encoding]::UTF8) | ConvertFrom-Json).rotation.current.text
  ClickButton $h 'NextButton'; Start-Sleep -Milliseconds 500
  $after = ([IO.File]::ReadAllText("$d\state.json",[Text.Encoding]::UTF8) | ConvertFrom-Json).rotation.current.text
  Check "buttons still work while locked" ($before -ne $after)
} finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
} finally {
  # Always let go of the button and put the cursor back, even if a step above failed.
  [M]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
  [M]::SetCursorPos($orig.X,$orig.Y) | Out-Null
}
""
"RESULT: $script:pass passed, $script:fail failed"
