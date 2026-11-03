param([string]$Exe, [string]$Root, [string]$Tag = "new")
# Does a long quote ever go behind the taskbar, and does the centre stay fixed? Pinned, no mouse.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class P {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string c, string n);
  public static IntPtr ByTitle(string t) { return FindWindow(null, t); }
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
  [DllImport("user32.dll", EntryPoint="PostMessageW")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint b, out RECT r, uint c);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
"@
[P]::SetProcessDPIAware() | Out-Null
$work = New-Object P+RECT; [P]::SystemParametersInfo(0x30,0,[ref]$work,0) | Out-Null
"work area bottom = $($work.B)"
$utf8 = New-Object Text.UTF8Encoding $false
$future = (Get-Date).AddHours(5).ToString("o")
$quiet = '"zenquotes":{"lastAttemptAt":"F","nextAttemptAt":"F"},"stoic":{"lastAttemptAt":"F","nextAttemptAt":"F"},"quotable":{"lastAttemptAt":"F","nextAttemptAt":"F"},"favqs":{"lastAttemptAt":"F","nextAttemptAt":"F"}'.Replace('F',$future)
$short = 'Short quote.'
$medium = 'It is not that we have a short time to live, but that we waste a lot of it, and life is long enough if you know how to use it.'
$long = ('A very long quote that wraps across many lines so the widget must grow a great deal taller than usual. ' * 3).Trim() + ' End.'
foreach ($look in 'Frosted','BareText','Tinted') {
  $d = Join-Path $Root "place-$Tag-$look"; if (Test-Path $d) { Remove-Item $d -Recurse -Force }; New-Item -ItemType Directory $d | Out-Null
  [IO.File]::WriteAllText("$d\settings.json", "{`"look`":`"$look`"}", $utf8)
  $q = '{"text":"' + $short + '","author":"A","source":"mine"}'
  # Own quotes of every length, so real animated Next changes switch between sizes.
  $own = @($short,$medium,$long) | ForEach-Object { '{"text":"' + $_ + '","author":"An Author","source":"mine"}' }
  [IO.File]::WriteAllText("$d\my-quotes.json", '[' + ($own -join ',') + ']', $utf8)
  [IO.File]::WriteAllText("$d\settings.json", "{`"look`":`"$look`",`"ownShare`":1}", $utf8)
  [IO.File]::WriteAllText("$d\state.json", '{"rotation":{"current":' + $q + ',"nextChangeAt":"' + $future + '","seen":[]},"sources":{' + $quiet + '}}', $utf8)
  $p = Start-Process -FilePath $Exe -ArgumentList '--data-dir',$d -PassThru
  try {
    Start-Sleep -Seconds 3
    $script:w = [IntPtr]::Zero
    $cb = [P+EnumProc]{ param($h,$x) $i=0; [P]::GetWindowThreadProcessId($h,[ref]$i) | Out-Null
      if ($i -eq $p.Id -and [P]::IsWindowVisible($h)) { $r=New-Object P+RECT; [P]::GetWindowRect($h,[ref]$r) | Out-Null; if (($r.R-$r.L) -gt 100) { $script:w=$h; return $false } }; return $true }
    [P]::EnumWindows($cb,[IntPtr]::Zero) | Out-Null
    $el = [Windows.Automation.AutomationElement]::FromHandle($script:w)
    $btn = $el.FindFirst([Windows.Automation.TreeScope]::Descendants,(New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty),'NextButton'))
    $inv = $btn.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
    $worst = -99999; $heights = @{}; $centers = @{}
    foreach ($i in 1..12) {
      $inv.Invoke()
      # Sample through the fade, since a brief overlap counts too.
      foreach ($j in 1..14) {
        Start-Sleep -Milliseconds 50
        $r = New-Object P+RECT; [P]::GetWindowRect($script:w,[ref]$r) | Out-Null
        $over = $r.B - $work.B
        if ($over -gt $worst) { $worst = $over }
      }
      Start-Sleep -Milliseconds 300
      $r = New-Object P+RECT; [P]::GetWindowRect($script:w,[ref]$r) | Out-Null
      $heights[$r.B-$r.T] = 1; $centers["$([int](($r.L+$r.R)/2))"] = 1
    }
    "{0,-9} heights seen: {1} | furthest bottom edge relative to work area: {2} px (positive = behind the taskbar)" -f $look,(($heights.Keys | Sort-Object) -join ', '),$worst
    if (Test-Path "$d\error.log") { Get-Content "$d\error.log" | Select-Object -First 5 }
  } finally { if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force } }
}
