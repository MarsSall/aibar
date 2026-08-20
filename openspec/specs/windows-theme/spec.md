# Windows Theme Specification

## Purpose

Define automatic semantic light/dark theme behavior for the existing WPF popup while preserving high-contrast accessibility and a usable fallback across Windows runtime variations.

## Requirements

### Requirement: Theme follows the Windows application preference automatically

AIBar MUST select semantic light or dark resources from the current Windows application theme without exposing an AIBar theme selector. Light and dark resource sets MUST provide matching semantic keys required by the popup.

#### Scenario: Windows light theme

- GIVEN high contrast is disabled and Windows application theme is light
- WHEN the popup is shown
- THEN the light semantic resources are active
- AND the popup's state, quota, and accessibility text remain available

#### Scenario: Windows dark theme

- GIVEN high contrast is disabled and Windows application theme is dark
- WHEN the popup is shown
- THEN the dark semantic resources are active
- AND the popup remains readable and structurally equivalent to the light theme

### Requirement: High contrast has precedence

When Windows high contrast is enabled, AIBar MUST prefer system high-contrast resources over custom light or dark palette choices. High-contrast presentation MUST retain textual state meaning, focus visibility, and keyboard/UI Automation semantics.

#### Scenario: High contrast overrides dark mode

- GIVEN Windows application theme is dark and high contrast is enabled
- WHEN the popup is rendered
- THEN high-contrast resources take precedence over the dark palette
- AND stale, error, percentage, and reset meaning remains available without color interpretation

#### Scenario: High contrast changes at runtime

- GIVEN the popup is open or hidden
- WHEN Windows high contrast changes
- THEN the next rendered state uses high-contrast resources
- AND focus and automation names remain valid

### Requirement: Theme transitions are dispatcher-safe and recoverable

AIBar MUST apply theme changes on the WPF dispatcher and MUST handle runtime light/dark/high-contrast transitions without replacing the popup authority or losing its current presentation state. A missed or off-dispatcher notification MUST be recoverable by reevaluating theme state when the popup opens.

#### Scenario: Runtime theme transition

- GIVEN the popup is open in the light theme
- WHEN Windows changes to dark theme
- THEN the semantic resources transition on the WPF dispatcher
- AND the popup keeps its current focusable controls, automation names, and visible status

#### Scenario: Notification is missed

- GIVEN the Windows theme changed while the popup was hidden and no notification was observed
- WHEN the user opens the popup
- THEN AIBar reevaluates the current theme before or during display
- AND the correct resources are active

#### Scenario: Notification arrives off-dispatcher

- GIVEN a theme source reports a change from a non-UI thread
- WHEN AIBar handles the notification
- THEN resource application is marshalled to the WPF dispatcher
- AND no cross-thread presentation failure is exposed to the user

### Requirement: Native surface enhancements are progressive

DWM corner, shadow, dark-mode, backdrop, or related Windows surface hints MAY enhance the popup only when supported. Their absence or failure MUST leave a usable opaque WPF surface with the same hierarchy, contrast, state text, focus behavior, and automation semantics.

#### Scenario: DWM enhancement is supported

- GIVEN the runtime supports a requested native surface enhancement
- WHEN the popup is shown
- THEN the enhancement may be applied
- AND it does not replace the semantic content or accessibility contract

#### Scenario: DWM enhancement is unavailable

- GIVEN the runtime does not support or rejects a native surface enhancement
- WHEN the popup is shown
- THEN the popup uses the opaque WPF fallback
- AND no feature failure prevents quota presentation or interaction

## Acceptance Criteria

- Light and dark Windows application themes select matching semantic resources without an AIBar selector.
- High contrast overrides custom palettes and preserves textual and automation semantics.
- Runtime changes, missed notifications, and unsupported DWM features remain safe and recoverable.
