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
        Assert.Equal(new[] { 16, 20, 24, 32, 40, 48, 64, 256 }, sizes);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(40)]
    [InlineData(48)]
    public void Tray_and_taskbar_frames_are_an_untiled_glyph_that_fills_the_frame(int size)
    {
        using var icon = new Icon(IconPath, size, size);
        using var bitmap = icon.ToBitmap();
        Assert.Equal(size, bitmap.Width);
        // No background tile: a rounded tile covers ~96% of the frame, the brain glyph leaves its corners clear.
        var opaque = Enumerable.Range(0, size * size).Count(index => bitmap.GetPixel(index % size, index / size).A >= 64);
        Assert.True(opaque < size * size * 0.9, $"{size} px frame is {opaque * 100 / (size * size)}% opaque: background tile");
        // The brain reaches the frame edges instead of floating inside padding.
        Assert.True(bitmap.GetPixel(0, size / 2).A > 0 || bitmap.GetPixel(1, size / 2).A > 0);
        Assert.True(bitmap.GetPixel(size / 2, size - 1).A > 0 || bitmap.GetPixel(size / 2, size - 2).A > 0);
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
