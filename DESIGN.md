---
name: ScreenSoundSwitcher
description: A calm control panel that lives in the Windows tray and switches monitors, sound and TV in one gesture.
colors:
  accent-blue: "#0A84FF"
  tile-blue-start: "#0A84FF"
  tile-blue-end: "#5E5CE6"
  tile-purple-start: "#BF5AF2"
  tile-purple-end: "#FF375F"
  tile-teal-start: "#1E9E4A"
  tile-teal-end: "#0E8FB0"
  tile-pink-start: "#F0264F"
  tile-pink-end: "#D9480F"
  tile-orange-start: "#E8730A"
  tile-orange-end: "#D9480F"
  tile-indigo-start: "#5E5CE6"
  tile-indigo-end: "#2E88E0"
  tile-green-start: "#23A03F"
  tile-green-end: "#0A8F8A"
  surface-base: "#202020"
  surface-group: "#2B2B2B"
  text-primary: "#FFFFFF"
  glyph-on-tile: "#FFFFFF"
typography:
  title:
    fontFamily: "Segoe UI Variable, Segoe UI, sans-serif"
    fontSize: "26px"
    fontWeight: 700
  heading:
    fontFamily: "Segoe UI Variable, Segoe UI, sans-serif"
    fontSize: "24px"
    fontWeight: 700
  row-title:
    fontFamily: "Segoe UI Variable, Segoe UI, sans-serif"
    fontSize: "14px"
    fontWeight: 600
  group-title:
    fontFamily: "Segoe UI Variable, Segoe UI, sans-serif"
    fontSize: "13px"
    fontWeight: 600
  hint:
    fontFamily: "Segoe UI Variable, Segoe UI, sans-serif"
    fontSize: "12px"
    fontWeight: 400
  footnote:
    fontFamily: "Segoe UI Variable, Segoe UI, sans-serif"
    fontSize: "11px"
    fontWeight: 400
rounded:
  monitor: "4px"
  icon-tile: "9px"
  group: "12px"
  banner: "14px"
spacing:
  row-x: "16px"
  row-y: "14px"
  group-gap: "20px"
  pane-gutter: "22px"
components:
  button-primary:
    backgroundColor: "{colors.accent-blue}"
    rounded: "4px"
  group:
    backgroundColor: "{colors.surface-group}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.group}"
  row-icon-tile:
    backgroundColor: "{colors.tile-blue-start}"
    textColor: "{colors.glyph-on-tile}"
    rounded: "{rounded.icon-tile}"
    size: "34px"
  profile-tile:
    backgroundColor: "{colors.tile-blue-start}"
    textColor: "{colors.glyph-on-tile}"
    rounded: "10px"
    size: "38px"
---

# Design System: ScreenSoundSwitcher

## Overview

**Creative North Star: "The Control Panel"**

A quiet control panel that lives in the tray and is opened only when something needs setting. It is a Windows 11 utility, not a showcase: dark by default, translucent Mica underneath, settings gathered into grouped rows, and colour reserved for the things that mean something. The user is usually changing their setup in a hurry, so every screen answers one question first: what happens when I press apply, and can I get out of it?

Safety is part of the look. Anything that can blank a screen shows a clear countdown with a plain way back, and the emergency shortcut is always printed in the footer. The tone is calm and exact rather than playful: short Spanish labels that name the action ("Guardar los actuales", "Eliminar perfil"), no decoration that does not carry meaning.

**Key Characteristics:**
- Grouped rows instead of repeated cards; hairline dividers inside a group, generous space between groups.
- Colour carries meaning: one gradient per kind of setting (screens, sound, TV, shortcut, controller, extra actions) and one per kind of profile.
- Tonal layers on Mica; no drop shadows.
- Basic mode is complete on its own; advanced options sit in their own groups below.
- Motion is one short ease-in for new content and is skipped when Windows animations are off.

## Colors

A dark, near-neutral surface with one bright accent and a small family of deep gradients used only on icon tiles.

### Primary
- **Control Blue** (#0A84FF): the accent. Primary buttons, the selected profile's border and tint, the focus ring, text selection. It is the only colour that appears on interactive state.

### Secondary
- **Tile gradients** (see the frontmatter, each from a start to an end stop): the identity of a kind of setting or profile. Blue is screens, purple is the TV, teal is several screens, red-orange is sound, indigo is keyboard shortcuts, green is the controller, orange is extra actions. Every stop was chosen so a white glyph keeps at least 3:1 contrast; measured, the lowest is 3.05:1 (the orange start) and most are above 3.4:1.

### Neutral
- **Mica Base** (#202020 when the backdrop is unavailable): the window surface, with the system's Mica material and a faint blue-to-purple wash over it.
- **Group Surface** (#2B2B2B): a group or list item, a white veil of about 5% over the base.
- **Text** (#FFFFFF): primary text. Secondary text is the same colour at no less than 65% opacity.

### Named Rules
**The Meaning Rule.** Colour is never decoration. If a tile or accent does not tell the user what kind of thing it is or what is selected, it stays neutral.
**The Contrast Floor Rule.** Secondary text never drops below 65% opacity (4.7:1 on the light theme, 5.1:1 on the dark one), and a white glyph on a gradient keeps 3:1 or better.

## Typography

**Display Font:** Segoe UI Variable (with Segoe UI as fallback)
**Body Font:** Segoe UI Variable (with Segoe UI as fallback)

**Character:** The system face, used at a small number of clearly separated sizes. Hierarchy comes from weight and size, not from a second family.

### Hierarchy
- **Title** (700, 26px): the profile's name in the editor header.
- **Heading** (700, 24px): the list heading "Perfiles".
- **Row title** (600, 14px): the name of a setting or a profile in the list.
- **Group title** (600, 13px, 70% opacity): the heading above each group of settings. Sentence case, no uppercase labels.
- **Hint** (400, 12px, 70% opacity): the sentence under a control that says what it does or what to expect.
- **Footnote** (400, 11px, 65% opacity): the emergency shortcut, the version, the hotkey under a profile.

### Named Rules
**The No Eyebrow Rule.** A heading is never preceded by a small label above it; the heading carries its own weight.

## Layout

A fixed two-pane window: a 270px profile list on the left and the editor on the right, with a 22px gutter and the footer below. The editor scrolls; the list scrolls separately. Minimum size is 760 by 540, and at that width a long profile name is trimmed with an ellipsis and the mode switch shortens to "Avanzado".

Spacing is a handful of fixed steps: 16px side and 14px vertical padding inside a row, 20px above each group title and 8px below it, 6px between list items. A group's dividers start under the text, past the icon tile, so the tile column stays clear.

An inline banner may appear above the content (the one-time screen-safety notice). It never blocks the window: the window stays usable and draggable while it is shown.

## Elevation & Depth

Depth is tonal, not cast. Surfaces are slightly lighter veils over the Mica base; there are no drop shadows. The selected profile gets a 1px accent border and a 16% accent tint, and keyboard focus gets a 3px accent border on the focused monitor box, so state is always a colour or border change and never a shadow.

### Named Rules
**The Flat-Layers Rule.** Surfaces are separated by tone and by 1px strokes. A shadow is never used to say "this is on top".

## Shapes

Soft and consistent: groups and list items use a 12px radius, the icon tiles 9 to 13px (larger tile, larger radius), the banner 14px, and the monitor boxes in the arrangement view 4px, like small real screens. Icon tiles are rounded squares, never circles. Strokes are 1px; the selected and focused states thicken to 1px accent and 3px accent respectively.

## Components

### Buttons
- **Shape:** the platform's rounded rectangle (4px).
- **Primary:** accent fill (#0A84FF) for the one action that matters on a screen: "Crear con los monitores actuales", "Aplicar perfil", "Actualizar".
- **Default:** the neutral control fill; "Guardar cambios", "Guardar los actuales".
- **Danger:** red fill, only for deleting a profile.
- **Busy:** "Aplicar perfil" is disabled and reads "Aplicando…" while a profile is being applied.

### Cards / Containers
- **Group:** one rounded container per topic (Pantallas, Sonido, Atajos, Tele, Acciones extra), rows inside separated by hairlines. Groups are never nested.
- **Row:** a 34px gradient icon tile, then a row title, the control, and an optional hint underneath.

### List item (profile)
- A 38px gradient tile whose colour says the profile's kind, then the name, a summary line and the shortcut. Selected: accent border and tint (fades in over 160ms); hover: a lighter surface; keyboard focus: a visible accent border.

### Inputs / Fields
- Platform text boxes and combo boxes on the control fill. Selection and caret follow the palette (accent selection at 45%). Shortcut fields are read-only and record the key combination pressed in them.

### Navigation
- There is no navigation: one list and one editor. Advanced mode adds groups to the editor; it does not add screens.

### Arrangement view (signature component)
- Monitors are drawn as proportional boxes inside a recessed panel, draggable like Windows' display settings. The primary monitor is the blue gradient with white text; the others are a neutral surface. They snap edge to edge. They can also be moved with the arrow keys, and P marks the primary one, so the control is fully usable without a mouse.

### Confirmation with countdown (signature component)
- A small topmost window, shown after a layout change, with a warning icon, a one-line question, a plain sentence about what happens if nothing is pressed, a progress bar that runs out, and two buttons: "Mantener" (primary) and "Volver a la anterior". If nothing is pressed, the old layout returns on its own.

### Tray menu
- The profile list with icons and shortcuts, then window, sound and update actions, then "Restaurar pantallas" and "Salir". A header shows the app name, version and the active profile.

## Do's and Don'ts

### Do:
- **Do** group settings by topic in one container with hairline dividers, rather than giving every setting its own card.
- **Do** give each kind of setting its own gradient tile and keep it the same everywhere it appears (list, editor, header).
- **Do** keep a white glyph on a gradient at 3:1 or better, and secondary text at 65% opacity or more.
- **Do** show a clear way back (countdown, emergency shortcut) before anything that can blank a screen.
- **Do** make every control reachable and operable from the keyboard, including the arrangement view.
- **Do** animate only new content, once, with a short ease-out, and skip it when Windows animations are off.

### Don't:
- **Don't** use colour as decoration; a gradient or accent that does not say what something is stays neutral.
- **Don't** use drop shadows or glows to separate surfaces; use tone and 1px strokes.
- **Don't** put a small label above a heading, or nest a card inside a card.
- **Don't** use symbols or emoji where an icon belongs; button names for the controller are words, not arrows.
- **Don't** block the window with a modal for something that does not need a decision; use an inline banner.
- **Don't** let secondary text fade below 65% opacity on either theme.
