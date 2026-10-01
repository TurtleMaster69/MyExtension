# tools/harness/dte-command.ps1 — connects to a running Visual Studio instance by PID and executes a DTE
# command (e.g. File.OpenProject, File.OpenFile). Used to open a real solution in the
# Experimental Instance after VS's Run deploys it.
#
# Usage:  pwsh tools/harness/dte-command.ps1 -DevenvPid <pid> -Command 'File.OpenProject' [-Arg 'path']
param(
    [int]$DevenvPid,
    [string]$Command,
    [string]$Arg,
    [int]$TimeoutSec = 90,
    [int]$QueryTimeoutSec = 5,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'harness-common.ps1')

if (-not $SelfTest) {
    if (-not $DevenvPid) { throw 'DevenvPid is required (unless -SelfTest).' }
    if (-not $Command) { throw 'Command is required (unless -SelfTest).' }
}

function Invoke-DteWithTimeout([scriptblock]$Block, [int]$TimeoutSec, [string]$What, [object[]]$Parameters = @()) {
    # M19: run a DTE COM call in a background runspace so a hung COM call cannot block the harness
    # forever. A checkpoint stopwatch (Assert-Budget) cannot interrupt a blocking COM call, so this
    # is a bounded wait: BeginInvoke + AsyncWaitHandle.WaitOne($TimeoutSec * 1000). On timeout the
    # runspace is stopped and a terminating error is thrown (with $ErrorActionPreference='Stop' this
    # terminates the script with a non-zero exit so the parent's teardown runs). The scriptblock
    # receives $dte/$Command/$Arg via AddParameter (a fresh runspace has no caller scope).
    $ps = [powershell]::Create()
    try {
        $ps.AddScript($Block.ToString()) | Out-Null
        foreach ($p in $Parameters) { $ps.AddParameter($p) | Out-Null }
        $handle = $ps.BeginInvoke()
        if (-not $handle.AsyncWaitHandle.WaitOne($TimeoutSec * 1000)) {
            $ps.Stop()
            throw "dte-command timed out after $TimeoutSec s: $What"
        }
        $output = $ps.EndInvoke($handle)
        if ($ps.HadErrors) {
            $err = $ps.Streams.Error | Select-Object -First 1
            if ($err) { throw $err.ToString() }
        }
        return $output
    } finally {
        $ps.Dispose()
    }
}

if ($SelfTest) {
    # No-VS seam (M19): exercise Invoke-DteWithTimeout against deliberately-blocking stubs. Must not
    # touch VS/DTE — this block runs before Resolve-VsRoot / GetForProcess.
    try {
        Write-Output 'SelfTest: exercising Invoke-DteWithTimeout against blocking stubs (no VS/DTE)'
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $caught = $null
        try { Invoke-DteWithTimeout { Start-Sleep -Seconds 30 } 2 'blocking stub' } catch { $caught = $_.Exception.Message }
        if (-not $caught) { throw 'SelfTest: blocking stub did not abort' }
        Write-Output "SelfTest: $caught"
        if ($sw.Elapsed.TotalSeconds -gt 10) { throw "SelfTest: blocking stub aborted too late ($($sw.Elapsed.TotalSeconds)s)" }

        $caught = $null
        try { Invoke-DteWithTimeout { Start-Sleep -Seconds 5 } 1 'short stub' } catch { $caught = $_.Exception.Message }
        if (-not $caught) { throw 'SelfTest: short-timeout stub did not abort' }
        Write-Output "SelfTest: $caught"

        Write-Output 'SELFTEST PASS'
        exit 0
    } catch {
        Write-Output "SELFTEST FAIL: $($_.Exception.Message)"
        exit 1
    }
}

$vsRoot = Resolve-VsRoot
$pub = Join-Path $vsRoot 'Common7\IDE\PublicAssemblies'
if (-not (Test-Path (Join-Path $pub 'envdte.dll'))) { throw 'Could not locate VS PublicAssemblies.' }

foreach ($a in 'envdte.dll','envdte80.dll','Microsoft.VisualStudio.Interop.dll') {
    try { [System.Reflection.Assembly]::LoadFrom((Join-Path $pub $a)) | Out-Null } catch { }
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using EnvDTE80;

public static class DteCmd
{
    [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable rot);
    [DllImport("ole32.dll")] static extern int CreateBindCtx(int reserved, out IBindCtx ctx);

    public static DTE2 GetForProcess(int pid)
    {
        IRunningObjectTable rot;
        GetRunningObjectTable(0, out rot);
        IEnumMoniker em;
        rot.EnumRunning(out em);
        IMoniker[] m = new IMoniker[1];
        IntPtr fetched = IntPtr.Zero;
        while (em.Next(1, m, fetched) == 0)
        {
            IBindCtx ctx;
            CreateBindCtx(0, out ctx);
            string name;
            m[0].GetDisplayName(ctx, null, out name);
            if (name != null && name.IndexOf("VisualStudio.DTE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                object obj;
                rot.GetObject(m[0], out obj);
                try {
                    var dte = obj as DTE2;
                    if (dte != null && dte.MainWindow != null)
                    {
                        IntPtr hwnd = new IntPtr(dte.MainWindow.HWnd);
                        uint winPid;
                        GetWindowThreadProcessId(hwnd, out winPid);
                        if ((int)winPid == pid) return dte;
                    }
                } catch { }
            }
        }
        return null;
    }

    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
'@ -ReferencedAssemblies 'mscorlib','System','System.Runtime','System.Collections',(Join-Path $pub 'envdte.dll'), (Join-Path $pub 'envdte80.dll'), (Join-Path $pub 'Microsoft.VisualStudio.Interop.dll')

$dte = [DteCmd]::GetForProcess($DevenvPid)
if (-not $dte) { throw "Could not connect DTE to devenv PID $DevenvPid." }

# 'GetActiveDocument' is a HARNESS-ONLY QUERY (it executes no VS command): it prints the FULL PATH
# of the currently active document, or an empty line when none is active. The Solution Explorer
# `o`/Enter open scenarios use it to prove the selected item opened even when VS REUSES an
# already-open tab — that path raises no `editor-view-opened` diagnostic (which only fires for a
# newly created text view). No product code or diagnostic is touched.
if ($Command -eq 'GetActiveDocument') {
    $doc = $null
    try {
        $doc = Invoke-DteWithTimeout { param($dte) $dte.ActiveDocument } $QueryTimeoutSec 'GetActiveDocument' @($dte)
    } catch { $doc = $null }
    if ($doc) { Write-Output $doc.FullName } else { Write-Output '' }
    exit 0
}

# 'Solution.Open' opens a solution file directly via dte.Solution.Open — no file-picker dialog
# (ExecuteCommand('File.OpenProject', path) opens the dialog instead). Use this for opening the
# scratch .sln in the Experimental Instance.
if ($Command -eq 'Solution.Open' -and $Arg) {
    Invoke-DteWithTimeout { param($dte, $arg) $dte.Solution.Open($arg) } $TimeoutSec "Solution.Open '$Arg'" @($dte, $Arg) | Out-Null
    Write-Output "Opened solution '$Arg' on PID $DevenvPid"
}
elseif ($Arg) {
    Invoke-DteWithTimeout { param($dte, $command, $arg) $dte.ExecuteCommand($command, $arg) } $TimeoutSec "ExecuteCommand '$Command'" @($dte, $Command, $Arg) | Out-Null
}
else {
    Invoke-DteWithTimeout { param($dte, $command) $dte.ExecuteCommand($command) } $TimeoutSec "ExecuteCommand '$Command'" @($dte, $Command) | Out-Null
}
if ($Command -ne 'Solution.Open') { Write-Output "Executed '$Command' (arg='$Arg') on PID $DevenvPid" }