using System.Drawing;
using MotionDesk.Widgets;
using Xunit;

namespace Opsis.Tests;

public class DesktopCustomizationTests
{
    [Fact]
    public void AnimatedGifRequiresTwoCompleteFramesAndATrailer()
    {
        byte[] header = [71, 73, 70, 56, 57, 97, 1, 0, 1, 0, 128, 0, 0, 0, 0, 0, 255, 255, 255];
        byte[] frame = [0x2c, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 0x44, 0x01, 0];

        AnimatedLockScreen.ValidateGif([.. header, .. frame, .. frame, 0x3b]);
        Assert.Throws<InvalidDataException>(() => AnimatedLockScreen.ValidateGif([.. header, .. frame, 0x3b]));
        Assert.Throws<InvalidDataException>(() => AnimatedLockScreen.ValidateGif([.. header, .. frame, .. frame]));
    }

    [Theory]
    [InlineData("LockScreen.jpg", true)]
    [InlineData("LockScreen___1920_1080_notdimmed.jpg", true)]
    [InlineData("LockScreen___3840_2160_notdimmed.jpg:alternate", false)]
    [InlineData("LockScreen___anything_notdimmed.jpg", false)]
    [InlineData("..\\LockScreen.jpg", false)]
    public void OnlyExpectedCacheFilesAreWritable(string name, bool accepted)
    {
        Assert.Equal(accepted, AnimatedLockScreen.IsImageName(name));
    }

    [Fact]
    public void DeskContainerPreservesItsRightGapWhenWorkingAreaExpands()
    {
        var area = new Rectangle(0, 0, 1920, 1040);
        var bounds = new Rectangle(1920 - 16 - 320, 16, 320, 384);
        var geometry = new DeskContainerGeometry("Screen", bounds, area);

        Assert.Equal(bounds, geometry.Place(area));
        Assert.Equal(new Rectangle(2560 - 16 - 320, 16, 320, 384),
            geometry.Place(new Rectangle(0, 0, 2560, 1440)));
    }
}
