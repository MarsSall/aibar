namespace AIBar.Packaging.Supervisor;

public static class Cleanup
{
    public static NativeReadinessResult Commit(DirectoryCapability capability, string leaf)
        => NativeRenameReadiness.Prove(capability, leaf);
}
