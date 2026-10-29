# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

<!-- Impeccable only knows web / ios / android / adaptive. This product is actually a Windows desktop app (C#, WPF, WPF-UI),
     so "web" is the closest default; treat web-specific guidance (CSS, browsers, responsive breakpoints) as approximations
     and judge the result by the rendered window. -->

## Users

Anyone on Windows who changes monitor setups or audio outputs often: people with several monitors who switch between layouts,
and people who connect a PC to a TV (games in Steam Big Picture, couch viewing) and go back to the desk. The author is the
first user. The UI language is Spanish.

## Product Purpose

Switch the whole setup in one gesture. A profile stores a monitor layout (which screens are on, where they sit, which is
primary) and the sound output, and applies both together from a window, the tray, a keyboard shortcut or a controller
combination. Success is that changing between desk and TV takes one action and never leaves the user without a picture.

## Positioning

- One profile changes everything at once: monitors, sound, the LG TV (power and input) and programs, with a keyboard or
  controller trigger per profile.
- Simple by default: the basic mode covers monitors and sound; everything else sits behind an advanced mode.
- Light and portable: a single .exe that updates itself from GitHub releases.

## Operating Context

- Lives in the Windows system tray and opens a window on demand; keyboard shortcuts work with the window closed.
- Used on machines with several monitors and often a TV on HDMI (LG webOS over the local network), sometimes from the couch
  with a game controller and Steam Big Picture.
- Profiles and settings live in `%AppData%\ScreenSoundSwitcher`.
- Distribution: open source on GitHub (MIT), donations through Ko-fi (https://ko-fi.com/vitalies), in-app update from GitHub
  releases. Ko-fi and the GitHub repository (https://github.com/vitaliesdev/screen-sound-switcher) are set in the app; the
  first release (tag `v0.1.0`) is not published yet.

## Capabilities and Constraints

- Monitor layouts are captured and restored through the Windows display API; layouts survive reboots because monitors are
  re-identified by device path. Layouts can be rearranged by dragging, like Windows' own display settings.
- Safety net: a confirmation with a countdown after any layout change (the previous layout returns on its own), and a global
  emergency shortcut that restores the last confirmed layout.
- Audio: default playback output, waiting for outputs that appear late (a TV that is waking up).
- TV: LG webOS only (wake-on-LAN, power off, input switching), paired over the local network.
- Triggers: global keyboard shortcut, Xbox-style (XInput) controller combination, program rules (open or close a program,
  match by process or window title).
- Builds: a small framework-dependent .exe (needs the .NET 10 Desktop Runtime) and a larger portable one with .NET inside.
- Terminology: "perfil" (profile), "disposición" (layout), "modo avanzado".
- Not decided: signing of the executable, other TV brands, languages other than Spanish.

## Brand Commitments

Name shown in the app: "ScreenSoundSwitcher". No logo or identity assets exist beyond a tray icon drawn in code (monitor with speaker and swap arrows, crimson gradient).

## Evidence on Hand

Working app and source in this repository. No users, testimonials, downloads, press or benchmarks exist yet; none may be
invented in the README, release notes or the app.

## Product Principles

1. Never leave the user without a picture: any change that can blank a screen is reversible without seeing anything.
2. One gesture, whole setup: a profile is the unit; the user should not have to chain actions by hand.
3. Basic first, depth on request: the common path (monitors and sound) is complete on its own; advanced options never crowd it.
4. Quiet in the background: it lives in the tray, starts without a window and interrupts only when something needs a decision.
5. Small and self-contained: a single file that needs nothing installed beyond what its variant states.
