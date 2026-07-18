using System;
using NUnit.Framework;
using Throughput.Sim;

namespace Throughput.SimTests
{
public sealed class ContractProgressionTests
{
    [Test]
    public void DayFourAndNimbusBeginAfterThreeCompleteGameDays()
    {
        var world = new DcWorld();
        int ticksPerDay = (int)(Balance.DaySeconds * Balance.TickRate);

        Step(world, ticksPerDay - 1);
        Assert.That(world.Day, Is.EqualTo(1));
        world.Step();

        Assert.Multiple(() =>
        {
            Assert.That(world.Day, Is.EqualTo(2));
            Assert.That(world.ClockHours, Is.EqualTo(Balance.StartHour).Within(0.001f));
        });

        Step(world, ticksPerDay * 2 - 1);
        Assert.Multiple(() =>
        {
            Assert.That(world.Day, Is.EqualTo(3));
            Assert.That(world.Contracts.Offers[1].State, Is.EqualTo(OfferState.Pending));
        });
        world.Step();

        Assert.Multiple(() =>
        {
            Assert.That(world.Elapsed, Is.EqualTo(Balance.DaySeconds * 3f).Within(Balance.TickDt));
            Assert.That(world.Day, Is.EqualTo(4));
            Assert.That(world.ClockHours, Is.EqualTo(Balance.StartHour).Within(0.001f));
            Assert.That(world.Contracts.Offers[1].State, Is.EqualTo(OfferState.Offered));
        });
    }

    [Test]
    public void AcceptingContractAddsCashButNotLifetimeEarnings()
    {
        var world = new DcWorld { GpuUnlocked = true };
        world.Step();
        Offer picoChat = world.Contracts.Offers[0];
        Assert.That(picoChat.State, Is.EqualTo(OfferState.Offered));
        float cashBefore = world.Cash;
        float earnedBefore = world.Earned;

        world.Contracts.Accept(0, world);

        Assert.Multiple(() =>
        {
            Assert.That(picoChat.State, Is.EqualTo(OfferState.Active));
            Assert.That(world.AnyContractSigned, Is.True);
            Assert.That(world.Cash, Is.EqualTo(cashBefore + picoChat.Advance));
            Assert.That(world.Earned, Is.EqualTo(earnedBefore));
        });
    }

    [Test]
    public void DisconnectedGpuDoesNotFulfillContract()
    {
        var world = new DcWorld { GpuUnlocked = true };
        world.Step();
        world.Contracts.Accept(0, world);
        Offer picoChat = world.Contracts.Offers[0];
        Building gpu = world.TryPlace(BuildingKind.GpuRack, 7, 8);
        int starterPduId = world.BuildingIdAt(6, 8);

        world.ToggleBuilding(starterPduId);
        Step(world, 80);

        Assert.Multiple(() =>
        {
            Assert.That(gpu.State, Is.EqualTo(BuildingState.Booting));
            Assert.That(gpu.BootRemaining, Is.EqualTo(gpu.Spec.BootSeconds));
            Assert.That(gpu.PduId, Is.EqualTo(starterPduId));
            Assert.That(gpu.HasPower, Is.False);
            Assert.That(gpu.ServedPf, Is.Zero);
            Assert.That(picoChat.State, Is.EqualTo(OfferState.Active));
        });
    }

    [Test]
    public void NoUplinkGpuDoesNotFulfillContractUntilCapacityIsBought()
    {
        var world = new DcWorld { GpuUnlocked = true };
        world.Step();
        world.Contracts.Accept(0, world);
        Offer picoChat = world.Contracts.Offers[0];
        world.ReceiveAdvance(100_000f);

        Place(world, BuildingKind.Crac, 6, 10);
        Place(world, BuildingKind.CpuRack, 4, 8);
        Place(world, BuildingKind.CpuRack, 5, 8);
        Place(world, BuildingKind.CpuRack, 7, 8);

        Place(world, BuildingKind.Pdu, 12, 5);
        Place(world, BuildingKind.Crac, 12, 7);
        Place(world, BuildingKind.CpuRack, 10, 5);
        Place(world, BuildingKind.CpuRack, 14, 5);

        Place(world, BuildingKind.Pdu, 18, 10);
        Place(world, BuildingKind.Crac, 18, 12);
        Place(world, BuildingKind.CpuRack, 16, 10);
        Place(world, BuildingKind.CpuRack, 20, 10);
        Building gpu = Place(world, BuildingKind.GpuRack, 18, 8);
        Step(world, 100);

        Assert.Multiple(() =>
        {
            Assert.That(world.BandwidthUsed, Is.EqualTo(11f));
            Assert.That(gpu.NoUplinkFlag, Is.True);
            Assert.That(gpu.Producing, Is.False);
            Assert.That(picoChat.State, Is.EqualTo(OfferState.Active));
        });

        Assert.That(world.BuyUplink(), Is.True);
        world.Step();

        Assert.Multiple(() =>
        {
            Assert.That(gpu.Producing, Is.True);
            Assert.That(picoChat.State, Is.EqualTo(OfferState.Fulfilled));
        });
    }

    [Test]
    public void LifetimeEarningsDoNotGrantMasteryBeforeNimbusIsFulfilled()
    {
        DcWorld world = BuildThroughFirstGpu();
        Building secondGpu = null;

        while (world.Day <= 7 && world.Earned < Balance.LifetimeGate)
        {
            Offer nimbus = world.Contracts.Offers[1];
            if (nimbus.State == OfferState.Offered)
                world.Contracts.Accept(1, world);

            if (secondGpu == null && world.Cash >= 7_000f)
            {
                Place(world, BuildingKind.Pdu, 12, 8);
                secondGpu = Place(world, BuildingKind.GpuRack, 12, 5);
            }

            if (secondGpu != null && secondGpu.NoUplinkFlag &&
                world.BandwidthCap == Balance.UplinkGbps && world.Cash >= Balance.UplinkUpgradeCost)
                Assert.That(world.BuyUplink(), Is.True);

            world.Step();
        }

        Offer finalContract = world.Contracts.Offers[1];
        Assert.Multiple(() =>
        {
            Assert.That(world.Earned, Is.GreaterThanOrEqualTo(Balance.LifetimeGate),
                "The honest two-GPU grid should reach the lifetime gate before Nimbus expires");
            Assert.That(finalContract.State, Is.EqualTo(OfferState.Active));
            Assert.That(world.Goals.CurrentIndex, Is.EqualTo(7),
                "Mastery must remain pending until the final contract is fulfilled");
        });
    }

    [Test]
    public void ExpiredNimbusFailsBeforeLateCapacityCanFulfill()
    {
        DcWorld world = BuildFourGpuGrid();
        Offer nimbus = world.Contracts.Offers[1];
        nimbus.State = OfferState.Active;
        nimbus.AdvancePaid = true;
        nimbus.DeadlineDay = world.Day - 1;
        float cashBeforePenalty = world.Cash;

        world.Contracts.Step(world);

        Assert.Multiple(() =>
        {
            Assert.That(nimbus.State, Is.EqualTo(OfferState.Failed));
            Assert.That(world.Cash, Is.EqualTo(cashBeforePenalty - nimbus.Penalty));
        });

        AdvanceUntil(world, _ => nimbus.State == OfferState.Offered, 4_000);
        float cashBeforeRecovery = world.Cash;
        world.Contracts.Accept(1, world);

        Assert.That(world.Cash, Is.EqualTo(cashBeforeRecovery),
            "A recovery contract must not pay the original advance twice");
        world.Step();
        Assert.That(nimbus.State, Is.EqualTo(OfferState.Fulfilled));
    }

    [Test]
    public void ExpiredOfferCannotBeAcceptedAndRefreshesNextDay()
    {
        var world = new DcWorld();
        Offer nimbus = world.Contracts.Offers[1];
        nimbus.State = OfferState.Offered;
        nimbus.DeadlineDay = world.Day - 1;
        float cashBefore = world.Cash;

        world.Contracts.Accept(1, world);

        Assert.Multiple(() =>
        {
            Assert.That(nimbus.State, Is.EqualTo(OfferState.Offered));
            Assert.That(world.Cash, Is.EqualTo(cashBefore));
        });

        world.Contracts.Step(world);

        Assert.Multiple(() =>
        {
            Assert.That(nimbus.State, Is.EqualTo(OfferState.Passed));
            Assert.That(nimbus.ReofferDay, Is.EqualTo(world.Day + 1));
        });

        AdvanceUntil(world, _ => nimbus.State == OfferState.Offered, 4_000);
        Assert.That(nimbus.DeadlineDay, Is.GreaterThanOrEqualTo(world.Day + 2));
    }

    [Test]
    public void PicoAdvanceFundsTheDocumentedSecondGpuDecisionByFiveThirty()
    {
        DcWorld world = BuildThroughFirstGpu();
        AdvanceUntil(world, w => w.Elapsed >= 330f, 5_000);
        float safeExpansionCost = Balance.UplinkUpgradeCost +
                                  Balance.Spec(BuildingKind.Pdu).Cost +
                                  Balance.Spec(BuildingKind.Crac).Cost +
                                  Balance.Spec(BuildingKind.GpuRack).Cost;

        Assert.That(world.Cash, Is.GreaterThanOrEqualTo(safeExpansionCost),
            "PicoChat should prevent a multi-minute wait before the second GPU decision");
    }

    private static DcWorld BuildThroughFirstGpu()
    {
        var world = new DcWorld();
        Place(world, BuildingKind.CpuRack, 5, 8);
        Place(world, BuildingKind.CpuRack, 6, 7);
        Place(world, BuildingKind.CpuRack, 7, 8);
        AdvanceUntil(world, w => w.CracUnlocked, 10);
        Place(world, BuildingKind.Crac, 6, 9);
        AdvanceUntil(world, w => w.GpuUnlocked, 6_000);
        AdvanceUntil(world, w => w.Contracts.Offers[0].State == OfferState.Offered, 10);
        world.Contracts.Accept(0, world);
        Place(world, BuildingKind.GpuRack, 5, 9);
        AdvanceUntil(world, w => w.Contracts.Offers[0].State == OfferState.Fulfilled, 100);
        return world;
    }

    private static DcWorld BuildFourGpuGrid()
    {
        var world = new DcWorld();
        world.ReceiveAdvance(100_000f);
        Assert.That(world.BuyUplink(), Is.True);

        Place(world, BuildingKind.Crac, 3, 8);
        Place(world, BuildingKind.GpuRack, 4, 7);

        Place(world, BuildingKind.Pdu, 10, 5);
        Place(world, BuildingKind.Crac, 12, 4);
        Place(world, BuildingKind.GpuRack, 11, 4);

        Place(world, BuildingKind.Pdu, 14, 5);
        Place(world, BuildingKind.GpuRack, 13, 4);

        Place(world, BuildingKind.Pdu, 19, 10);
        Place(world, BuildingKind.Crac, 18, 11);
        Place(world, BuildingKind.GpuRack, 18, 10);
        Step(world, 120);
        Assert.That(world.GpuOnlineCount, Is.EqualTo(4));
        return world;
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
        Assert.That(condition(world), Is.True, $"Condition was not reached after {maxTicks} ticks");
    }

    private static void Step(DcWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++) world.Step();
    }
}
}
