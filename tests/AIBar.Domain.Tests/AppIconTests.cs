using System.Drawing;
using System.Reflection;
using System.Xml.Linq;
using AIBar.Desktop;
using Forms = System.Windows.Forms;

namespace AIBar.Domain.Tests;

// The liquid-gold brain icon: one .ico drives the executable, the windows and the tray.
public sealed class AppIconTests
{
    private static string IconPath => Path.Combine(RepositoryRoot(), "src", "AIBar.Desktop", "Assets", "AIBar.ico");

    [Fact]
    public void Icon_file_carries_tray_glyph_and_tile_sizes()
    {
        var bytes = File.ReadAllBytes(IconPath);
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, bytes[..4]);
        var count = BitConverter.ToUInt16(bytes, 4);
        var sizes = Enumerable.Range(0, count).Select(index => bytes[6 + 16 * index] == 0 ? 256 : bytes[6 + 16 * index]);
        Assert.Equal(new[] { 16, 20, 24, 32, 48, 64, 256 }, sizes);
    }

    [Fact]
    public void Desktop_executable_declares_the_AIBar_icon()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "AIBar.Desktop", "AIBar.Desktop.csproj"));
        Assert.Equal(@"Assets\AIBar.ico", Assert.Single(project.Descendants("ApplicationIcon")).Value);
    }

    [Fact]
    public async Task Windows_tray_shows_the_AIBar_icon_at_the_system_small_size()
    {
        await using var tray = new WindowsTrayRuntime();
        var notifyIcon = (Forms.NotifyIcon)typeof(WindowsTrayRuntime).GetField("_icon", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tray)!;
        using var expected = new Icon(IconPath, Forms.SystemInformation.SmallIconSize);

        Assert.Equal(expected.Size, notifyIcon.Icon!.Size);
        Assert.Equal(Pixels(expected), Pixels(notifyIcon.Icon));
        Assert.Equal("AIBar", notifyIcon.Text);
    }

    private static int[] Pixels(Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        return Enumerable.Range(0, bitmap.Width * bitmap.Height).Select(index => bitmap.GetPixel(index % bitmap.Width, index / bitmap.Width).ToArgb()).ToArray();
    }

    private static string RepositoryRoot() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); ; directory = directory.Parent!) if (File.Exists(Path.Combine(directory.FullName, "AIBar.sln"))) return directory.FullName; }
}
