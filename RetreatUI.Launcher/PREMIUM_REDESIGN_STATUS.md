# Premium launcher redesign status

## Locked direction

The approved dark fantasy mockup is the implementation target, not loose inspiration.

## Architecture changes

- Replace the single scaled form/Viewbox composition with a responsive shell
- Add a real left navigation rail
- Separate game selection, release state, changelog and play actions into dedicated panels
- Preserve all existing updater, channel, path, install, verification and self-update services
- Keep text and controls native so DPI scaling remains sharp
- Use artwork only as background/branding assets, never as baked UI text

## Acceptance gates

- Sharp at 100%, 125%, 150% and 200% Windows scaling
- No clipped text or controls at minimum supported resolution
- CoA and TBC retain independent paths and release channels
- Update progress clearly shows checking, downloading, verifying, installing and complete states
- No public release until the running build is visually compared with the approved mockup

## Current work

The redesign is isolated on this branch so launcher 0.3.9 remains untouched while the shell is rebuilt.

Author: Retreat
