## Context

Every other control in the settings window is a lens onto one `Settings` object:

```
  Check(label, get, set)
      box.Click     -> Edit(s => set(s, box.IsChecked == true))
      _refreshers   -> box.IsChecked = get(_host.Current)
```

`Edit` mutates the live object and hands it to whoever is listening. `_opened = host.Snapshot()`
is taken in the constructor, `Cancel` restores it, and `OnClosed` writes the file once. The
whole window rests on there being exactly one object holding the state.

Startup is not in that object and must not be. It lives in
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, and since the previous change also in the
per-user Start Menu. It can be turned off from Task Manager, from Windows Settings, or by
another copy of this application, none of which would update a value stored here. Two records
of one fact disagree the moment either is changed outside the other, which is precisely the
mistake the tray entry's existing requirement was written to prevent -- and that reasoning
survives the move even though the requirement's wording cannot.

So this is the window's first control whose state lives somewhere else. The three promises the
window makes were all written on the assumption that no such control existed:

```
  every persisted setting is reachable        <- startup is not persisted; the rule does not reach it
  Cancel restores what the window opened with <- the snapshot cannot hold what is not in Settings
  settings are written when the window closes <- there is nothing to write
```

## Goals / Non-Goals

**Goals:**

- The startup control shows whether startup is on, at a glance, whenever the window opens.
- It reads and writes the operating system, and nothing about it is stored by this application.
- The tray menu keeps only entries whose meaning is visible.
- The specs stop requiring something that cannot be done.

**Non-Goals:**

- Any change to `Startup` itself -- what it writes, what it removes, or the reconcile at launch.
- Turning on `ShowImageMargin`. That would make ticks drawable, and is the other way to fix
  this; see below for why it is not taken.
- Anything about `Pause`. It has the same invisible tick and it is staying in the menu, because
  it is a command used often rather than configuration. Its label is a separate question and
  nobody has asked for it.
- Adding a `settings.json` key for startup.

## Decisions

### The entry moves rather than the margin being turned on

`ShowImageMargin = true` would make the tick render, and would be a smaller diff. It is the
wrong fix twice over.

It widens every row in the menu to make room for images none of the entries have, for the sake
of two ticks -- and the menu was deliberately narrowed. More importantly it settles the wrong
question: the argument for the window was never "the menu cannot draw a tick", it was that a
menu is a poor place for what is set once and read rarely. Startup is set once. Making its tick
visible would leave it in the wrong place with better rendering.

`Pause` stays, tick and all, because it is not configuration -- and being unable to show its
state is a smaller problem for something you toggle and immediately observe.

### It reads the system every time the window opens, and writes it at once

Same rule the tray entry had, for the same reason, on a surface that can show the answer. The
checkbox is refreshed from `Startup.IsEnabled` like every other control is refreshed from
`_host.Current`, so the existing `_refreshers` list carries it with no new machinery. Clicking
it calls `Startup.Set` immediately, exactly as the menu entry did.

That means the control does not go through `Edit`, does not touch `Settings`, and contributes
nothing to `OnClosed`'s save. It is a lens onto the machine that happens to live in this window.

### Cancel puts it back; Restore-defaults leaves it alone

These pull in opposite directions and the split is deliberate.

*Cancel* means "undo what I did in this window". Somebody who ticked startup and then cancelled
did that here, and leaving them registered would be the window quietly keeping one of the edits
it promised to drop. The cost is one bool captured beside `_opened` and one comparison on the
way out -- and `Startup.Set` is only called if the value actually differs, so cancelling a
window in which startup was never touched writes nothing at all.

*Restore-defaults* means "put the screensaver back how it ships". Whether the application starts
with Windows is not one of the screensaver's defaults -- it is a property of the installation,
and the button is reached by somebody who dislikes what the shapes are doing. Silently
unregistering their autostart, and removing their Start Menu entry with it, is a much longer
reach than that button implies and is not what they asked for.

So the asymmetry is: Cancel undoes what you did, Restore-defaults resets what the screensaver
looks like. Startup is in the first and not the second, and the requirement says so rather than
leaving it to be discovered.

*Alternative considered: leave it outside both.* Simpler, and it makes Cancel a liar about a
control sitting in the same window as the ones it does restore. Not worth the four lines saved.

### The two requirements from the previous change follow what they describe

"Registering to start with Windows also makes the application findable" describes what
`Startup.Set` does. It was put in `tray-menu` because that was the only place startup was named;
it now moves to `settings-dialog` with the control that triggers it.

"The application is recognisable where it is found" -- the executable's icon -- stays where it
is. It is not about the settings window either, and moving it twice to end up somewhere equally
arbitrary is churn. It is noted as homeless rather than shuffled.

## Risks / Trade-offs

- **Startup becomes less discoverable.** The tray menu is one right-click away; the window is
  two clicks and a scroll. -> It is set once, usually never again, and it was already invisible
  where it was. A control you can see in a place you have to look for beats one you cannot see
  in a place you pass often.

- **The window now has a control that behaves differently from its neighbours.** It ignores
  Restore-defaults, and it is the only one whose value is not in `settings.json`. -> That is
  the honest shape of the thing, and both differences are stated in the note beside it rather
  than left as surprises.

- **Cancel now touches the registry and the Start Menu.** A path that previously only mutated
  an in-memory object can now write to the machine. -> Only when the value actually differs
  from the capture, which is only when the user changed it in this window. The failure envelope
  in `Startup` already swallows everything, so a refused write cannot take the window down.

- **The tick that was never visible may have been clicked blind before now.** Somebody may be
  registered, or not, without knowing which. -> The new checkbox tells them the truth the first
  time they open the window, which is the point.

## Migration Plan

None. Nothing is stored, so there is nothing to migrate: the checkbox reads whatever the
registry says the first time the window opens. An installation whose startup was toggled blind
from the old menu simply sees its actual state.

## Open Questions

None blocking. The three raised in the proposal are answered above: Cancel restores it,
Restore-defaults does not touch it, and of the two requirements from the previous change one
follows startup to the window while the icon one stays put and is noted.
