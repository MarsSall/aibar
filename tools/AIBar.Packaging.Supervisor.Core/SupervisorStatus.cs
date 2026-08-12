namespace AIBar.Packaging.Supervisor;

public enum SupervisorStatus
{
    Success, InvalidRequest, RootCreateFailed, RootIdentityChanged, ReparseDetected, JobCreateFailed,
    JobConfigFailed, ProcessStartFailed, JobAssignFailed, ProcessResumeFailed, Timeout, Cancelled,
    ProcessFailed, OutputDrainFailed, QuiescenceUnproved, CleanupRefused, CleanupPartial, InternalUnknown
}
