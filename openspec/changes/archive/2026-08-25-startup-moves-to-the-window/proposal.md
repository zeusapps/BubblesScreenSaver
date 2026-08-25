## Why

The tray menu has an entry, "Start with Windows", whose state cannot be seen.

`TrayIcon` builds the menu as `new ContextMenuStrip { ShowImageMargin = false }`. The image
margin is where WinForms draws the check glyph, so with it off, `Checked` sets a value nothing
renders. Two entries depend on it -- `_startup.Checked = Startup.IsEnabled` and `_pause` with
`CheckOnClick` -- and neither shows anything at all.

That was found the only way it could be: somebody was asked to look at the tick, and answered
that the menu does not display ticks, *that being why the settings moved to the window in the
first place*.

The `tray-menu` spec says the same thing, as the reason configuration left:

> The menu is the only surface reachable without opening anything, which makes it valuable for
> what is done often and a poor container for what is set once. **It cannot show a value**, and
> it has no room.

And then, ninety lines later, requires the impossible of this one entry:

> The startup entry SHALL be ticked according to the operating system's state, read when the
> menu opens, rather than according to a value the application stores.

Both cannot be true. The entry was listed among what the menu offers when configuration moved
out, and was never re-examined against the rule that moved everything else.

It should not have stayed. Whether the application starts with Windows is set once and then
left for months -- the definition of what the window is for and the menu is not. The menu's own
boundary puts it in the window, and a window can draw a checkbox that shows its state.

There is now a second reason. `Startup.Set` no longer writes one registry value; it writes and
removes a Start Menu entry alongside it. That is a larger action on somebody's machine than it
used to be, and its only trigger is a menu entry that cannot say whether it is on. Verifying it
required clicking blind and reading the registry from outside the application.

## What Changes

- **Move the startup control into the settings window**, as a checkbox that shows its state,
  with a note saying what it does -- that it registers the application to start at login and
  puts an entry in the Start Menu.
- **Remove the entry from the tray menu**, leaving the menu the commands it is good at.
- **Read and write the operating system**, not a stored value. That rule is right and survives
  the move; the registry can be changed from Task Manager or Settings, and the checkbox must
  reflect it whenever the window opens.
- **Keep it outside `settings.json`.** Startup is not a persisted setting and must not become
  one: two records of the same fact would disagree the first time somebody changed it outside
  the application.
- **Correct the specs.** The requirement demanding a tick the menu cannot draw is removed, and
  the startup requirements move to the capability that now owns them.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `tray-menu`: loses the startup entry and the requirement that it be ticked. The menu keeps
  only what it can actually show.
- `settings-dialog`: gains the startup control, and with it the first control in the window
  backed by the operating system rather than by `settings.json` -- which needs saying, because
  Cancel and Restore-defaults both assume otherwise.

## Impact

- `src/Bubbles/Session/TrayIcon.cs` -- the entry and its refresh come out.
- `src/Bubbles/Session/SettingsWindow.xaml.cs` -- a checkbox that reads and writes `Startup`
  rather than `Settings`, its note, and its part in the window's snapshot.
- `tests/Bubbles.Tests/` -- the window's treatment of a control that is not a setting.
- `openspec/specs/tray-menu/spec.md`, `openspec/specs/settings-dialog/spec.md`.
- No change to `Startup` itself, to what it writes, or to the idle timer.

## Open Questions

Recorded here, to be settled in `design.md`:

1. **What Cancel does to it.** The window promises that Cancel restores everything to how it
   stood when it opened. Startup is not in the snapshot it captures, so either the snapshot
   grows or the promise acquires an exception.
2. **What Restore-defaults does to it.** There is no obvious default: unregistering somebody's
   autostart because they wanted the shapes back to normal would be a surprise.
3. **Where the two requirements added last change belong.** "Registering to start with Windows
   also makes the application findable" and "The application is recognisable where it is found"
   were put in `tray-menu` because that is where startup lived. The first follows startup to
   the window; the second is about the executable's icon and is arguably homeless in either.
