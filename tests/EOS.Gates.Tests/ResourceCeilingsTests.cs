using EOS.Gates;

namespace EOS.Gates.Tests;

public class ResourceCeilingsTests
{
    [Fact]
    public void ResourceCeilings_HoldsEverySixResourceProtectionCeiling_PerSpec16()
    {
        var ceilings = new ResourceCeilings(
            CpuCeilingPercent: 90,
            RamCeilingMegabytes: 8192,
            DiskCeilingMegabytes: 476000,
            ModelUsageCeilingTokens: 100000,
            ContextSizeCeilingTokens: 32000,
            BackgroundTasksCeilingCount: 4);

        Assert.Equal(90, ceilings.CpuCeilingPercent);
        Assert.Equal(8192, ceilings.RamCeilingMegabytes);
        Assert.Equal(476000, ceilings.DiskCeilingMegabytes);
        Assert.Equal(100000, ceilings.ModelUsageCeilingTokens);
        Assert.Equal(32000, ceilings.ContextSizeCeilingTokens);
        Assert.Equal(4, ceilings.BackgroundTasksCeilingCount);
    }

    [Fact]
    public void ResourceCeilings_IsImmutable_RecordEquality()
    {
        var first = new ResourceCeilings(90, 8192, 476000, 100000, 32000, 4);
        var second = new ResourceCeilings(90, 8192, 476000, 100000, 32000, 4);

        Assert.Equal(first, second);
    }
}
