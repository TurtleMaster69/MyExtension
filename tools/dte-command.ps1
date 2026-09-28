# tools/dte-command.ps1 — connects to a running Visual Studio instance by PID and executes a DTE
# command (e.g. File.OpenProject, File.OpenFile). Used to open a real solution in the
# Experimental Instance after VS's Run deploys it.
#
# Usage:  pwsh tools/dte-command.ps1 -DevenvPid <pid> -Command 'File.OpenProject' [-Arg 'path']
param(
    [Parameter(Mandatory=$true)][int]$DevenvPid,
    [Parameter(Mandatory=$true)][string]$Command,
    [string]$Arg
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'harness-common.ps1')
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
    try { $doc = $dte.ActiveDocument } catch { $doc = $null }
    if ($doc) { Write-Output $doc.FullName } else { Write-Output '' }
    exit 0
}

# 'Solution.Open' opens a solution file directly via dte.Solution.Open — no file-picker dialog
# (ExecuteCommand('File.OpenProject', path) opens the dialog instead). Use this for opening the
# scratch .sln in the Experimental Instance.
if ($Command -eq 'Solution.Open' -and $Arg) {
    $dte.Solution.Open($Arg)
    Write-Output "Opened solution '$Arg' on PID $DevenvPid"
}
elseif ($Arg) { $dte.ExecuteCommand($Command, $Arg) }
else { $dte.ExecuteCommand($Command) }
if ($Command -ne 'Solution.Open') { Write-Output "Executed '$Command' (arg='$Arg') on PID $DevenvPid" }