namespace AIBar.Application;

internal enum LocalUsageAdmissionStatus { Admitted, PathChanged, Unavailable }
internal enum LocalUsageAdmissionLayout { Direct, PiEncodedDirectory }
// Path-only adapters require bracketing Prepare; this cannot eliminate the race between validation and their open.
internal interface ILocalUsageSourceAdmission { LocalUsageAdmissionStatus Validate(CancellationToken token); }
