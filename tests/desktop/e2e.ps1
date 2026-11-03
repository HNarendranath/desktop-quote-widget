param([string]$Exe, [string]$Root)
# End-to-end checks with the real pinned widget and throwaway data folders.
# Never minimises other windows, and never touches the clipboard.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class E {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint c);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string c, string n);
  public static IntPtr ByTitle(string t) { return FindWindow(null, t); }
  public static IntPtr ByClass(string c) { return FindWindow(c, null); }
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
  [DllImport("user32.dll", EntryPoint="PostMessageW")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  public static IntPtr MenuOf(uint procId) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      var s = new StringBuilder(32); GetClassName(h, s, 32);
      uint p; GetWindowThreadProcessId(h, out p);
      if (s.ToString() == "#32768" && p == procId && IsWindowVisible(h)) { found = h; return false; }
      return true; }, IntPtr.Zero);
    return found;
  }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
"@
[E]::SetProcessDPIAware() | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding $false
$script:pass = 0; $script:fail = 0
function Check($name, [bool]$ok, $detail = "") {
  if ($ok) { $script:pass++; "PASS  $name" } else { $script:fail++; "FAIL  $name  $detail" }
}
function NewDir($name) { $d = Join-Path $Root "e2e-$name"; if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory $d | Out-Null; $d }
function Put($dir, $file, $text) { [IO.File]::WriteAllText((Join-Path $dir $file), $text, $utf8) }
function State($dir) { [IO.File]::ReadAllText((Join-Path $dir 'state.json'), [Text.Encoding]::UTF8) | ConvertFrom-Json }
function Launch($dir, [string[]]$extra = @()) { Start-Process -FilePath $Exe -ArgumentList (@('--data-dir',$dir) + $extra) -PassThru }
function Stop($p) { if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force; $p.WaitForExit(3000) | Out-Null } }
function WidgetHwnd($procId) {
  $script:w = [IntPtr]::Zero
  $cb = [E+EnumProc]{ param($h,$x)
    $i = 0; [E]::GetWindowThreadProcessId($h,[ref]$i) | Out-Null
    if ($i -eq $procId -and [E]::IsWindowVisible($h)) {
      $r = New-Object E+RECT; [E]::GetWindowRect($h,[ref]$r) | Out-Null
      if (($r.R-$r.L) -gt 100) { $script:w = $h; return $false } }
    return $true }
  [E]::EnumWindows($cb,[IntPtr]::Zero) | Out-Null
  $script:w
}
function ExStyle($h) { [E]::GetWindowLongPtr($h,-20).ToInt64() }
function IsAboveDesktopOnly($h) {
  # True when the only windows beneath it are IME helpers and Progman, the desktop.
  $b = [E]::GetWindow($h,2); $n = 0
  while ($b -ne [IntPtr]::Zero -and $n -lt 12) {
    $s = New-Object System.Text.StringBuilder 128; [E]::GetClassName($b,$s,128) | Out-Null
    if ($s.ToString() -eq 'Progman') { return $true }
    if ($s.ToString() -notin 'IME','MSCTFIME UI','GDI+ Hook Window Class') { return $false }
    $b = [E]::GetWindow($b,2); $n++ }
  return $false
}
function Reload { [E]::PostMessage([E]::ByTitle('WindowsQuotes.Host'),[E]::RegisterWindowMessage('WindowsQuotes.Reload'),[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 900 }
function Invoke-Button($h, $id) {
  $el = [Windows.Automation.AutomationElement]::FromHandle($h)
  $c = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty), $id
  $b = $el.FindFirst([Windows.Automation.TreeScope]::Descendants, $c)
  if (-not $b) { throw "button $id not found" }
  $b.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function PrivateMB($procId) { (Get-CimInstance Win32_PerfFormattedData_PerfProc_Process -Filter "IDProcess=$procId").WorkingSetPrivate / 1MB }

$future = (Get-Date).AddHours(5).ToString("o")
# Marks every source as recently tried, so no test hits the network unless it wants to.
$quietSources = '"zenquotes":{"lastAttemptAt":"F","nextAttemptAt":"F"},"stoic":{"lastAttemptAt":"F","nextAttemptAt":"F"},"quotable":{"lastAttemptAt":"F","nextAttemptAt":"F"},"favqs":{"lastAttemptAt":"F","nextAttemptAt":"F"}'.Replace('F',$future)

"--- Pinning, focus and z-order"
$d = NewDir 'pin'
Put $d 'state.json' ('{"rotation":{"seen":[]},"sources":{' + $quietSources + '}}')
$fg = [E]::GetForegroundWindow()
$p = Launch $d; Start-Sleep -Seconds 3
try {
  $h = WidgetHwnd $p.Id
  Check "window appears" ($h -ne [IntPtr]::Zero)
  Check "launch doesn't take focus" ([E]::GetForegroundWindow() -eq $fg)
  $ex = ExStyle $h
  Check "tool window (no Alt+Tab or taskbar)" (($ex -band 0x80) -ne 0)
  Check "never activates" (($ex -band 0x08000000) -ne 0)
  Check "sits directly above the desktop" (IsAboveDesktopOnly $h)
  # SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE. Asking to raise it must be refused.
  [E]::SetWindowPos($h,[IntPtr]::Zero,0,0,0,0,0x13) | Out-Null; Start-Sleep -Milliseconds 400
  Check "refuses to be raised to the top" (IsAboveDesktopOnly $h)
  [E]::SetWindowPos($h,[IntPtr](-1),0,0,0,0,0x13) | Out-Null; Start-Sleep -Milliseconds 400
  Check "refuses to become topmost" ((IsAboveDesktopOnly $h) -and ((ExStyle $h) -band 0x8) -eq 0)

  "--- Single instance"
  $before = @(Get-Process WindowsQuotes).Count
  $second = Launch $d; $second.WaitForExit(6000) | Out-Null
  Check "second launch exits" ($second.HasExited)
  Start-Sleep -Seconds 4
  $mgr = Get-Process WindowsQuotes | Where-Object { $_.Id -ne $p.Id }
  Check "second launch opens the manager" (@($mgr).Count -eq 1)
  $mgrHandle = [E]::ByTitle('Windows Quotes')
  Check "manager window exists" ($mgrHandle -ne [IntPtr]::Zero)

  "--- Adding a quote in the manager reaches the widget"
  $el = [Windows.Automation.AutomationElement]::FromHandle($mgrHandle)
  $find = { param($id) $el.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),$id)) }
  (& $find 'QuoteInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Typed into the manager.')
  (& $find 'AuthorInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('A Tester')
  Start-Sleep -Milliseconds 300
  (& $find 'SaveButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
  Start-Sleep -Milliseconds 900
  $own = [IO.File]::ReadAllText((Join-Path $d 'my-quotes.json')) | ConvertFrom-Json
  Check "manager saved the quote" (@($own)[0].text -eq 'Typed into the manager.')
  # With only this quote and share 100%, Next must now pick it, proving the widget reloaded its list.
  [IO.File]::WriteAllText((Join-Path $d 'settings.json'), '{"ownShare":1}', $utf8); Reload
  Invoke-Button $h 'NextButton'; Start-Sleep -Milliseconds 900
  Check "widget picked up the new quote without restarting" ((State $d).rotation.current.text -eq 'Typed into the manager.')
  $mgr | Stop-Process -Force
  $mgr | ForEach-Object { $_.WaitForExit(3000) | Out-Null }
  Check "manager process is gone after closing" (@(Get-Process WindowsQuotes | Where-Object { $_.Id -ne $p.Id }).Count -eq 0)

  "--- Hover-button actions"
  [IO.File]::WriteAllText((Join-Path $d 'settings.json'), '{"ownShare":0}', $utf8); Reload
  Invoke-Button $h 'NextButton'; Start-Sleep -Milliseconds 900
  $cur = (State $d).rotation.current
  Check "Next moves to another quote" ($cur.text -ne 'Typed into the manager.' -and $cur.source -eq 'starter')
  Invoke-Button $h 'StarButton'; Start-Sleep -Milliseconds 600
  $raw = [IO.File]::ReadAllText((Join-Path $d 'my-quotes.json'),[Text.Encoding]::UTF8)
  Check "Star saves it as starred" ($raw.Contains($cur.text) -and $raw.Contains('"starred"'))
  Check "Starring keeps self-added quotes separate" ($raw.Contains('Typed into the manager.'))
  Invoke-Button $h 'StarButton'; Start-Sleep -Milliseconds 600
  $raw = [IO.File]::ReadAllText((Join-Path $d 'my-quotes.json'),[Text.Encoding]::UTF8)
  Check "Starring again removes only the star" ((-not $raw.Contains('"starred"')) -and $raw.Contains('Typed into the manager.'))

  "--- Pin request from the manager"
  Put $d 'pin-request.json' '{"quote":{"text":"Pinned from the list.","author":"Me","source":"mine"},"duration":"OneWeek"}'; Reload
  $s = (State $d).rotation
  Check "pin shows the requested quote" ($s.current.text -eq 'Pinned from the list.')
  Check "pin holds it for a week" ($s.held -and ([DateTimeOffset]$s.heldUntil -gt (Get-Date).AddDays(6.9)))
  Check "pin request is consumed" (-not (Test-Path (Join-Path $d 'pin-request.json')))
  $holdButton = [Windows.Automation.AutomationElement]::FromHandle($h).FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),'HoldButton'))
  Check "pin button shows a hold set from the manager" ($holdButton.Current.Name -eq 'Quote held')
  Invoke-Button $h 'NextButton'; Start-Sleep -Milliseconds 900
  $s = (State $d).rotation
  Check "Next ends the hold" ((-not $s.held) -and $s.current.text -ne 'Pinned from the list.')

  "--- Settings changes apply live"
  [IO.File]::WriteAllText((Join-Path $d 'settings.json'), '{"controls":"TrayOnly","ownShare":0}', $utf8); Reload
  Check "tray-only makes clicks pass through" (((ExStyle $h) -band 0x20) -ne 0)
  [IO.File]::WriteAllText((Join-Path $d 'settings.json'), '{"controls":"HoverButtons","ownShare":0}', $utf8); Reload
  Check "hover mode takes clicks again" (((ExStyle $h) -band 0x20) -eq 0)
  foreach ($look in 'Frosted','Tinted','BareText') {
    [IO.File]::WriteAllText((Join-Path $d 'settings.json'), "{`"look`":`"$look`",`"ownShare`":0}", $utf8); Reload
    $h = WidgetHwnd $p.Id
    Check "switch to $look keeps one pinned window" (($h -ne [IntPtr]::Zero) -and (IsAboveDesktopOnly $h))
    $layered = ((ExStyle $h) -band 0x80000) -ne 0
    Check "$look uses the right window type" ($layered -eq ($look -ne 'Frosted'))
  }

  "--- Font and lowercase apply live"
  [IO.File]::WriteAllText((Join-Path $d 'settings.json'), '{"font":"Great Vibes","lowercase":true,"ownShare":0}', $utf8); Reload
  Start-Sleep -Milliseconds 500
  $line = [Windows.Automation.AutomationElement]::FromHandle($h).FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),'QuoteLine'))
  Check "lowercase setting shows the quote in lowercase" ($line.Current.Name -ceq $line.Current.Name.ToLower())
  [IO.File]::WriteAllText((Join-Path $d 'settings.json'), '{"ownShare":0}', $utf8); Reload
  Start-Sleep -Milliseconds 300

  "--- Position"
  $mon = [Windows.Automation.AutomationElement]::RootElement.Current.BoundingRectangle
  $r = New-Object E+RECT; [E]::GetWindowRect($h,[ref]$r) | Out-Null
  Check "default position is on screen" ($r.L -ge $mon.Left -and $r.R -le $mon.Right -and $r.T -ge $mon.Top -and $r.B -le $mon.Bottom)

  "--- Native menus"
  $fgBefore = [E]::GetForegroundWindow()
  $host1 = [E]::ByTitle('WindowsQuotes.Host')
  $before = (State $d).rotation
  $textBefore = $before.current.text
  [E]::PostMessage($host1,0x8001,[IntPtr]::Zero,[IntPtr]0x205) | Out-Null   # tray right-click
  Start-Sleep -Milliseconds 700
  $menu = [E]::MenuOf([uint32]$p.Id)
  Check "tray menu opens" ($menu -ne [IntPtr]::Zero)
  if ($menu -ne [IntPtr]::Zero) {
    $r = New-Object E+RECT; [E]::GetWindowRect($menu,[ref]$r) | Out-Null
    $b = New-Object System.Drawing.Bitmap ($r.R-$r.L),($r.B-$r.T); $g=[System.Drawing.Graphics]::FromImage($b)
    $g.CopyFromScreen($r.L,$r.T,0,0,(New-Object System.Drawing.Size ($r.R-$r.L),($r.B-$r.T))); $b.Save((Join-Path $Root 'tray-menu.png')); $g.Dispose(); $b.Dispose()
    [E]::PostMessage($menu,0x100,[IntPtr]0x28,[IntPtr]0) | Out-Null  # Down, to "Next quote"
    Start-Sleep -Milliseconds 200
    [E]::PostMessage($menu,0x100,[IntPtr]0x0D,[IntPtr]0) | Out-Null  # Enter
    Start-Sleep -Milliseconds 1100
    $afterMenu = (State $d).rotation
    Check "choosing Next quote from the menu works" ($afterMenu.current.text -ne $textBefore) "commandRan=$("$($afterMenu.nextChangeAt)" -ne "$($before.nextChangeAt)") text='$textBefore' seen=$(@($afterMenu.seen).Count) held=$($afterMenu.held)"
    Check "menu closed" (-not [E]::IsWindowVisible($menu))
    Check "focus returned to the previous app" ([E]::GetForegroundWindow() -eq $fgBefore)
  }

  "--- Memory under stress (300 quote changes)"
  Start-Sleep -Seconds 6
  $m0 = PrivateMB $p.Id
  for ($i = 0; $i -lt 300; $i++) { Invoke-Button $h 'NextButton'; Start-Sleep -Milliseconds 40 }
  Start-Sleep -Seconds 7
  $m1 = PrivateMB $p.Id
  for ($i = 0; $i -lt 300; $i++) { Invoke-Button $h 'NextButton'; Start-Sleep -Milliseconds 40 }
  Start-Sleep -Seconds 7
  $m2 = PrivateMB $p.Id
  "  private working set: start {0:N1} MB, after 300 {1:N1} MB, after 600 {2:N1} MB" -f $m0,$m1,$m2
  Check "no runaway growth between batches" (($m2 - $m1) -lt 8) "grew $($m2-$m1) MB"
  Check "no errors logged" (-not (Test-Path (Join-Path $d 'error.log'))) "see $d\error.log"
} finally { Stop $p; Get-Process WindowsQuotes -ErrorAction SilentlyContinue | Stop-Process -Force }

"--- The centre stays fixed as quotes change size"
$d = NewDir 'center'
$long = ('A very long quote that wraps across many lines so the widget grows a great deal taller. ' * 3).Trim()
Put $d 'my-quotes.json' ('[{"text":"Short.","author":"A","source":"mine"},{"text":"' + $long + '","author":"B","source":"mine"},{"text":"A quote of a medium sort of length, enough to wrap onto a second line of text.","author":"C","source":"mine"}]')
Put $d 'settings.json' '{"ownShare":1,"monitor":null,"centers":{"\\\\.\\DISPLAY1":{"x":1200,"y":800}}}'
Put $d 'state.json' ('{"rotation":{"current":{"text":"Short.","author":"A","source":"mine"},"nextChangeAt":"' + $future + '","seen":[]},"sources":{' + $quietSources + '}}')
$p = Launch $d; Start-Sleep -Seconds 3
try {
  $h = WidgetHwnd $p.Id
  $cxs = @{}; $cys = @{}; $heights = @{}
  foreach ($i in 1..9) {
    Invoke-Button $h 'NextButton'; Start-Sleep -Milliseconds 700
    $r = New-Object E+RECT; [E]::GetWindowRect($h,[ref]$r) | Out-Null
    $cxs[[int](($r.L+$r.R)/2)] = 1; $cys[[int](($r.T+$r.B)/2)] = 1; $heights[$r.B-$r.T] = 1
  }
  Check "several quote heights were exercised" ($heights.Count -ge 2) "heights: $($heights.Keys -join ',')"
  $ySpread = ($cys.Keys | Measure-Object -Maximum).Maximum - ($cys.Keys | Measure-Object -Minimum).Minimum
  $xSpread = ($cxs.Keys | Measure-Object -Maximum).Maximum - ($cxs.Keys | Measure-Object -Minimum).Minimum
  Check "horizontal centre doesn't move (within rounding)" ($xSpread -le 1) "x: $($cxs.Keys -join ',')"
  Check "vertical centre doesn't move (within rounding)" ($ySpread -le 1) "y: $($cys.Keys -join ',')"
} finally { Stop $p }

"--- Manager: added and starred are separate"
$d = NewDir 'tabs'
Put $d 'my-quotes.json' '[{"text":"One I typed myself.","author":"Me","source":"mine"},{"text":"Another I typed.","author":"","source":"mine"},{"text":"One I starred from the widget.","author":"Someone","source":"starred"}]'
$p = Launch $d @('--manager'); Start-Sleep -Seconds 4
try {
  $mh = [E]::ByTitle('Windows Quotes')
  $el = [Windows.Automation.AutomationElement]::FromHandle($mh)
  $byId = { param($id) $el.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),$id)) }
  $items = { (& $byId 'QuoteList').FindAll([Windows.Automation.TreeScope]::Children,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::ListItem)) }
  Check "Added tab lists only typed quotes" ((& $items).Count -eq 2)
  Check "Added tab header shows its count" ((& $byId 'AddedView').Current.Name -match '2')
  (& $byId 'StarredView').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 500
  Check "Starred tab lists only starred quotes" ((& $items).Count -eq 1)
  Check "Starred tab hides the add form" ((& $byId 'SaveButton').Current.IsOffscreen -or $null -eq (& $byId 'SaveButton'))
  (& $items)[0].GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300
  Add-Type -AssemblyName System.Drawing
  $r = New-Object E+RECT; [E]::GetWindowRect($mh,[ref]$r) | Out-Null
  $b = New-Object System.Drawing.Bitmap ($r.R-$r.L),($r.B-$r.T); $g=[System.Drawing.Graphics]::FromImage($b)
  $g.CopyFromScreen($r.L,$r.T,0,0,(New-Object System.Drawing.Size ($r.R-$r.L),($r.B-$r.T))); $b.Save((Join-Path $Root 'manager-starred.png')); $g.Dispose(); $b.Dispose()
  (& $byId 'DeleteButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 500
  $raw = [IO.File]::ReadAllText((Join-Path $d 'my-quotes.json'))
  Check "Remove star removes only that quote" ((-not $raw.Contains('starred')) -and $raw.Contains('One I typed myself.'))
  (& $byId 'AddedView').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 400
  Check "Added tab still has its quotes" ((& $items).Count -eq 2)
  (& $byId 'QuoteInput').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Added after switching back.'); Start-Sleep -Milliseconds 200
  (& $byId 'SaveButton').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 500
  $raw = [IO.File]::ReadAllText((Join-Path $d 'my-quotes.json'))
  Check "New quotes go in Added" ($raw.Contains('Added after switching back.') -and -not $raw.Contains('"starred"'))
  Check "manager logged no errors" (-not (Test-Path (Join-Path $d 'error.log')))
} finally { Stop $p }

"--- Holds survive restarts"
$d = NewDir 'hold'
$quote = '{"text":"A held quote.","author":"Holder","source":"mine"}'
Put $d 'state.json' ('{"rotation":{"current":' + $quote + ',"held":true,"heldUntil":null,"nextChangeAt":"2020-01-01T00:00:00+00:00","seen":[]},"sources":{' + $quietSources + '}}')
$p = Launch $d; Start-Sleep -Seconds 3; Stop $p
$s = (State $d).rotation
Check "indefinite hold survives a restart" ($s.held -and $s.current.text -eq 'A held quote.')

Put $d 'state.json' ('{"rotation":{"current":' + $quote + ',"held":true,"heldUntil":"2020-01-01T00:00:00+00:00","nextChangeAt":"2020-01-01T00:00:00+00:00","seen":[]},"sources":{' + $quietSources + '}}')
$p = Launch $d; Start-Sleep -Seconds 3; Stop $p
$s = (State $d).rotation
Check "hold that expired while off ends at launch" ((-not $s.held) -and $s.current.text -ne 'A held quote.')

$soon = (Get-Date).AddSeconds(5).ToString("o")
Put $d 'state.json' ('{"rotation":{"current":' + $quote + ',"held":true,"heldUntil":"' + $soon + '","nextChangeAt":"' + $future + '","seen":[]},"sources":{' + $quietSources + '}}')
$p = Launch $d; Start-Sleep -Seconds 2
$s = (State $d).rotation
Check "timed hold still applies before it ends" ($s.held -and $s.current.text -eq 'A held quote.')
Start-Sleep -Seconds 6
$s = (State $d).rotation
Check "timed hold ends on schedule and moves on" ((-not $s.held) -and $s.current.text -ne 'A held quote.')
Stop $p

"--- Rotation on schedule"
$d = NewDir 'rotate'
$soon = (Get-Date).AddSeconds(4).ToString("o")
Put $d 'state.json' ('{"rotation":{"current":' + $quote + ',"held":false,"nextChangeAt":"' + $soon + '","seen":[]},"sources":{' + $quietSources + '}}')
$p = Launch $d; Start-Sleep -Seconds 2
Check "quote stays until it's due" ((State $d).rotation.current.text -eq 'A held quote.')
Start-Sleep -Seconds 5
Check "quote changes when due" ((State $d).rotation.current.text -ne 'A held quote.')
Stop $p

"--- Offline: all sources fail, widget carries on"
$d = NewDir 'offline'
$env:HTTPS_PROXY = 'http://127.0.0.1:9'; $env:HTTP_PROXY = 'http://127.0.0.1:9'
try { $p = Launch $d } finally { Remove-Item Env:HTTPS_PROXY; Remove-Item Env:HTTP_PROXY }
Start-Sleep -Seconds 14
try {
  $h = WidgetHwnd $p.Id
  Check "still running with no network" (-not $p.HasExited)
  $s = State $d
  Check "shows a starter quote" ($s.rotation.current.source -eq 'starter')
  $names = $s.sources.PSObject.Properties
  Check "each source recorded its failure" (@($names).Count -eq 4 -and @($names | Where-Object { $_.Value.lastError }).Count -eq 4)
  Check "failures back off rather than retry at once" (@($names | Where-Object { [DateTimeOffset]$_.Value.nextAttemptAt -gt (Get-Date).AddMinutes(30) }).Count -eq 4)
  Invoke-Button $h 'NextButton'; Start-Sleep -Milliseconds 900
  Check "Next still works offline" ((State $d).rotation.current.source -eq 'starter')
  Check "no errors logged offline" (-not (Test-Path (Join-Path $d 'error.log')))
} finally { Stop $p }

"--- Corrupt data"
$d = NewDir 'corrupt'
foreach ($f in 'settings.json','state.json','my-quotes.json','cache.json') { Put $d $f '{ not json' }
$p = Launch $d; Start-Sleep -Seconds 3
Check "starts despite corrupt files" (-not $p.HasExited)
Check "corrupt state is set aside" (Test-Path (Join-Path $d 'state.json.corrupt'))
Stop $p

""
"RESULT: $script:pass passed, $script:fail failed"
