using NUnit.Framework;
using Throughput.Sim;

namespace Throughput.SimTests
{
public sealed class BandwidthInvariantTests
{
    [Test]
    public void OverCapacityDemandRemainsVisibleUntilUplinkIsUpgraded()
    {
        var world = new DcWorld();
        world.ReceiveAdvance(10_000f);

        Place(world, BuildingKind.Pdu, 12, 8);
        Building firstCpu = Place(world, BuildingKind.CpuRack, 5, 8);
        Place(world, BuildingKind.CpuRack, 6, 7);
        Place(world, BuildingKind.CpuRack, 7, 8);
        Building firstGpu = Place(world, BuildingKind.GpuRack, 12, 5);
        Building newestGpu = Place(world, BuildingKind.GpuRack, 12, 11);
        Step(world, 100);

        Assert.Multiple(() =>
        {
            Assert.That(firstCpu.PlacedTick, Is.EqualTo(newestGpu.PlacedTick));
            Assert.That(world.BandwidthUsed, Is.EqualTo(11f));
            Assert.That(world.BandwidthAccepted, Is.EqualTo(7f));
            Assert.That(firstGpu.Producing, Is.True);
            Assert.That(newestGpu.NoUplinkFlag, Is.True);
            Assert.That(newestGpu.ServedPf, Is.Zero);
            Assert.That(newestGpu.RevenueRate, Is.Zero);
        });

        Assert.That(world.BuyUplink(), Is.True);
        world.Step();

        Assert.Multiple(() =>
        {
            Assert.That(world.BandwidthCap, Is.EqualTo(20f));
            Assert.That(world.BandwidthUsed, Is.EqualTo(11f));
            Assert.That(world.BandwidthAccepted, Is.EqualTo(11f));
            Assert.That(newestGpu.NoUplinkFlag, Is.False);
            Assert.That(newestGpu.Producing, Is.True);
        });
    }

    private static Building Place(DcWorld world, BuildingKind kind, int x, int y)
    {
        Building building = world.TryPlace(kind, x, y);
        Assert.That(building, Is.Not.Null, $"Expected {kind} placement at ({x}, {y}) to succeed");
        return building;
    }

    private static void Step(DcWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++) world.Step();
    }
}
}
