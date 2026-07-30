using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;
using System.Text;

namespace AIBar.Packaging.Supervisor;

public enum CommittedChildObjectKind : byte
{
    Directory = 1
}

public sealed class CommittedFileIdentity
{
    private readonly byte[] _fileId;

    internal CommittedFileIdentity(ulong volumeSerialNumber, byte[] fileId)
    {
        VolumeSerialNumber = volumeSerialNumber;
        _fileId = fileId.ToArray();
    }

    public ulong VolumeSerialNumber { get; }
    public byte[] FileId => _fileId.ToArray();

    internal void Zero() => CryptographicOperations.ZeroMemory(_fileId);
}

public sealed class CommittedChildRecord
{
    private readonly byte[] _leafTag;
    private readonly byte[] _fileId;

    internal CommittedChildRecord(byte[] leafTag, ulong volumeSerialNumber, byte[] fileId, CommittedChildObjectKind kind, bool isReparsePoint)
    {
        _leafTag = leafTag.ToArray();
        VolumeSerialNumber = volumeSerialNumber;
        _fileId = fileId.ToArray();
        Kind = kind;
        IsReparsePoint = isReparsePoint;
    }

    public byte[] LeafTag => _leafTag.ToArray();
    public ulong VolumeSerialNumber { get; }
    public byte[] FileId => _fileId.ToArray();
    public CommittedChildObjectKind Kind { get; }
    public bool IsReparsePoint { get; }

    internal ReadOnlySpan<byte> TagSpan => _leafTag;
    internal ReadOnlySpan<byte> FileIdSpan => _fileId;
    internal void Zero()
    {
        CryptographicOperations.ZeroMemory(_leafTag);
        CryptographicOperations.ZeroMemory(_fileId);
    }
}

public sealed class CommittedChildEvidence : IDisposable
{
    public const string VersionV1 = "CommittedChildEvidence/v1";
    private readonly byte[] _digest;
    private readonly byte[] _correlationKey;
    private bool _disposed;

    private CommittedChildEvidence(CommittedFileIdentity root, CommittedFileIdentity quarantineParent, byte[] digest, byte[] correlationKey, IReadOnlyList<CommittedChildRecord> children)
    {
        Root = root;
        QuarantineParent = quarantineParent;
        _digest = digest.ToArray();
        _correlationKey = correlationKey.ToArray();
        Children = children;
    }

    public string Version => VersionV1;
    public CommittedFileIdentity Root { get; }
    public CommittedFileIdentity QuarantineParent { get; }
    public byte[] Digest => _digest.ToArray();
    public byte[] CorrelationKey => _correlationKey.ToArray();
    public IReadOnlyList<CommittedChildRecord> Children { get; }

    internal static bool TryCreate(DirectoryObservation root, DirectoryObservation parent, IReadOnlyCollection<(string Leaf, DirectoryObservation Observation)> children, out CommittedChildEvidence? evidence)
    {
        evidence = null;
        byte[]? rootFileId = null, parentFileId = null, key = null, digest = null;
        var records = new List<CommittedChildRecord>();
        try
        {
            if (children.Count > 16 || root.IsReparsePoint || parent.IsReparsePoint || !TryGetFileId(root.Identity, out rootFileId) || !TryGetFileId(parent.Identity, out parentFileId))
                return false;

            key = RandomNumberGenerator.GetBytes(32);
            var tags = new HashSet<string>(StringComparer.Ordinal);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (leaf, observation) in children)
            {
                if (!DirectoryCapability.IsEvidenceLeaf(leaf) || observation.IsReparsePoint || observation.Identity.VolumeSerialNumber != root.Identity.VolumeSerialNumber || !TryGetFileId(observation.Identity, out var childFileId))
                    return false;

                byte[]? leafBytes = null;
                byte[]? tag = null;
                try
                {
                    leafBytes = Encoding.Unicode.GetBytes(leaf);
                    tag = HMACSHA256.HashData(key, leafBytes);
                    if (!tags.Add(Convert.ToHexString(tag)) || !identities.Add($"{observation.Identity.VolumeSerialNumber:X16}:{Convert.ToHexString(childFileId)}")) return false;
                    records.Add(new(tag, observation.Identity.VolumeSerialNumber, childFileId, CommittedChildObjectKind.Directory, false));
                }
                finally
                {
                    if (leafBytes is not null) CryptographicOperations.ZeroMemory(leafBytes);
                    if (tag is not null) CryptographicOperations.ZeroMemory(tag);
                    CryptographicOperations.ZeroMemory(childFileId);
                }
            }

            records.Sort((left, right) => left.TagSpan.SequenceCompareTo(right.TagSpan));
            digest = ComputeDigest(root.Identity.VolumeSerialNumber, rootFileId, parent.Identity.VolumeSerialNumber, parentFileId, records);
            evidence = new(new(root.Identity.VolumeSerialNumber, rootFileId), new(parent.Identity.VolumeSerialNumber, parentFileId), digest, key, records);
            return true;
        }
        catch { return false; }
        finally
        {
            if (evidence is null) foreach (var record in records) record.Zero();
            if (digest is not null) CryptographicOperations.ZeroMemory(digest);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (rootFileId is not null) CryptographicOperations.ZeroMemory(rootFileId);
            if (parentFileId is not null) CryptographicOperations.ZeroMemory(parentFileId);
        }
    }

    public bool HasValidDigest()
    {
        if (_disposed) return false;
        var calculated = ComputeDigest(Root.VolumeSerialNumber, Root.FileId, QuarantineParent.VolumeSerialNumber, QuarantineParent.FileId, Children);
        try { return CryptographicOperations.FixedTimeEquals(_digest, calculated); }
        finally { CryptographicOperations.ZeroMemory(calculated); }
    }

    private static byte[] ComputeDigest(ulong rootVolume, byte[] rootFileId, ulong parentVolume, byte[] parentFileId, IReadOnlyList<CommittedChildRecord> children)
    {
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
        {
            writer.Write(VersionV1);
            writer.Write(rootVolume); writer.Write(rootFileId);
            writer.Write(parentVolume); writer.Write(parentFileId);
            writer.Write(children.Count);
            foreach (var child in children)
            {
                writer.Write(child.TagSpan); writer.Write(child.VolumeSerialNumber); writer.Write(child.FileIdSpan);
                writer.Write((byte)child.Kind); writer.Write(child.IsReparsePoint);
            }
        }
        return SHA256.HashData(bytes.ToArray());
    }

    private static bool TryGetFileId(DirectoryIdentity identity, out byte[] fileId)
    {
        fileId = [];
        if (identity.FileId.Length != 32) return false;
        try
        {
            fileId = Convert.FromHexString(identity.FileId);
            return fileId.Length == 16;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CryptographicOperations.ZeroMemory(_digest);
        CryptographicOperations.ZeroMemory(_correlationKey);
        Root.Zero(); QuarantineParent.Zero();
        foreach (var child in Children) child.Zero();
    }
}

public sealed class CommittedQuarantineCapability : IDisposable
{
    private DirectoryCapability? _owner;
    private bool _disposed;

    internal CommittedQuarantineCapability(DirectoryCapability owner, DirectoryObservation root, DirectoryObservation parent, CommittedChildEvidence evidence)
    {
        _owner = owner;
        Source = root;
        QuarantineParent = parent;
        Evidence = evidence;
    }

    public DirectoryObservation Source { get; }
    public DirectoryObservation QuarantineParent { get; }
    public CommittedChildEvidence Evidence { get; }
    internal SafeFileHandle RootHandle => _owner?.RootHandle ?? throw new ObjectDisposedException(nameof(CommittedQuarantineCapability));
    internal SafeFileHandle QuarantineParentHandle => _owner?.QuarantineParentHandle ?? throw new ObjectDisposedException(nameof(CommittedQuarantineCapability));
    internal bool HandlesReleased => _owner is null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Evidence.Dispose();
        _owner?.Dispose();
        _owner = null;
    }
}
