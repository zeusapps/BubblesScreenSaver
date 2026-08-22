---
name: release
description: Cut, publish and install a Bubbles release. Use when asked to release, publish, ship, tag a version, cut a version, or (re)install the app locally — including "push the new version and reinstall". Covers the preflight checks, the tag that triggers CI, and installing the released binary over the running instance.
---

# Releasing and installing Bubbles

The release is driven entirely by a `v*` tag. Pushing the tag builds, signs, checksums and
publishes; nothing is uploaded by hand. Local `dist\Bubbles.exe` is **not** what users get and
must never be installed as if it were — see *Never install the local build*.

Do the phases in order. Stop and report rather than working around a failure.

## 1. Preflight

```bash
git status --short                  # must be clean, or know why it is not
dotnet build Bubbles.sln --nologo
dotnet test Bubbles.sln --nologo
```

All three must pass before anything is tagged. If commits are still to be made, make them
first — the tag names a commit, so a tag pushed ahead of a commit ships the wrong tree.

**If splitting work into several commits, verify each one builds and tests on its own.** A
commit that only compiles alongside the next one breaks `git bisect`, which is the reason to
split at all. Isolate by moving the later commit's new files aside and reverting its edits, then
build. Two traps, both hit before:

- **Back up before reverting.** `git checkout -- <file>` on a file carrying edits for a *later*
  commit destroys them with no reflog to recover from. Copy every file being reverted somewhere
  safe first, including ones the later commit only modifies.
- **Never rebuild a file through `subprocess`/`git show` with locale decoding.** It silently
  turns `—` into `â€”` throughout. Read and write with explicit UTF-8, and check the result
  (`grep -c 'â€'`) before committing.

## 2. Version

```bash
git tag | sort -V | tail -3
```

Pick the next semantic version from what is there. New behaviour or a new setting is a minor
bump; a fix alone is a patch. Do not edit `<Version>` in `Bubbles.csproj` — it stays at `1.0.0`
and CI passes the real version with `-p:Version=` from the tag.

## 3. Push, then tag

```bash
git push origin main
git tag -a vX.Y.Z -m "<annotation>"
git push origin vX.Y.Z
```

Main first. The tag triggers the release; pushing it before the branch publishes a commit that
is not on `main`.

The annotation is read by people scanning the tag list: say what changed and why, in the same
register as the commit messages — the problem, not the patch notes.

## 4. Watch both runs

A tag push starts two workflows: `build` on main and `release` on the tag.

```bash
gh run list --limit 4
gh run watch <release-run-id> --exit-status
gh release view vX.Y.Z --json tagName,assets --jq '{tag:.tagName,assets:[.assets[].name]}'
```

Expect assets `Bubbles.exe` and `SHA256SUMS.txt`.

`build` also re-renders the documentation images with `--export` and **fails if the committed
ones differ**, because every image in the README is generated from the code. If it fails there,
the artwork changed and the regenerated images need committing.

The `Sign` step is skipped unless `SIGNING_PFX_BASE64` is configured; an unsigned release is
normal here and SmartScreen will warn on first run.

## 5. Install

**Never install the local build.** `dist\Bubbles.exe` is framework-dependent (`SelfContained=false`
in the csproj) and around 24 MB. The released binary is built with `-p:SelfContained=true
-p:EnableCompressionInSingleFile=true` and is around 75 MB. Installing the local one leaves a
binary that needs a .NET runtime the user may not have, and that is not what the checksum
covers. Always install the downloaded release.

Find the install path rather than assuming it:

```bash
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" | grep -i bubble
```

Then:

```bash
gh release download vX.Y.Z --repo zeusapps/BubblesScreenSaver --dir <tmp>
sha256sum <tmp>/Bubbles.exe && cat <tmp>/SHA256SUMS.txt      # must match
```

Verify the checksum **before** overwriting anything. Then stop the running instance — the app
holds a single-instance mutex, so a new one will not start alongside it:

```bash
taskkill //IM Bubbles.exe //F
```

`/F` is fine: `OnExit` restores monitor brightness and HDR, but so does `RecoverFromCrash` at
the next launch, from records written before each change. Do not force-kill *during* a blackout
if it can be avoided.

Copy `Bubbles.exe` and `SHA256SUMS.txt` into the install directory, start it, and confirm:

```bash
powershell -NoProfile -Command "(Get-Item \"\$env:LOCALAPPDATA\Programs\Bubbles\Bubbles.exe\").VersionInfo.ProductVersion"
powershell -NoProfile -Command "Get-Process Bubbles | Select-Object Id,Path"
```

`ProductVersion` should read `X.Y.Z+<commit sha>`. Report the version and the path actually
running, not the one that was intended.

## 6. Report

State the released version, that CI was green, that the checksum matched, and the running
`ProductVersion`. If any step was skipped, say which.

## Things that need the user, not you

- **Triggering a blackout.** Check `LockAfterBlackout` in `%APPDATA%\Bubbles\settings.json`
  first. When it is on, a completed blackout **locks the workstation** and demands a PIN. Never
  do that to somebody mid-work without asking. It also dims monitors over DDC/CI and toggles
  HDR, which is a display mode change on every attached screen.
- **Anything needing real media playing**, a real camera, or a real call.

## Verifying an install without disturbing anyone

These are read-only or self-contained, and safe to run against a live instance:

| Command | Answers |
|---|---|
| `--media` | what Windows reports playing, and which stages it would hold off |
| `--busy` | whether anything is holding the overlay off, and the foreground geometry |
| `--glass-test` | whether the desktop actually composites through the overlay (puts a window up for ~1s) |
| `--inputs` | which source each monitor is showing; sends no DDC writes |
| `--dim-test` | backlight control per monitor; uses a scratch state file when an instance is live |
| `--check-update` | one update check, without starting the overlay |

For anything deeper, relaunch with `BUBBLES_LOG=1 BUBBLES_SNAP=1` and read
`%APPDATA%\Bubbles\log.txt` and `snap.png`.

## Rolling back

Releases are immutable once published; roll forward with a new patch tag rather than retagging.
To put a user back on the previous version, `gh release download vX.Y.Z-1` and install that the
same way — the updater will offer the newer one again, so also set `"AutoUpdate": false` if the
downgrade needs to stick.
