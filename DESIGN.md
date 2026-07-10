# Codex Quota Bar Design

## Intent
An unobtrusive companion bar that reads as part of the Codex desktop surface, never as a dashboard.

## Tokens
- Background: `#202123` dark / `#FFFFFF` light
- Text: `#ECECF1` dark / `#202123` light
- Muted: `#A7A7B4` dark / `#6B6B75` light
- Accent: `#10A37F`
- Warning: `#F59E0B`
- Height: 34 px; width: 620 px (responsive down to 440 px); horizontal padding: 12 px; corner radius: 8 px

## Components
- A centered, compact title and two quota groups, separated by a subtle divider. It occupies the Codex chrome band rather than its tool area.
- A 76 px progress rail with a solid accent fill, never decorative animation.
- Compact reset label uses relative time when under one day, local weekday/time otherwise.

## Accessibility
The bar has a high-contrast text mode through Windows high-contrast colors, non-color percentage labels, and a tooltip with exact quota state.
