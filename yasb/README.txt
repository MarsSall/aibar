AIBar YASB quota widget — private beta

This optional integration uses YASB's stock CustomWidget. AIBar remains the only quota authority; the widget reads only its sanitized snapshot. There is no installer, updater, package-manager delivery, or public release.

Setup
1. Manually extract the unsigned, self-contained Windows x64 private-beta ZIP to one stable location you choose: <AIBarRoot>.
2. Copy custom-widget.example.yaml into your YASB configuration and retain <AIBarRoot> as the location you configure once.
3. Copy or customize custom-widget.example.css in your YASB stylesheet.
4. Start AIBar normally. The widget reads its local sanitized snapshot; left-click uses only AIBar.Desktop.exe --show.

Presentation
Only supplied quota windows appear, in 5h then 7d order. Weekly-only input can display "7d: 35% | current" without an empty 5h slot.
Human warnings use "refresh failed", "authentication failed", and "service unavailable". Duplicate unavailable/disabled warnings are omitted from visible copy, not from authoritative CSS classes.
The tooltip shows state, any distinct warning, supplied-window resets, then source age. Invalid or missing input fails closed as "unavailable", with "unavailable | age: --" in the tooltip.
The example stylesheet retains its padding, rounded corners, state backgrounds, and warning underlines without fading unavailable/disabled text or adding a partial class.

Service boundary
The quota service is an unsupported private integration. The widget does not refresh quotas, authenticate, access credentials, databases, logs, or analytics, and it does not imply public support or service availability.

Upgrade
Stop AIBar before manually replacing the extracted directory. If AIBar cannot be stopped, stop the upgrade and leave the current directory in place.

Rollback
1. Stop AIBar.
2. From <AIBarRoot>, run yasb\remove-aibar-quota.ps1 with no arguments.
3. Continue only when the cleanup command exits 0.
4. Remove or replace the YASB assets.
5. Verify that the fixed snapshot is absent or is a schema-v1 disabled document with null sourceRetrievedAt, fiveHour, and weekly values.

If cleanup fails, stop removal. Do not remove or replace the assets until cleanup succeeds.
