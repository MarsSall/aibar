# Popup Presentation Specification

## Purpose

Define the reference-quality hierarchy and resilient Windows behavior of the existing WPF popup without replacing its host or redesigning secondary analytics business content.

## Requirements

### Requirement: Quota-first hierarchy is unmistakable

The popup MUST make the five-hour and weekly quota slots the permanent primary surface. Each slot MUST expose its label, percentage or `--` placeholder, reset information when available, and textual state. Secondary analytics MUST remain available but subordinate through grouped or progressive disclosure.

#### Scenario: Complete quota state

- GIVEN both quota windows are current
- WHEN the popup opens
- THEN the five-hour and weekly slots are the first unmistakable information group
- AND both slots show percentage and reset information
- AND secondary analytics do not compete with the primary quota group

#### Scenario: Missing quota window

- GIVEN one quota window is unavailable
- WHEN the popup opens
- THEN both permanent slots remain visible
- AND the missing slot shows an explicit `--` or equivalent unavailable placeholder
- AND no value is fabricated from the other window

#### Scenario: Secondary analytics are disclosed

- GIVEN detailed local analytics are available
- WHEN the user inspects secondary content
- THEN it remains accessible in the existing popup
- AND its presentation is grouped or subordinate without becoming a duplicate YASB panel

### Requirement: Status meaning is not color-only

The popup MUST communicate refreshing, stale, unavailable, disabled, and error meaning through text, labels, icons, or accessible names in addition to color. Percentage and reset information MUST remain readable in every supported theme and state.

#### Scenario: Stale cached values

- GIVEN cached quota values are retained after a failed refresh
- WHEN the popup is rendered
- THEN the values remain visible
- AND a textual or accessible stale/degraded indication and source age are visible
- AND color is not the sole indication of the failure

#### Scenario: Disabled values

- GIVEN privacy controls have disabled or cleared external quota disclosure
- WHEN the popup is rendered
- THEN the quota slots show disabled or unavailable placeholders
- AND no previous value remains visible as if current

### Requirement: Accessibility semantics remain stable

The popup MUST preserve keyboard traversal, visible focus, UI Automation names, accessible percentage and reset information, and non-interactive progress semantics. Visual refinement MUST NOT make quota state depend on animation or pointer-only interaction.

#### Scenario: Keyboard traversal

- GIVEN a keyboard user opens the popup
- WHEN focus moves through quota cards, grouped secondary content, and actions
- THEN focus order follows the visual reading order
- AND every interactive control has visible focus
- AND non-interactive progress visuals do not trap focus

#### Scenario: UI Automation inspection

- GIVEN an accessibility client inspects the popup
- WHEN it queries both quota slots and their actions
- THEN the quota labels, percentages, reset information, state, and action names are exposed
- AND the names remain present after theme or state transitions

### Requirement: Popup placement respects DPI and work area

The popup MUST be placed relative to its invoking tray or host location, respect the active monitor work area and taskbar edge, and remain fully usable at 100%, 125%, 150%, and 200% DPI. Placement MUST clamp or adjust within the available work area rather than allowing supported content to clip off-screen.

#### Scenario: Popup opens near a work-area edge

- GIVEN the tray is near any taskbar or monitor edge
- WHEN the popup opens
- THEN it is positioned within the active work area or an equivalent visible safe region
- AND the quota slots, footer, and accessible controls are not clipped

#### Scenario: DPI changes the available layout

- GIVEN the popup is opened at 100%, 125%, 150%, or 200% DPI
- WHEN its layout is measured
- THEN the content remains readable and unclipped
- AND the hierarchy, focus order, and automation relationships remain intact

#### Scenario: Narrow work area

- GIVEN the available work area is narrower than the preferred popup size
- WHEN the popup is shown
- THEN it adapts or clamps to the available area
- AND users can still reach both quota slots and the stable action/footer area

### Requirement: Surface treatment has a complete fallback

The popup MAY use restrained Windows-native elevation, borders, corners, or shadows, but those treatments MUST remain progressive enhancements. The existing WPF host MUST remain the usable fallback, and visual treatment MUST NOT alter single-instance ownership, popup lifecycle, high-contrast precedence, or secondary content availability.

#### Scenario: Native treatment is unavailable

- GIVEN the Windows runtime does not support a selected surface treatment
- WHEN the popup is shown
- THEN an opaque WPF surface is used
- AND all quota, status, accessibility, placement, and interaction requirements remain satisfied

## Explicit Scope Boundaries

This capability MUST NOT introduce a WinUI rewrite, replace the WPF host, create a duplicate YASB analytics panel, or add cost, spend, trend, forecasting, or ETA presentation.

## Acceptance Criteria

- Both permanent quota slots lead the popup and remain available as values or placeholders.
- Status, stale age, errors, focus, automation, and reset meaning are not color-only.
- Supported DPI and work-area cases remain unclipped, with a usable opaque WPF fallback.
