using NUnit.Framework;
using Throughput.Sim;

namespace Throughput.SimTests
{
public sealed class PowerInvariantTests
{
    [Test]
    public void TurningOffSupplyingPduStopsRackServiceAndRevenue()
    {
        var world = new DcWorld();
        Building rack = world.TryPlace(BuildingKind.CpuRack, 7, 8);

        Step(world, 80);

        Assert.That(rack.ServedPf, Is.GreaterThan(0f));
        Assert.That(rack.RevenueRate, Is.GreaterThan(0f));

        int starterPduId = world.BuildingIdAt(6, 8);
        world.ToggleBuilding(starterPduId);
        world.Step();

        Assert.Multiple(() =>
        {
            Assert.That(rack.ServedPf, Is.Zero);
            Assert.That(rack.RevenueRate, Is.Zero);
            Assert.That(world.RevenuePerSec, Is.Zero);
        });
    }

    [Test]
    public void TurningOffRackClearsItsRevenueImmediately()
    {
        var world = new DcWorld();
        Building rack = world.TryPlace(BuildingKind.CpuRack, 7, 8);
        Step(world, 80);
        Assert.That(rack.RevenueRate, Is.GreaterThan(0f));

        world.ToggleBuilding(rack.Id);
        world.Step();

        Assert.Multiple(() =>
        {
            Assert.That(rack.ServedPf, Is.Zero);
            Assert.That(rack.RevenueRate, Is.Zero);
            Assert.That(world.RevenuePerSec, Is.Zero);
        });
    }

    [Test]
    public void UnpoweredCracDoesNotCoolASeparatelyPoweredRack()
    {
        var world = new DcWorld();
        Building coolingPdu = world.TryPlace(BuildingKind.Pdu, 11, 8);
        Building gpu = world.TryPlace(BuildingKind.GpuRack, 8, 8);
        world.TryPlace(BuildingKind.Crac, 10, 8);
        Step(world, 80);
        Assert.That(gpu.TileTemp, Is.LessThan(Balance.HotTemp));

        world.ToggleBuilding(coolingPdu.Id);
        world.Step();

        Assert.That(gpu.TileTemp, Is.GreaterThanOrEqualTo(Balance.HotTemp));
    }

    [Test]
    public void RackBootingDuringPduOutageDoesNotReceivePower()
    {
        var world = new DcWorld();
        Building starterPdu = world.Buildings[world.BuildingIdAt(6, 8)];
        starterPdu.State = BuildingState.TrippedDark;
        starterPdu.DarkRemaining = Balance.DarkSeconds;
        Building rack = world.TryPlace(BuildingKind.CpuRack, 7, 8);

        Step(world, 70);

        Assert.Multiple(() =>
        {
            Assert.That(starterPdu.State, Is.EqualTo(BuildingState.TrippedDark));
            Assert.That(rack.State, Is.EqualTo(BuildingState.Booting));
            Assert.That(rack.BootRemaining, Is.EqualTo(rack.Spec.BootSeconds));
            Assert.That(rack.ServedPf, Is.Zero);
            Assert.That(rack.RevenueRate, Is.Zero);
            Assert.That(world.FeedLoadKw, Is.EqualTo(Balance.Spec(BuildingKind.Uplink).DrawKw));
        });
    }

    [Test]
    public void ChildBehindNewPduWaitsForParentBeforeBooting()
    {
        var world = new DcWorld();
        world.ReceiveAdvance(10_000f);
        Building pdu = world.TryPlace(BuildingKind.Pdu, 12, 8);
        Building rack = world.TryPlace(BuildingKind.CpuRack, 12, 5);
        float initialRackBoot = rack.BootRemaining;

        Step(world, 5);

        Assert.Multiple(() =>
        {
            Assert.That(pdu.State, Is.EqualTo(BuildingState.Booting));
            Assert.That(rack.HasPower, Is.False);
            Assert.That(rack.BootRemaining, Is.EqualTo(initialRackBoot));
        });

        for (int i = 0; i < 40 && pdu.State != BuildingState.Online; i++) world.Step();
        Assert.That(pdu.State, Is.EqualTo(BuildingState.Online));
        world.Step();

        Assert.Multiple(() =>
        {
            Assert.That(rack.HasPower, Is.True);
            Assert.That(rack.BootRemaining, Is.LessThan(initialRackBoot));
        });
    }

    [Test]
    public void BreakerTripClearsSubtreePowerAndFeedLoadInTheTripTick()
    {
        var world = new DcWorld();
        world.ReceiveAdvance(10_000f);
        Building firstGpu = world.TryPlace(BuildingKind.GpuRack, 5, 8);
        Building secondGpu = world.TryPlace(BuildingKind.GpuRack, 7, 8);
        Building thirdGpu = world.TryPlace(BuildingKind.GpuRack, 6, 9);
        Building starterPdu = world.Buildings[world.BuildingIdAt(6, 8)];

        for (int i = 0; i < 100 && starterPdu.State != BuildingState.TrippedDark; i++)
            world.Step();

        Assert.Multiple(() =>
        {
            Assert.That(starterPdu.State, Is.EqualTo(BuildingState.TrippedDark));
            Assert.That(starterPdu.HasPower, Is.False);
            Assert.That(firstGpu.HasPower, Is.False);
            Assert.That(secondGpu.HasPower, Is.False);
            Assert.That(thirdGpu.HasPower, Is.False);
            Assert.That(world.FeedLoadKw, Is.EqualTo(Balance.Spec(BuildingKind.Uplink).DrawKw));
            Assert.That(world.TripEvents, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void BreakerSubtreeRecoversInStagesAndResumesService()
    {
        var world = new DcWorld();
        world.ReceiveAdvance(10_000f);
        Building firstGpu = world.TryPlace(BuildingKind.GpuRack, 5, 8);
        Building secondGpu = world.TryPlace(BuildingKind.GpuRack, 7, 8);
        Building thirdGpu = world.TryPlace(BuildingKind.GpuRack, 6, 9);
        Building crac = world.TryPlace(BuildingKind.Crac, 6, 10);
        Building starterPdu = world.Buildings[world.BuildingIdAt(6, 8)];
        Assert.That(crac, Is.Not.Null);

        for (int i = 0; i < 100 && starterPdu.State != BuildingState.TrippedDark; i++)
            world.Step();
        Assert.That(starterPdu.State, Is.EqualTo(BuildingState.TrippedDark));

        world.ToggleBuilding(secondGpu.Id);
        world.ToggleBuilding(thirdGpu.Id);
        Step(world, 260);

        Assert.Multiple(() =>
        {
            Assert.That(starterPdu.State, Is.EqualTo(BuildingState.Online));
            Assert.That(firstGpu.Producing, Is.True);
            Assert.That(firstGpu.RevenueRate, Is.GreaterThan(0f));
            Assert.That(secondGpu.Producing, Is.False);
            Assert.That(thirdGpu.Producing, Is.False);
            Assert.That(secondGpu.State, Is.Not.EqualTo(BuildingState.TrippedDark));
            Assert.That(thirdGpu.State, Is.Not.EqualTo(BuildingState.TrippedDark));
            Assert.That(world.TripEvents, Has.Count.EqualTo(1));
        });

        world.ToggleBuilding(secondGpu.Id);
        Step(world, 12);
        Assert.Multiple(() =>
        {
            Assert.That(secondGpu.Producing, Is.True);
            Assert.That(world.TripEvents, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void HeatShutdownRackStopsGeneratingHeatAndCools()
    {
        var world = new DcWorld();
        world.ReceiveAdvance(10_000f);
        Building firstGpu = world.TryPlace(BuildingKind.GpuRack, 5, 8);
        Building secondGpu = world.TryPlace(BuildingKind.GpuRack, 7, 8);

        for (int i = 0; i < 100 && firstGpu.State != BuildingState.HeatShutdown; i++)
            world.Step();
        Assert.That(firstGpu.State, Is.EqualTo(BuildingState.HeatShutdown));
        Assert.That(secondGpu.State, Is.EqualTo(BuildingState.HeatShutdown));
        float cashWhileHot = world.Cash;
        Assert.That(world.RestartHeatShutdown(firstGpu.Id), Is.False);
        Assert.That(world.Cash, Is.EqualTo(cashWhileHot));

        world.Step();

        Assert.Multiple(() =>
        {
            Assert.That(firstGpu.TileTemp, Is.LessThan(Balance.WarmTemp));
            Assert.That(secondGpu.TileTemp, Is.LessThan(Balance.WarmTemp));
            Assert.That(firstGpu.RevenueRate, Is.Zero);
            Assert.That(secondGpu.RevenueRate, Is.Zero);
            Assert.That(world.FeedLoadKw, Is.EqualTo(
                Balance.Spec(BuildingKind.Uplink).DrawKw + Balance.Spec(BuildingKind.Pdu).DrawKw));
        });

        float cashBeforeRestart = world.Cash;
        Assert.That(world.RestartHeatShutdown(firstGpu.Id), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(world.Cash, Is.EqualTo(cashBeforeRestart - Balance.HeatRestartCost));
            Assert.That(firstGpu.State, Is.EqualTo(BuildingState.Booting));
        });
        Step(world, 21);
        Assert.That(firstGpu.Producing, Is.True);
    }

    [Test]
    public void BreakerRecoveryDoesNotBypassThermalRestart()
    {
        var world = new DcWorld();
        world.ReceiveAdvance(20_000f);
        Building shutdownGpu = world.TryPlace(BuildingKind.GpuRack, 5, 8);
        Building overloadA = world.TryPlace(BuildingKind.GpuRack, 7, 8);
        Building overloadB = world.TryPlace(BuildingKind.GpuRack, 6, 7);
        Building overloadC = world.TryPlace(BuildingKind.GpuRack, 6, 9);
        Building starterPdu = world.Buildings[world.BuildingIdAt(6, 8)];
        shutdownGpu.State = BuildingState.HeatShutdown;

        for (int i = 0; i < 100 && starterPdu.State != BuildingState.TrippedDark; i++)
            world.Step();
        Assert.That(starterPdu.State, Is.EqualTo(BuildingState.TrippedDark));

        world.ToggleBuilding(overloadA.Id);
        world.ToggleBuilding(overloadB.Id);
        world.ToggleBuilding(overloadC.Id);
        Step(world, 260);

        Assert.Multiple(() =>
        {
            Assert.That(starterPdu.State, Is.EqualTo(BuildingState.Online));
            Assert.That(shutdownGpu.State, Is.EqualTo(BuildingState.HeatShutdown));
            Assert.That(shutdownGpu.Producing, Is.False);
            Assert.That(shutdownGpu.RevenueRate, Is.Zero);
        });
    }

    private static void Step(DcWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++) world.Step();
    }
}
}
