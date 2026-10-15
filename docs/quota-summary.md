# Copy a quota summary

Select **Copy summary** in the popup to put the currently displayed quota snapshot on the clipboard. Copying does not refresh quota or collect more data.

The text lists weekly quota first, then 5-hour quota, with **percentage used**, reset countdowns, and freshness. Cached age and a safe warning are included when available. Unavailable windows are labeled explicitly; percentages use a decimal point and up to two decimal places.

- Copy is disabled when private integration is disabled or neither quota window is available.
- Only quota presentation fields are copied: no local analytics, model names, paths, credentials, raw service responses, or diagnostic codes.
- **Copied** confirms a successful clipboard write. **Clipboard unavailable** means the write failed; try another explicit click when the clipboard is free. AIBar does not retry automatically or open a dialog.
- Feedback clears when the displayed quota state changes. The existing Refresh action and outside-click dismissal are unchanged.

Automated tests inject a recording or failing clipboard writer; they never read, overwrite, lock, or restore the user's clipboard. See [local details](panel-details.md) for the collapsible analytics panel.
