using FilesMate.App.Services;

namespace FilesMate.App.Tests.Services;

public sealed class TransferFeedbackTests
{
    [Fact]
    public void PartialTransferIncludesEveryFailureAndCounts()
    {
        var result = new ShelfTransferResult([new("a", "b")], ["locked.txt: locked", "denied.txt: denied"], false) { Skipped = 3 };
        Assert.True(TransferFeedback.NeedsAttention(result));
        var message = TransferFeedback.Format(result);
        Assert.Contains("1", message);
        Assert.Contains("3", message);
        Assert.Contains("2", message);
        Assert.Contains("locked.txt: locked", message);
        Assert.Contains("denied.txt: denied", message);
    }

    [Fact]
    public void SkipAndCancellationAreVisibleEvenWithoutErrors()
    {
        Assert.True(TransferFeedback.NeedsAttention(new([], [], false) { Skipped = 1 }));
        Assert.True(TransferFeedback.NeedsAttention(new([], [], true)));
        Assert.NotEqual(TransferFeedback.Format(new([], [], false)), TransferFeedback.Format(new([], [], true)));
        Assert.False(TransferFeedback.NeedsAttention(new([new("a", "b")], [], false)));
    }
}
