# TaskBoost

A small Windows 10/11 tool that makes a PC lean and fast, keeps watch over it, explains what starts with Windows, has a one-click gaming mode, and puts a pixel spider on your desktop. One exe, no installer, no dependencies.

**English** · [Română](README.ro.md)

![The TaskBoost window with the buddy sitting on it](docs/hero.png)

| Tab | What it is for |
| --- | --- |
| **Home** | A health score for the whole PC, live readings, settings that travel between PCs, the interface language, and the way out |
| **Boost** | One-click performance and battery tweaks, with full undo |
| **Guard** | Watches camera, microphone, network, keyboards, remote access and startup changes |
| **Startup** | Lists everything that starts with Windows and says what each item does |
| **Turbo** | Laptop modes, gaming mode, memory freeing and a safe clean-up |
| **Care** | Maintenance and updates: disk check, drive optimisation, virus scan, BIOS / firmware / driver check for this device, a switch to stop Windows updates |

It also has a **desktop buddy**, a pixel spider that walks the screen edges and opens a small menu when you click it, and an **MCP server** so Claude can read the PC's state and speak through the buddy.

![Tabs sliding, the score ring filling, a switch being flipped](docs/motion.gif)

## Contents

- [Getting started](#getting-started)
- [Home](#home) · [Boost](#boost-what-it-applies) · [Guard](#guard) · [Startup](#startup) · [Turbo](#turbo) · [Care](#care)
- [The desktop buddy](#the-desktop-buddy) · [Laptop modes](#laptop-modes) · [Claude (MCP)](#claude-mcp)
- [Shortcuts](#shortcuts) · [Look and movement](#look-and-movement) · [Undo](#undo) · [Command line](#command-line)
- [Build, test, pictures](#build-test-pictures) · [Notes](#notes) · [License](#license)

## Getting started

1. Download `TaskBoost.exe` from the [latest release](../../releases/latest) and put it in a folder of its own.
2. Run it. Windows may show a "Windows protected your PC" notice because the exe is not code-signed: choose *More info*, then *Run anyway*.
3. Switch on what you want on the **Boost** tab and press **Apply now**.

**Administrator approval.** Some actions change system settings and need Windows' approval. The first time one is needed, TaskBoost offers to be approved once for the session: an elevated helper then runs TaskBoost's own commands until you exit, without further prompts. The helper lives only as long as the TaskBoost window, and it takes commands only from that window (it checks which process connects to it and refuses any other). Say No and Windows asks at every action instead; the tray menu has the same choice.

Requirements: Windows 10 version 1903 or newer, or Windows 11. .NET Framework 4.8 is already part of those. Administrator approval is asked for only when a feature needs it.

TaskBoost changes Windows settings. Everything on the Boost tab can be undone with **Restore previous settings**, and gaming mode undoes itself when you leave it, but use it at your own risk.

## Home

![Home tab](docs/home.png)

**Health score**, 0 to 100, from seven parts: security (the Guard tab's alerts and notices), startup programs (suspicious or to-check entries), disk space, free memory, protection (Defender and the firewall), clean-up (old temporary files) and updates (whether Windows updates itself). Each row says where the points went and has a small gauge. The ring fills and the number counts up each time the score is worked out. The buddy earns a bow tie at 75 and a crown at 90, and sits beside the score wearing it.

![Accessories](docs/accessories.png)

**Live readings** under the score: memory in use, free space on the system drive, battery charge and whether it is charging, and how long the PC has been running (a restart is suggested after a week). They refresh every few seconds while the tab is open.

- **Export settings** writes one `.taskboost` file with your choices (Boost items, Guard switches, Turbo and Care options, game list, known items, language). **Import settings** reads it on another PC; press *My full setup* or *Apply now* there to apply. Only the files TaskBoost knows are read from it, never the undo record or the log.
- **Language** switches the interface between English and Romanian; TaskBoost restarts itself. The explanations in the Guard and Startup lists and the log stay in English.
- **Animations** switches the movement in the window on or off (see [Look and movement](#look-and-movement)).
- **Remove TaskBoost** puts every setting it changed back, removes the sign-in entries and the Claude link, deletes its data folder and exits. The TaskBoost folder itself stays for you to delete.

![The window in Romanian](docs/romana.png)

## Boost: what it applies

![Boost tab](docs/boost.png)

| Item | Needs admin | What it does |
| --- | --- | --- |
| Dark mode | no | Dark theme for Windows and apps, solid black desktop with no wallpaper, no transparency |
| No animations | no | Window, menu, tooltip and taskbar animations, fades and shadows off; menus open with no delay; font smoothing stays on |
| Quiet background | no | Background apps off, Start/lock-screen suggestions and tips off, no delay before startup programs |
| Thin taskbar | no | Small icons, window titles shown, search box / Task View / Cortana / People hidden, no "most used" tracking |
| Battery first | no | On battery: screen off after 3 min, hibernate after 15 min, lid close hibernates, battery saver always on, CPU capped at 99% (no turbo). Plugged in: full CPU speed, screen off after 10 min |
| No browser autostart | no | Removes the Chrome and Edge "launch at sign-in" entries |
| Trim services | yes | Disables telemetry, Xbox, maps, fax, retail demo and media sharing services and nine data-collection tasks; stops Edge preloading and running in the background |
| Text boot | yes | Boot lists drivers as they load; detailed status lines at sign-in and shutdown |
| Bare Start menu | yes | Empty, locked Start layout (tiles cannot be pinned while it is on); no "recently added" list |

Text boot and Bare Start menu are switched off until you turn them on. **My full setup** switches every item on and applies them in one click, which is the quickest way to bring a fresh Windows 10 or 11 PC to the complete setup.

On Windows 11 the taskbar item also moves the Start button to the left and hides Chat (small icons and window titles are not available there), and Bare Start menu only hides the "recently added" list, because Windows 11 has no tiles. Some changes only show after signing out and back in, or restarting.

"No animations" is about Windows' own effects. TaskBoost's window keeps its own movement, which has its own switch on the Home tab.

### When it runs

- **Manual**: only when you press *Apply now*.
- **Automatic**: a scheduled task named `TaskBoost` runs `TaskBoost.exe --apply` at sign-in. The task stores the full path of the exe, so if you move the folder, switch to Manual and back to Automatic.

Switching between the two asks for administrator approval once.

## Guard

![Guard tab](docs/guard.png)

The Guard watches the PC every few seconds and lists what it sees. Red rows are alerts, amber rows are notices, grey rows are information. The rim of the spider emblem in the header and in the tray turns amber or red to match, the status dot beside it sends out rings, and the Guard tab shows how many rows want attention.

| Check | What it reports |
| --- | --- |
| Camera and mic | Apps using the camera, microphone or location right now, and apps that used them in the last 15 minutes |
| Network activity | Every program with an open internet connection, with its publisher. A program seen online for the first time is a notice; one without a valid signature is an alert |
| Fake keyboards | A keyboard device that was not connected before (for example a "BadUSB" stick that types commands), two keyboards connected at once, and keyboards built on programmable boards (Arduino, Teensy, Pico...) |
| Remote and mirroring | An active Remote Desktop session, Remote Desktop being switched on, nearly 30 known remote-access and mirroring tools (TeamViewer, AnyDesk, VNC, Chrome Remote Desktop, ...), and extra displays |
| New startup programs | Anything newly added to the Run keys or Startup folders |
| Suspicious signs | Programs using a Windows component's name from the wrong folder, programs with a double extension, unsigned programs running from a temp folder, a proxy, hosts-file redirects, and Defender or the firewall being off |

The first scan takes what is already on the PC as the starting point. After that, **Mark as known** accepts a row so it stops alerting; **End program** closes the selected program; **Open file location** shows its file.

Behaviour switches: tray notifications, keep watching in the tray after the window is closed, and start hidden at sign-in (`TaskBoost.exe --guard`, a per-user Run entry, no administrator rights). TaskBoost runs in the background by default: both of the last two are on, so the guard, the buddy and the shortcuts are there from sign-in. Switch them off on the Guard tab to run it only when you open it.

What it is not: an antivirus. It does not scan files, block connections or remove anything, it cannot see keyloggers or hidden screen capture, and it shows who is connected, not how much data is sent. Keep Windows Defender (or another antivirus) on.

## Startup

![Startup tab](docs/startup.png)

The Startup tab lists everything that starts with Windows (Run keys, Startup folders, non-Windows background services, and scheduled tasks outside Windows' own folders, with their trigger) and says in plain words what each one does, who signed it, and a verdict: **Keep**, **Useful**, **Optional**, **OK**, **Check**, **Leftover** or **Suspicious**.

The explanations come from a built-in list of about 260 common programs (`src\Knowledge.cs`); anything else is described from the information stored in its own file. Select an item and press **Switch off** to stop it starting with Windows, or **Switch on** to bring it back. Nothing is deleted: sign-in entries use the same switch as Task Manager's Startup page, background services are changed between Automatic and Manual (they keep running until the next restart), and scheduled tasks are disabled and enabled. Items marked *Keep* ask for confirmation first, and all-users entries, services and tasks need administrator approval.

An entry is marked Check or Suspicious when it is unsigned, runs from a temp folder, uses the name of a Windows component from the wrong folder, or starts a hidden or encoded script. An entry whose file is gone is a Leftover. TaskBoost's own sign-in entry is recognised as itself.

## Turbo

![Turbo tab](docs/turbo.png)

**Laptop mode** has the same five modes the buddy offers; see [Laptop modes](#laptop-modes).

**Gaming mode** is temporary; *Leave gaming mode* puts everything back.

- switches to the High performance power plan
- turns Windows Game Mode on and background game recording off
- lifts the network throttling limit and flushes the DNS cache
- pauses Windows Update, delivery optimisation, indexing, preloading and telemetry services (only the ones that were running)
- closes sync tools, updaters and phone link; browsers and chat apps only if you switch that on
- frees memory
- programs under *My games* are set to use the high-performance graphics card, and get high priority while gaming mode is on and TaskBoost is running

**Charge my phone** keeps the USB ports powered so a phone keeps charging from the PC: USB power saving is switched off, the PC does not sleep or hibernate, and closing the lid does nothing (the screen can still turn off). **Stop phone charging** puts every setting back. On battery this drains the laptop to feed the phone.

**Free memory now** asks every program to give back memory it is not using and, as administrator, empties the Windows file cache. Nothing is closed.

**Safe clean-up** deletes only inside a fixed list of folders (your temp folder, the Windows temp folder, crash dumps, error reports), only files older than a week, never through links or junctions, and it skips anything in use. *Preview clean-up* shows what would be freed without deleting. Recycle Bin and clipboard are off unless you switch them on. It does not use `diskpart` and cannot touch Windows, programs or documents.

## Care

![Care tab](docs/care.png)

Maintenance with Windows' own tools. Nothing here deletes your files, and the PC stays usable while it runs.

| Task | What it does | Time |
| --- | --- | --- |
| Health report | Uptime, pending restart, free memory, free space per drive, disk health, battery wear and charge cycles | seconds |
| Check the disk | `chkdsk /scan` on the system drive | under a minute |
| Optimise drives | `defrag /C /O`: TRIM on SSDs, defragmentation on hard disks | under a minute on SSDs |
| Quick virus scan | Windows Defender quick scan | a few minutes |
| Check system files | `sfc /scannow`, repairs damaged Windows files | 10 to 20 minutes |
| Windows image | `DISM /ScanHealth` and `/StartComponentCleanup` | slow |

Everything except the health report and the virus scan needs administrator approval.

**Updates for this device**

- **Check for updates** goes online and reports what this particular machine needs: the installed BIOS against the newest one the maker offers (read from Lenovo's catalogue on Lenovo machines), firmware packages, driver and Windows updates waiting in Windows Update, and how old the antivirus definitions are.
- **Install updates** installs what Windows Update offers, drivers and firmware included, and refreshes the antivirus definitions. It never flashes a BIOS by itself; for that, **Maker's driver page** opens the right page for the model.
- **Stop updates** switches automatic Windows updates off (policy `NoAutoUpdate`) and back on. Switching updates back on also removes the common "fake update server" block if the PC has one, keeping a backup. With updates stopped, security fixes stop too, so check by hand now and then.

## The desktop buddy

![The buddy, its menu, an emote and a change of costume](docs/buddy.gif)

**The buddy** is a blue pixel spider. While the TaskBoost window is open it walks back and forth along the top of the window; when you close the window, TaskBoost stays in the tray and the buddy hops down to live on the edges of the screen: it walks along the bottom, climbs the sides, crosses the top, stops to look around, blinks, falls asleep when the PC is idle, and turns red-eyed with a "!" when the Guard raises an alert. From the top edge it sometimes lets itself down on a thread, hangs there a while and climbs back up; with more than one monitor it wanders from one to the next. It speaks in a bubble, a word at a time.

**It waits for you.** When the pointer comes near, the buddy stops and watches it, so it is easy to click; it stays exactly where it is while a mouse button is held on it. If nobody clicks for a few seconds it scuttles out of the pointer's way instead.

**Click it, with either button, and its menu opens beside it.** The buddy turns red and waves while the menu is open.

| | |
| --- | --- |
| ![The buddy's menu](docs/menu.png) | ![The emotes unfolded](docs/menu-emotes.png) |

- **The five laptop modes** as a strip of faces, each the face the buddy wears in that mode. The one in use is lit; the name and purpose of the one under the pointer are written below.
- **Emotes** unfolds a grid of its 17 expressions: happy, love, laugh, wink, cool, star, music, think, surprised, scared, dizzy, cry, angry, fierce, alert, yawn and sleepy. The face under the pointer comes alive; a click and the buddy makes it.
- **Search for files...**: type what you are looking for and the buddy opens a Windows search in the File Explorer folder that was in front, or in your user folder if none was.
- **Open TaskBoost**, **Take a nap**, **Go away**. The tray icon's menu calls it back.
- In Study / work mode the top line shows the time left in the **Pomodoro**: 25 minutes of focus, 5 of break, announced in its bubble.

The menu closes when you choose something, click the buddy again, click anywhere else or press Esc, and by itself when the mouse stays away for a few seconds. It never takes the keyboard away from the program you are working in.

**Carry it.** Hold the left button on the buddy and move the pointer to carry it anywhere; when you let go it settles on the nearest edge of the screen, or on the TaskBoost window if you drop it on the title bar.

**Net focus.** Drop the buddy on another program's window and it sits on that window, follows it, and asks: *Net focus: this program goes first online*, or *Just sit here*. Nothing changes unless you choose net focus. With net focus, that program's traffic is marked as priority and it gets high CPU priority, the other programs that are online have their upload held to about 0.5 Mbit/s, and Windows' background downloaders are paused (this asks for administrator approval). Drag the buddy back to the desktop, or close that window, to end it; it also ends when TaskBoost exits. Windows can shape what a PC sends but not what it receives, so the download gain comes from pausing the background downloaders; the rules disappear by themselves at the next restart.

![Every expression](docs/emotes.png)

Its three switches are on the Guard tab:

- **Desktop buddy**: on or off.
- **Hide while I work**: off by default, so the buddy stays on screen whatever you are doing (except over a full-screen window). When on, it shows only when the desktop, the taskbar, TaskBoost, a web browser, File Explorer or a terminal is in front.
- **Only with Claude**: off by default; when on, it appears only while Claude is connected.

The tray icon has the same kind of menu: open the window, the keyboard shortcuts, Ask Claude, send the buddy away or call it back, approve administrator rights for the session, and Exit.

![The tray icon's menu](docs/tray.png)

## Laptop modes

![The buddy in each mode](docs/costumes.png)

The buddy's menu and the Turbo tab offer five modes. The buddy dresses for the one in use:

| Mode | Its job | What it changes | The buddy |
| --- | --- | --- | --- |
| **Low power** | Longest battery | Power saver plan, power mode "better battery" | Sleepy, ambles at half speed, yawns |
| **Normal** | Everyday balance | Balanced plan with the Boost battery settings | Its usual blue self |
| **Study / work** | Quick and quiet without burning the battery | Balanced plan pushed to "better performance", the processor cap on battery lifted so programs open fast, app notifications off | Engineer: hard hat and glasses, no random emotes, and it runs the Pomodoro |
| **Best performance** | Full power | High performance plan, power mode "best performance" | Red, fierce, runs twice as fast |
| **Gaming** | Full power and nothing in the way | Gaming mode: best performance plus background services and apps paused (asks for administrator approval) | Red, fierce, runs twice as fast |

These are plain Windows power settings, so they work on any laptop or desktop, unlike a maker's own key such as Lenovo's Fn+Q. They do not change the fan profile that such a key also switches. Shortcuts: Ctrl+Alt+F9 Low power, Ctrl+Alt+F10 Normal, Ctrl+Alt+F11 Best performance.

Switching to another mode undoes the previous one: leaving Gaming restores everything gaming mode changed, and leaving Study / work turns notifications back on.

## Claude (MCP)

**Claude** connects through the Model Context Protocol. `TaskBoost.exe --mcp` is an MCP server on stdin/stdout; while it is connected, Claude is the spider and can speak through it.

You do not have to set anything up: every time TaskBoost starts it connects itself to Claude Code and to Claude Desktop if they are installed, and repairs the link if the folder was moved. **Ask Claude**, at the top right of the window (also in the tray menu), opens Claude Code already asked to look over the PC.

To do it by hand instead, for Claude Code:

```
claude mcp add --scope user taskboost -- "C:\path\to\TaskBoost.exe" --mcp
```

Claude Desktop, in `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{ "mcpServers": { "taskboost": { "command": "C:\\path\\to\\TaskBoost.exe", "args": ["--mcp"] } } }
```

| Tool | What Claude can do with it |
| --- | --- |
| `guard_scan` | Read the security snapshot from the Guard tab |
| `startup_items` | Read what starts with Windows, with explanations and verdicts |
| `health_report` | Read disk health, free space, battery wear, uptime |
| `cleanup_preview` | See how much a clean-up would free, without deleting |
| `free_memory` | Ask programs to give back unused memory |
| `gaming_mode` | Switch gaming mode on or off (Windows asks you to approve) |
| `buddy_say` | Make the buddy say one sentence with any of its 17 moods |
| `buddy_visible` | Call the buddy or send it away |

Claude cannot delete files, switch startup items or change Boost settings through these tools.

## Shortcuts

Global shortcuts work anywhere while TaskBoost is running, even from the tray. **F1** in the window shows the list. The full manual is in [SHORTCUTS.md](SHORTCUTS.md) (Romanian: [SCURTATURI.md](SCURTATURI.md)).

![The shortcut list](docs/keys.png)

| Keys | Action |
| --- | --- |
| Ctrl+Alt+F1 | Show or hide TaskBoost |
| Ctrl+Alt+F2 | Gaming mode on / off |
| Ctrl+Alt+F3 | Free memory |
| Ctrl+Alt+F5 | Charge my phone on / off |
| Ctrl+Alt+F6 | Ask Claude |
| Ctrl+Alt+F7 | Send the buddy away / call it back |
| Ctrl+Alt+F8 | Quick health report from the buddy |
| Ctrl+Alt+F9 / F10 / F11 | Low power / Normal / Best performance |
| Ctrl+1 ... Ctrl+6 | Switch tab (inside the window); Left / Right on the tab bar does the same |
| Esc / F1 | Close to the tray / show the shortcut list (inside the window) |

## Look and movement

Everything in the window is drawn by TaskBoost itself, in one dark theme taken from the emblem: near-black, electric blue, white. So are its message boxes, the shortcut list, the search prompt and the two menus.

Things move a little, to show what just happened: the highlight slides to the tab you open while the old page gives way to the new one, switches glide, buttons and cards answer the mouse and send out a ring from a click, a turning arc shows work in progress, the score ring fills as the number counts up, lists fade in row by row, and the menus slide in beside what you clicked.

All of it is driven by one timer that stops when nothing is moving, so a window that is standing still costs nothing, and the header's pulse and the buddy beside the score only run while they can be seen. **Animations: on / off** on the Home tab switches the movement off; everything then goes straight to its end state. (The buddy keeps walking; it has its own switch.)

## Undo

The first time a setting is changed its original value is saved. **Restore previous settings** puts everything back.

The log, the undo record and the saved selection are in `%LOCALAPPDATA%\TaskBoost` (button *Open log folder*), not in the app folder, so the folder can be copied to another PC as it is.

## Command line

```
TaskBoost.exe            open the window
TaskBoost.exe --guard    start hidden in the tray with the guard watching
TaskBoost.exe --exit     ask the running TaskBoost to close (it leaves the tray too)
TaskBoost.exe --mcp      MCP server for Claude (started by Claude, not by hand)
TaskBoost.exe --apply    apply the saved selection with no window (admin items only if already elevated)
```

A second `TaskBoost.exe` does not open a second window: it brings the running one forward.

## Build, test, pictures

Run `build.bat`. It uses the C# compiler that ships with Windows (.NET Framework 4.x); no SDK or Visual Studio is needed. If TaskBoost is running, the old exe is moved aside and the new one takes its place at the next start (`TaskBoost.exe --exit`, then start it again).

**`tests\test.bat`** builds a self-test from the same sources and runs it. It keeps its data in `tests\out`, never in your TaskBoost folder.

| Group | What it checks |
| --- | --- |
| `logic` | Command-line parsing of startup entries, the built-in lists, which addresses count as the internet, drawing of every mood and icon, and that every text has a Romanian version |
| `sandbox` | Saved selections, the undo record, registry changes and their undo (on a scratch key), export and import, and the clean-up on a sandbox temp folder: old files go, new files, files in use and anything behind a junction stay |
| `scans` | The guard, the startup list, the health report and the score on the real PC, read-only |
| `mcp` | The MCP server as Claude talks to it: every read-only tool, errors, and a clean stop |
| `ui` | The real window, shown off-screen: every tab, the tab bar, switches, dialogs, the tray menu, shortcuts, the Pomodoro, and the buddy's clicks, menu and drop |
| `live` | Only with `test.bat live`. Round trips on the real system, each of which puts back what it changed: a test entry in the sign-in list (the guard must notice it; the Startup switch must turn it off and on), the per-user Boost tweaks applied and restored, gaming and charging mode on and off (the parts that need no administrator), the laptop modes, and the online update check |

What the self-test cannot do is approve a Windows administrator prompt, so the administrator-only parts (services, boot options, the sign-in task, net focus, system maintenance) are not run by it.

**`tests\shots.bat`** makes the pictures and the two short films in `docs\` from the app itself: it opens the real window on a sandbox copy of your settings and captures the screen, so the pictures always show what the code draws.

```
src\Program.cs    entry point and command line
src\MainForm.cs   window shell: header, tab bar, page slide, tray icon
src\HomePage.cs   the Home tab: health score, score ring, live readings
src\BoostPage.cs  the Boost tab
src\GuardPage.cs  the Guard tab
src\Guard.cs      the checks themselves; add new watchers here
src\StartupInfo.cs, StartupPage.cs   the Startup tab
src\Knowledge.cs  built-in list of known programs and keyboard makers
src\Turbo.cs, TurboPage.cs           gaming mode, laptop modes, net focus, memory, clean-up
src\Care.cs, CarePage.cs             maintenance
src\Updates.cs    the update check, and the switch that stops Windows updates
src\Buddy.cs      the desktop buddy: pixel sprite, speech bubble, behaviour
src\PopMenu.cs    the small dark menu of the buddy and the tray icon
src\Dialog.cs     message boxes, the search prompt and the shortcut list, in the app's colours
src\Motion.cs     animation: values that glide to a target, moved by one shared timer
src\Controls.cs   dark owner-drawn controls: toggle card, option card, button, tab bar, header, findings list
src\Theme.cs      colours, fonts and the illustrations (drawn in code)
src\Mcp.cs        the MCP server and the link to Claude
src\Portable.cs   export / import of settings, and Remove TaskBoost
src\Lang.cs       the Romanian translation; add a language by adding a table
src\Tweaks.cs     every setting, grouped; add new groups here (the key also names its illustration)
src\State.cs      undo record and registry helpers
src\Startup.cs    the sign-in scheduled task
src\Util.cs       logging, saved selection, process and Win32 helpers, the per-session administrator helper
src\emblem.png    the spider-shield emblem, embedded in the exe
src\app.ico       exe icon, generated from Icons.AppIconBitmap
tests\SelfTest.cs the self-test
tests\Shots.cs    the picture and film maker
```

## Notes

- Written and tested on Windows 10 (LTSC 2021). Services or tasks that do not exist on a machine are skipped.
- `dptftcs`, `SQLWriter` and `TbtP2pShortcutService` in the service list come from the laptop this was first written for; they are harmless elsewhere.
- The pictures on this page were taken at 125% display scaling.

## License

MIT. See [LICENSE](LICENSE).
