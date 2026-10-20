using System.Windows;
using System.Windows.Media.Animation;

namespace AIBar.Desktop;

internal static class NotificationMotion
{
    internal static DoubleAnimationUsingKeyFrames Animate(double from, double to, TimeSpan duration, bool exit, bool reduced)
    {
        var result = new DoubleAnimationUsingKeyFrames();
        result.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (reduced) result.KeyFrames.Add(new LinearDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration)));
        else result.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration),
            exit ? new KeySpline(.7, 0, 1, .5) : new KeySpline(0, 0, 0, 1)));
        return result;
    }
}
