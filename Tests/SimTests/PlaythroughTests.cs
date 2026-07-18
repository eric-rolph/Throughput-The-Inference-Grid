using System;
using NUnit.Framework;
using Throughput.Sim;

namespace Throughput.SimTests
{
public sealed class PlaythroughTests
{
    [Test]
    public void LogicalExpansionFulfillsNimbusAndReachesMasteryBeforeDeadline()
    {
        var world = new DcWorld();
        PlaceThenTick(world, BuildingKind.CpuRack, 4, 8);
        PlaceThenTick(world, BuildingKind.CpuRack, 4, 10);
        PlaceThenTick(world, BuildingKind.CpuRack, 6, 10);
        AdvanceUntil(world, w => w.CracUnlocked, 10);

        Building firstCrac = Place(world, BuildingKind.Crac, 3, 8);
        AdvanceUntil(world, _ => firstCrac.State == BuildingState.Online, 100);
        AdvanceUntil(world, w => w.GpuUnlocked, 6_000);
        AdvanceUntil(world, w => w.Contracts.Offers[0].State == OfferState.Offered, 10);
        world.Contracts.Accept(0, world);

        Building firstGpu = Place(world, BuildingKind.GpuRack, 4, 7);
        AdvanceUntil(world, w => firstGpu.Producing && firstGpu.TileTemp < Balance.WarmTemp &&
                                 w.Contracts.Offers[0].State == OfferState.Fulfilled, 100);

        const float secondExpansionCost = 11_500f;
        AdvanceUntil(world, w => w.Cash >= secondExpansionCost, 5_000);
        Building secondPdu = Place(world, BuildingKind.Pdu, 10, 5);
        AdvanceUntil(world, _ => secondPdu.State == BuildingState.Online, 100);
        Building secondCrac = Place(world, BuildingKind.Crac, 12, 4);
        AdvanceUntil(world, _ => secondCrac.State == BuildingState.Online, 100);
        Building secondGpu = Place(world, BuildingKind.GpuRack, 11, 4);
        AdvanceUntil(world, _ => secondGpu.State == BuildingState.Online && secondGpu.NoUplinkFlag, 100);
        AdvanceUntil(world, w => w.Cash >= Balance.UplinkUpgradeCost, 5_000);
        Assert.That(world.BuyUplink(), Is.True);
        world.Step();
        Assert.That(secondGpu.NoUplinkFlag, Is.False);

        AdvanceUntil(world, w => w.Contracts.Offers[1].State == OfferState.Offered, 15_000);
        world.Contracts.Accept(1, world);

        AdvanceUntil(world, w => w.Cash >= 7_000f, 5_000);
        Building thirdPdu = Place(world, BuildingKind.Pdu, 14, 5);
        AdvanceUntil(world, _ => thirdPdu.State == BuildingState.Online, 100);
        Building thirdGpu = Place(world, BuildingKind.GpuRack, 13, 4);
        AdvanceUntil(world, _ => thirdGpu.Producing, 100);

        AdvanceUntil(world, w => w.Cash >= 8_500f, 15_000);
        Building fourthPdu = Place(world, BuildingKind.Pdu, 19, 10);
        AdvanceUntil(world, _ => fourthPdu.State == BuildingState.Online, 100);
        Building thirdCrac = Place(world, BuildingKind.Crac, 18, 11);
        AdvanceUntil(world, _ => thirdCrac.State == BuildingState.Online, 100);
        Building fourthGpu = Place(world, BuildingKind.GpuRack, 18, 10);
        AdvanceUntil(world, w => fourthGpu.Producing && w.Contracts.NimbusFulfilled, 100);
        AdvanceUntil(world, w => w.Goals.CurrentIndex == w.Goals.Chips.Length, 20_000);

        float hottestRack = float.MinValue;
        int operationalGpus = 0;
        foreach (Building building in world.Buildings)
        {
            if (building.Spec.IsRack) hottestRack = Math.Max(hottestRack, building.TileTemp);
            if (building.Kind == BuildingKind.GpuRack && building.Producing) operationalGpus++;
        }

        TestContext.Out.WriteLine(
            $"Mastery t={world.Elapsed:0.0}s Day={world.Day} cash={world.Cash:0} earned={world.Earned:0} " +
            $"feed={world.FeedLoadKw:0}/{world.FeedCapKw:0} bw={world.BandwidthUsed:0}/{world.BandwidthCap:0} " +
            $"hottest={hottestRack:0.0}C");

        Assert.Multiple(() =>
        {
            Assert.That(world.Day, Is.LessThanOrEqualTo(7));
            Assert.That(world.Contracts.Offers[0].State, Is.EqualTo(OfferState.Fulfilled));
            Assert.That(world.Contracts.Offers[1].State, Is.EqualTo(OfferState.Fulfilled));
            Assert.That(world.Goals.IsComplete, Is.True);
            Assert.That(world.Earned, Is.GreaterThanOrEqualTo(Balance.LifetimeGate));
            Assert.That(operationalGpus, Is.EqualTo(4));
            Assert.That(world.BandwidthUsed, Is.EqualTo(19f));
            Assert.That(world.BandwidthCap, Is.EqualTo(20f));
            Assert.That(world.FeedLoadKw, Is.EqualTo(290f));
            Assert.That(hottestRack, Is.LessThan(Balance.WarmTemp));
            Assert.That(world.TripEvents, Is.Empty);
        });
    }

    private static Building PlaceThenTick(DcWorld world, BuildingKind kind, int x, int y)
    {
        Building building = Place(world, kind, x, y);
        world.Step();
        return building;
    }

    private static Building Place(DcWorld world, BuildingKind kind, int x, int y)
    {
        Building building = world.TryPlace(kind, x, y);
        Assert.That(building, Is.Not.Null, $"Expected {kind} placement at ({x}, {y}) to succeed");
        return building;
    }

    private static void AdvanceUntil(DcWorld world, Func<DcWorld, bool> condition, int maxTicks)
    {
        for (int i = 0; i < maxTicks && !condition(world); i++) world.Step();
        Assert.That(condition(world), Is.True,
            $"Condition was not reached after {maxTicks} ticks at t={world.Elapsed:0.0}s, Day {world.Day}");
    }
}
}
