# ROTORWASH autopilot
#
# Starts a fresh headless Claude Code session to continue building the game, and is
# invoked by a Windows scheduled task rather than by Claude itself.
#
# WHY THIS EXISTS, rather than the in-session scheduler:
#   ScheduleWakeup and session cron are bound to the session that created them. When a
#   usage window runs out, the turn dies mid-flight and any pending wake-up dies with it -
#   so the one situation the restart was meant to cover is exactly the one it cannot.
#   A scheduled task lives outside all of that. It survives the window closing, the
#   session ending, the terminal closing and a reboot.
#
# STOP IT with any of:
#   New-Item C:\repos\heli-rpg\tools\autopilot\STOP     (pauses; delete the file to resume)
#   schtasks /Delete /TN RotorwashAutopilot /F          (removes it entirely)
#
# WATCH IT:
#   Get-Content C:\repos\heli-rpg\tools\autopilot\autopilot.log -Tail 40 -Wait

param([switch]$Verify)

$ErrorActionPreference = 'Stop'

$Repo    = 'C:\repos\heli-rpg'
$Here    = Join-Path $Repo 'tools\autopilot'
$Claude  = Join-Path $env:USERPROFILE '.local\bin\claude.exe'
$Log     = Join-Path $Here 'autopilot.log'
$Lock    = Join-Path $Here 'RUNNING.lock'
$Stop    = Join-Path $Here 'STOP'
$Retry   = Join-Path $Here 'retry-after.txt'
$Transcript = Join-Path $Here 'last-run.txt'

function Write-Log($msg) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
    Add-Content -Path $Log -Value $line -Encoding utf8
}

# --- Plumbing check --------------------------------------------------------
# Everything except actually starting a session, so the setup can be validated without
# burning a run. Used because a nested agent launch cannot be tested from inside one.
if ($Verify) {
    $ok = $true
    function Check($label, $cond) {
        $script:ok = $script:ok -and $cond
        "{0}  {1}" -f $(if ($cond) { 'ok  ' } else { 'FAIL' }), $label
    }
    Check "claude.exe at $Claude"            (Test-Path $Claude)
    Check "repo at $Repo"                    (Test-Path $Repo)
    Check "STATUS.md present"                (Test-Path (Join-Path $Repo 'STATUS.md'))
    Check "decisions.md present"             (Test-Path (Join-Path $Repo 'docs\wiki\decisions.md'))
    Check "godot present"                    (Test-Path (Join-Path $Repo 'tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe'))
    Check "autopilot dir writable"           (Test-Path $Here)
    Check "scheduled task registered"        ((schtasks /Query /TN 'RotorwashAutopilot' 2>&1) -match 'RotorwashAutopilot')
    Check "not currently paused by STOP"     (-not (Test-Path $Stop))
    Check "no stale lock"                    (-not (Test-Path $Lock))
    if ($ok) { "ALL PLUMBING OK" } else { "SOMETHING IS WRONG" }
    exit $(if ($ok) { 0 } else { 1 })
}

# --- Kill switch -----------------------------------------------------------
if (Test-Path $Stop) { exit 0 }

# --- Do not stack runs -----------------------------------------------------
# A build-and-verify cycle can take a long time. If one is still going, leave it alone.
if (Test-Path $Lock) {
    $age = (Get-Date) - (Get-Item $Lock).LastWriteTime
    if ($age.TotalMinutes -lt 180) { exit 0 }
    Write-Log "stale lock ({0:N0} min old), clearing" -f $age.TotalMinutes
    Remove-Item $Lock -Force -ErrorAction SilentlyContinue
}

# --- Respect a usage-limit backoff ----------------------------------------
# When a window is exhausted there is no point burning a run every 15 minutes just to be
# told so again. The previous run records when it is worth trying again.
if (Test-Path $Retry) {
    try {
        $until = [datetime]::Parse((Get-Content $Retry -Raw).Trim())
        if ((Get-Date) -lt $until) { exit 0 }
        Write-Log "backoff expired, resuming"
        Remove-Item $Retry -Force -ErrorAction SilentlyContinue
    } catch { Remove-Item $Retry -Force -ErrorAction SilentlyContinue }
}

if (-not (Test-Path $Claude)) { Write-Log "claude.exe not found at $Claude"; exit 1 }

# --- The brief -------------------------------------------------------------
# Every run is a FRESH session with no memory of the last one, so the prompt has to be
# entirely self-directing and point at the two files that carry the state.
$Prompt = @'
Continue building ROTORWASH, a post-apocalyptic helicopter RPG, in C:\repos\heli-rpg.

Read STATUS.md and docs/wiki/decisions.md FIRST. They are the source of truth, they stay
current, and they tell you what has already been decided and why. Do not re-litigate
settled decisions.

Then take the next item from the "Next" list in STATUS.md and build it properly.

Verify your work, always:
  - anything in sim/    -> dotnet run --project tools/simlab -c Release -- all
  - anything in game/   -> godot --headless --path game -- --selftest   AND   -- --looptest
  - anything visual     -> godot --path game -- --screenshot, then actually LOOK at the
                           images with the Read tool. Most bugs on this project were
                           invisible in the code and obvious in a render.

The Godot binary is tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe

When the item is done: commit with a real message explaining what and why, and update
STATUS.md. Log any design decision in docs/wiki/decisions.md with its reasoning and how
reversible it is.

Fred is away and has authorised "decide and log it" - make the call yourself and record
it. Do not produce playable builds unless asked. Work until the item is genuinely
finished and verified, then stop; a scheduled task will start the next session.
'@

New-Item -ItemType File -Path $Lock -Force | Out-Null
Write-Log "run starting"

$exit = 0
try {
    Push-Location $Repo
    # --dangerously-skip-permissions is required: nothing can answer a prompt in a
    # headless scheduled run, and a run that blocks on one is a run that does nothing.
    & $Claude -p $Prompt `
        --permission-mode bypassPermissions `
        --dangerously-skip-permissions `
        --add-dir $Repo `
        *> $Transcript
    $exit = $LASTEXITCODE
} catch {
    Write-Log ("run threw: " + $_.Exception.Message)
    $exit = 1
} finally {
    Pop-Location
    Remove-Item $Lock -Force -ErrorAction SilentlyContinue
}

# --- Read the tea leaves ---------------------------------------------------
$tail = ''
if (Test-Path $Transcript) {
    $tail = (Get-Content $Transcript -Tail 60 -ErrorAction SilentlyContinue) -join ' '
}

# Claude reports an exhausted window in the output rather than with a distinct exit code,
# so the text is what has to be matched.
$limitHit = $tail -match '(?i)(usage limit|rate limit|limit reached|out of (usage|credit)|resets? at|try again (later|in))'

if ($limitHit) {
    # Usage windows are several hours. Back off well past the next likely reset rather
    # than hammering the API, but not so far that a reopened window sits unused.
    $until = (Get-Date).AddMinutes(45)
    Set-Content -Path $Retry -Value $until.ToString('o') -Encoding utf8
    Write-Log ("usage limit hit; backing off until {0:HH:mm}" -f $until)
} else {
    Write-Log ("run finished, exit {0}" -f $exit)
    # Record what actually changed, so the log is a history of the project and not just
    # a history of the scheduler.
    try {
        Push-Location $Repo
        $head = (& git log -1 --format='%h %s' 2>$null)
        if ($head) { Write-Log ("  head: " + $head) }
        Pop-Location
    } catch { }
}

exit 0
