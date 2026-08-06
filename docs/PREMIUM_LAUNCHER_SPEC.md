# RetreatUI Launcher — Premium Redesign Specification

This document is the implementation contract for the production launcher redesign. The approved mockup is treated as a layout and visual-system specification, not as loose inspiration.

## Non-negotiable goals

- Preserve the working updater, install, validation, rollback, channel and game-launch backend.
- Replace the current form-like presentation with a premium native WPF launcher shell.
- Keep all text rendered natively in WPF. Never bake labels, version numbers or buttons into screenshots.
- Use artwork only as independent assets with gradients and masks layered in code.
- Maintain sharp rendering at 100%, 125%, 150% and 200% Windows scaling.
- No public release until the implementation is visually reviewed against the approved mockup.

## Window

- Design canvas: 1440 × 900.
- Minimum supported size: 1180 × 760.
- Custom dark title chrome with native minimize, maximize and close behaviour.
- Background: near-black blue with subtle warm gold edge lighting and restrained texture.
- Primary layout: 260 px sidebar plus flexible content area.

## Sidebar

- Brand block with RetreatUI emblem, RETREATUI and LAUNCHER.
- Navigation: Home, Conquest of Azeroth, The Burning Crusade, AddOns, Settings.
- Selected item uses a soft gold gradient, 1 px gold edge and left highlight.
- Discord/support card anchored near the bottom.
- Launcher version and update state at the bottom edge.

## Home/content layout

### Header

- RETREATUI wordmark centred over the content area.
- Short subtitle and restrained ornamental divider.
- Settings and native window controls remain visually separate.

### Game cards

- Two large cards: CoA and TBC.
- CoA: ember/red-black identity.
- TBC: fel-green/black identity.
- Each card contains native text for title, install state, path, version and channel.
- Selected card gets a warm gold border, soft glow and ACTIVE marker.
- Cards must remain readable without relying on baked artwork text.

### Status panel

- Compact state heading: Up to date, Update available, Repair required or Not installed.
- Installed version, latest version and selected channel.
- Channel action available without leaving the page.
- No large brown warning bars.

### Progress panel

Visual states:

1. Checking
2. Downloading
3. Verifying
4. Installing
5. Complete

- Each state uses a node and connecting progress line.
- The active step is highlighted; completed steps use a success state.
- Text status remains accessible for screen readers.

### Changelog

- Current release notes with compact version badge and date.
- Scrollable content with clear hierarchy.
- Full changelog action remains secondary.

### Primary action

- Large PLAY / UPDATE / INSTALL / REPAIR button in the bottom-right content area.
- Gold metallic treatment built with WPF gradients, borders and shadows.
- The action text and product subtitle are native text.
- Disabled and busy states remain visually deliberate, not greyed-out default controls.

## Footer

- Installation folder with edit/folder action.
- Connection state.
- Release channel.
- Current launcher update status.

## Implementation constraints

- Existing named controls and backend event hooks must be preserved or explicitly mapped before replacement.
- Backend services must not be rewritten as part of the visual redesign.
- All visual resources should live in XAML resource dictionaries or dedicated theme classes.
- Avoid one giant `Viewbox`; use responsive Grid layouts with sensible MinWidth constraints.
- Avoid low-resolution watermarks. New raster artwork must be at least 2× its maximum rendered size.
- Decorative effects must remain subtle enough to keep text crisp.

## Acceptance checklist

- [ ] Layout visually matches the approved mockup at 1440 × 900.
- [ ] No clipped or blurred text at 100–200% scaling.
- [ ] CoA and TBC selection updates all content correctly.
- [ ] Stable/Beta selection still works.
- [ ] Installed/latest versions remain accurate.
- [ ] Install, update, repair and play actions still work.
- [ ] Launcher self-update still works.
- [ ] Paths can still be selected and opened.
- [ ] Release notes remain readable and scrollable.
- [ ] Keyboard focus, hover, disabled and busy states are styled.
- [ ] Build pipeline succeeds on GIGACHAD before any release.
