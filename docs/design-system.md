# Local GitHub Library Design System

## Product intent

Local GitHub Library is a dense desktop research workspace. The primary task is
to move from a topic or ranking query to a trustworthy repository shortlist,
inspect one repository, and save it locally. Visual hierarchy must therefore
follow this order:

1. Current workspace and task controls
2. Repository result list
3. Selected repository detail
4. Secondary history and utility actions

The visual direction is calm technical utility: compact, precise, and readable.
It is not an OLED showcase or a marketing landing page. Light mode is the
default; dark mode is a complete peer theme.

## Semantic tokens

### Light theme

| Token | Value | Use |
| --- | --- | --- |
| App background | `#F4F7FB` | Window and workspace canvas |
| Surface | `#FFFFFF` | Panels, command bars, dialogs |
| Surface alternate | `#EAF0F7` | Table headers, grouped controls |
| Surface subtle | `#F1F5F9` | Hover fills and empty regions |
| Text primary | `#142033` | Headings and body copy |
| Text secondary | `#506176` | Metadata, helper copy |
| Border | `#CCD6E3` | Panel and input boundaries |
| Divider | `#DCE4EE` | Low-emphasis separators |
| Primary | `#1F5FBF` | Primary action |
| Primary hover | `#184E9F` | Primary hover |
| Primary pressed | `#143F82` | Primary pressed |
| On primary | `#FFFFFF` | Text on primary actions |
| Selection subtle | `#E8F0FF` | Selected table rows |
| Focus ring | `#0B6FD8` | Keyboard focus |
| Success | `#0F7A58` | Growth and success |
| Warning | `#8A5200` | Model/configuration warnings |
| Danger | `#B42318` | Destructive and failure |

### Dark theme

| Token | Value | Use |
| --- | --- | --- |
| App background | `#0E141D` | Window and workspace canvas |
| Surface | `#151D28` | Panels, command bars, dialogs |
| Surface alternate | `#1B2634` | Table headers and grouped controls |
| Surface subtle | `#202C3B` | Hover fills and empty regions |
| Text primary | `#F2F6FB` | Headings and body copy |
| Text secondary | `#B8C5D6` | Metadata and helper copy |
| Border | `#46566D` | Panel and input boundaries |
| Divider | `#2B384A` | Low-emphasis separators |
| Primary | `#8FB6FF` | Primary action |
| Primary hover | `#A8C6FF` | Primary hover |
| Primary pressed | `#729DEB` | Primary pressed |
| On primary | `#08111E` | Text on primary actions |
| Selection subtle | `#253956` | Selected table rows |
| Focus ring | `#A8C6FF` | Keyboard focus |
| Success | `#4ADE80` | Growth and success |
| Warning | `#F7C35F` | Model/configuration warnings |
| Danger | `#FF8A80` | Destructive and failure |

All primary and secondary text pairs are designed to exceed WCAG AA 4.5:1.
Primary text on primary buttons is checked independently in each theme.

Measured pairs:

- Light primary text / surface: 16.35:1
- Light secondary text / surface: 6.34:1
- Light primary button text / primary: 6.09:1
- Dark primary text / surface: 15.63:1
- Dark secondary text / surface: 9.69:1
- Dark primary button text / primary: 9.29:1

## Typography

- Display and headings: `Segoe UI Variable Display`
- Body and controls: `Segoe UI Variable Text`
- IDs, counts, and numeric table columns: `Cascadia Mono`
- Scale: 12 helper, 13 dense body, 14 control, 16 section, 20 panel title,
  26 application title
- Body line height: 20-22 px
- Numeric columns use tabular-compatible monospaced figures to prevent jitter.

Long repository names and descriptions use a single-line ellipsis only in tables.
The complete value is always available as a tooltip. Detail panels wrap instead
of truncating.

## Spacing and geometry

- Spacing scale: 4, 8, 12, 16, 20, 24, 32
- Control height: 36 px; compact icon control: 28 px
- Card/panel radius: 10 px
- Control radius: 7 px
- Dialog radius: 14 px
- Panel spacing: 12 px
- Dense workspace inset: 20 px wide, 12 px between major blocks

## Component behavior

### Buttons

- Primary: one filled primary action per task area.
- Secondary: surface background with clear border.
- Hover: surface changes without size/layout movement.
- Pressed: deeper fill with a small 1 px visual offset only.
- Focus: 2 px high-contrast ring plus 2 px offset.
- Disabled: reduced contrast, unchanged geometry, no pointer affordance.

### Inputs and selectors

- Every control has a persistent visible label.
- Focus changes border and shows the shared focus ring.
- Hover remains subtle; focus is stronger than hover.
- Invalid or warning text is semantic text, not color alone.

### Tables

- Header uses alternate surface and semibold secondary text.
- Rows use a 34-38 px dense height.
- Hover and keyboard selection use distinct surfaces.
- Full row remains keyboard reachable through the DataGrid.
- Repository and description columns ellipsize with a tooltip.

### Panels and navigation

- Workspace tabs are a compact segmented navigation bar.
- Each workspace uses one command bar followed by bordered work panels.
- Draggable split lines remain neutral; focus and drag states are visible.
- The reset-layout icon remains a rounded square and gains a keyboard focus ring.

## Responsive desktop behavior

- Wide layout: target 1240 px, three-pane workspace.
- Compact layout: supported down to 980 px; panel minimums shrink before the
  window minimum is reached.
- Command bars retain all controls and let secondary table content scroll
  horizontally rather than clipping labels.
- Dialogs can shrink to 620 x 520 and scroll their content vertically.
- No action depends on hover; the full workflow remains keyboard operable.

## Theme switching

- Default: follow the Windows application light/dark preference at launch.
- Header control switches theme at runtime and updates all DynamicResource
  surfaces, text, borders, focus rings, and dialog backgrounds together.
- No data or business state is stored in the theme layer.

## Accessibility checks

- Keyboard focus remains visible on buttons, selectors, tabs, lists, tables, and
  splitter controls.
- Color is paired with text for warnings, growth, selection, and status.
- Small controls have an accessible name and tooltip.
- Tooltips expose full text where table cells use ellipsis.
- Dark and light themes are captured separately for visual regression.
