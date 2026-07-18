using System.Collections.Generic;
using NUnit.Framework;
using Throughput.Sim;

namespace Throughput.SimTests
{
public sealed class PlacementInvariantTests
{
    [Test]
    public void RapidPlacementsCannotExceedGridFeedCapacity()
    {
        var world = new DcWorld();
        world.Step();
        world.ReceiveAdvance(1_000_000f);
        List<(int X, int Y)> freeTiles = FreeTiles(world);

        const int pduPlacementsThatFit = 98;
        Building lastPdu = null;
        for (int i = 0; i < pduPlacementsThatFit; i++)
        {
            Building pdu = world.TryPlace(BuildingKind.Pdu, freeTiles[i].X, freeTiles[i].Y);
            Assert.That(pdu, Is.Not.Null, $"PDU #{i + 1} should fit within the feed budget");
            lastPdu = pdu;
        }

        world.ToggleBuilding(lastPdu.Id);
        (int x, int y) = freeTiles[pduPlacementsThatFit];
        PlacementCheck check = world.CheckPlace(BuildingKind.Pdu, x, y);

        Assert.Multiple(() =>
        {
            Assert.That(check.Verdict, Is.EqualTo(Verdict.Red));
            Assert.That(check.Reason, Does.Contain("Grid feed maxed"));
            Assert.That(world.TryPlace(BuildingKind.Pdu, x, y), Is.Null);
        });

        Assert.That(world.TrySell(lastPdu.Id), Is.True);
        Assert.That(world.CheckPlace(BuildingKind.Pdu, x, y).Verdict, Is.EqualTo(Verdict.Green));
    }

    [Test]
    public void RapidPlacementsForecastPduOverload()
    {
        var world = new DcWorld();
        world.ReceiveAdvance(10_000f);
        Building firstGpu = world.TryPlace(BuildingKind.GpuRack, 5, 8);
        Assert.That(firstGpu, Is.Not.Null);
        Assert.That(world.TryPlace(BuildingKind.GpuRack, 7, 8), Is.Not.Null);
        world.ToggleBuilding(firstGpu.Id);

        PlacementCheck check = world.CheckPlace(BuildingKind.Crac, 6, 7);

        Assert.Multiple(() =>
        {
            Assert.That(check.Verdict, Is.EqualTo(Verdict.Amber));
            Assert.That(check.Reason, Does.Contain("105%"));
        });

        Assert.That(world.TrySell(firstGpu.Id), Is.True);
        Assert.That(world.CheckPlace(BuildingKind.Crac, 6, 7).Verdict, Is.EqualTo(Verdict.Green));
    }

    [Test]
    public void RapidPlacementsForecastBandwidthOverload()
    {
        var world = new DcWorld();
        world.ReceiveAdvance(10_000f);
        Assert.That(world.TryPlace(BuildingKind.Pdu, 12, 8), Is.Not.Null);
        Assert.That(world.TryPlace(BuildingKind.CpuRack, 5, 8), Is.Not.Null);
        Assert.That(world.TryPlace(BuildingKind.CpuRack, 6, 7), Is.Not.Null);
        Assert.That(world.TryPlace(BuildingKind.CpuRack, 7, 8), Is.Not.Null);
        Building firstGpu = world.TryPlace(BuildingKind.GpuRack, 12, 5);
        Assert.That(firstGpu, Is.Not.Null);
        world.ToggleBuilding(firstGpu.Id);

        PlacementCheck check = world.CheckPlace(BuildingKind.GpuRack, 12, 11);

        Assert.Multiple(() =>
        {
            Assert.That(check.Verdict, Is.EqualTo(Verdict.Amber));
            Assert.That(check.Reason, Does.Contain("Uplink saturated"));
        });

        Assert.That(world.TrySell(firstGpu.Id), Is.True);
        Assert.That(world.CheckPlace(BuildingKind.GpuRack, 12, 11).Verdict, Is.EqualTo(Verdict.Green));
    }

    [Test]
    public void PlacementWarnsWhenPduWouldCrossNinetyPercent()
    {
        var world = new DcWorld();
        Assert.That(world.TryPlace(BuildingKind.CpuRack, 5, 8), Is.Not.Null);
        Assert.That(world.TryPlace(BuildingKind.CpuRack, 6, 7), Is.Not.Null);
        Assert.That(world.TryPlace(BuildingKind.CpuRack, 7, 8), Is.Not.Null);
        Assert.That(world.TryPlace(BuildingKind.Crac, 6, 9), Is.Not.Null);

        PlacementCheck check = world.CheckPlace(BuildingKind.GpuRack, 5, 9);

        Assert.Multiple(() =>
        {
            Assert.That(check.Verdict, Is.EqualTo(Verdict.Amber));
            Assert.That(check.Reason, Does.Contain("95%"));
            Assert.That(check.Reason, Does.Contain("near breaker limit"));
        });
    }

    [Test]
    public void PduPlacementWarnsWhenItWouldAdoptOverloadedOrphans()
    {
        var world = new DcWorld();
        world.ReceiveAdvance(20_000f);
        Building oldPdu = world.TryPlace(BuildingKind.Pdu, 12, 8);
        Assert.That(oldPdu, Is.Not.Null);
        Assert.That(world.TryPlace(BuildingKind.GpuRack, 10, 8), Is.Not.Null);
        Assert.That(world.TryPlace(BuildingKind.GpuRack, 12, 5), Is.Not.Null);
        Assert.That(world.TryPlace(BuildingKind.GpuRack, 12, 11), Is.Not.Null);
        Assert.That(world.TrySell(oldPdu.Id), Is.True);

        PlacementCheck check = world.CheckPlace(BuildingKind.Pdu, 12, 8);

        Assert.Multiple(() =>
        {
            Assert.That(check.Verdict, Is.EqualTo(Verdict.Amber));
            Assert.That(check.Reason, Does.Contain("adopts 120%"));
            Assert.That(check.Reason, Does.Contain("breaker will trip"));
        });
    }

    private static List<(int X, int Y)> FreeTiles(DcWorld world)
    {
        var tiles = new List<(int X, int Y)>();
        for (int y = 0; y < DcWorld.GridH; y++)
            for (int x = 0; x < DcWorld.GridW; x++)
                if (world.BuildingIdAt(x, y) < 0)
                    tiles.Add((x, y));
        return tiles;
    }
}
}
