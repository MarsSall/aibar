using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AIBar.Desktop;

internal sealed class GlyphFontWindowsEnvironment : IGlyphFontEnvironment
{
    private const string FontsKey = @"Software\Microsoft\Windows NT\CurrentVersion\Fonts";
    public string WindowsFontsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
    public string UserFontsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");

    public IEnumerable<GlyphFontRegistration> ReadFontRegistrations(bool currentUser)
    {
        using var key = (currentUser ? Registry.CurrentUser : Registry.LocalMachine).OpenSubKey(FontsKey, writable: false);
        return key is null ? Array.Empty<GlyphFontRegistration>() : key.GetValueNames()
            .Select(name => new GlyphFontRegistration(name, key.GetValue(name) as string)).ToArray();
    }

    public bool FileExists(string path) => File.Exists(path);
    public Stream OpenRead(string path) => File.OpenRead(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void CopyFile(string source, string destination) => File.Copy(source, destination, overwrite: false);
    public void MoveFile(string source, string destination) => File.Move(source, destination, overwrite: true);
    public void DeleteFile(string path) => File.Delete(path);
    public void WriteUserFontRegistration(string name, string path)
    {
        using var key = Registry.CurrentUser.CreateSubKey(FontsKey, writable: true);
        key.SetValue(name, path, RegistryValueKind.String);
    }
    public void AddFontResource(string path)
    {
        if (AddFontResourceW(path) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void BroadcastFontChange()
    {
        // Bounded broadcast: a hung application must not indefinitely block Settings/startup.
        if (SendMessageTimeoutW(new IntPtr(0xffff), 0x001D, UIntPtr.Zero, IntPtr.Zero, 0x0002, 1000, out _) == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern int AddFontResourceW(string fileName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
}
