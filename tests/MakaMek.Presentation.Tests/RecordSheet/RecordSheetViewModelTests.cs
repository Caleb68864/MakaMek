using Sanet.MakaMek.Core.Data.Game;
using Sanet.MakaMek.Core.Models.Units;
using Sanet.MakaMek.Core.Models.Units.Mechs;
using Sanet.MakaMek.Core.Models.Units.Components.Internal;
using Sanet.MakaMek.Presentation.RecordSheet;
using Shouldly;

namespace Sanet.MakaMek.Presentation.Tests.RecordSheet;

public class RecordSheetViewModelTests
{
    [Fact]
    public void SampleDiagramData_WithoutPartStates_ExposesEmptyLocations()
    {
        var data = RecordSheetDiagramData.Create(20, []);

        data.Locations.ShouldBeEmpty();
    }

    [Fact]
    public void SelectUnit_ProjectsCurrentAndMaximumValuesWithoutMutatingUnit()
    {
        var unit = CreateMech(20, 10);
        var centerTorso = (CenterTorso)unit.Parts[PartLocation.CenterTorso];
        var viewModel = new RecordSheetViewModel();

        viewModel.SelectUnit(unit);

        viewModel.DiagramData!.Tonnage.ShouldBe(20);
        viewModel.DiagramData.Armour[new(PartLocation.CenterTorso, ArmourFace.Front)].ShouldBe(10);
        viewModel.DiagramData.Armour[new(PartLocation.CenterTorso, ArmourFace.Rear)].ShouldBe(3);
        viewModel.DiagramData.Locations[PartLocation.CenterTorso].CurrentArmor.ShouldBe(10);
        viewModel.DiagramData.Locations[PartLocation.CenterTorso].MaxArmor.ShouldBe(10);
        viewModel.DiagramData.Locations[PartLocation.CenterTorso].CurrentRearArmor.ShouldBe(3);
        viewModel.DiagramData.Locations[PartLocation.CenterTorso].MaxRearArmor.ShouldBe(3);
        centerTorso.CurrentArmor.ShouldBe(10);
        centerTorso.CurrentRearArmor.ShouldBe(3);
    }

    [Fact]
    public void Refresh_AfterDamageUpdatesSnapshotWithoutReselecting()
    {
        var unit = CreateMech(20, 10);
        var centerTorso = unit.Parts[PartLocation.CenterTorso];
        var viewModel = new RecordSheetViewModel();
        viewModel.SelectUnit(unit);

        centerTorso.ApplyDamage(4, HitDirection.Front);
        viewModel.Refresh();

        viewModel.Unit.ShouldBeSameAs(unit);
        viewModel.DiagramData!.Armour[new(PartLocation.CenterTorso, ArmourFace.Front)].ShouldBe(6);
        viewModel.DiagramData.Locations[PartLocation.CenterTorso].MaxArmor.ShouldBe(10);
    }

    [Fact]
    public void Refresh_RaisesNoChange_WhenNothingAboutTheUnitMoved()
    {
        // Every command refreshes the projection, and each refresh builds new dictionaries. Without
        // structural equality that reads as a change and re-renders the whole sheet every time.
        var unit = CreateMech(20, 10);
        var viewModel = new RecordSheetViewModel();
        viewModel.SelectUnit(unit);
        var changes = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RecordSheetViewModel.DiagramData)) changes++;
        };

        viewModel.Refresh();
        viewModel.Refresh();
        viewModel.Refresh();

        changes.ShouldBe(0, "an unchanged unit must not look like new data");
    }

    [Fact]
    public void Refresh_RaisesChange_WhenTheUnitTakesDamage()
    {
        // Negative control for the test above: real change must still get through.
        var unit = CreateMech(20, 10);
        var viewModel = new RecordSheetViewModel();
        viewModel.SelectUnit(unit);
        var changes = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RecordSheetViewModel.DiagramData)) changes++;
        };

        unit.Parts[PartLocation.CenterTorso].ApplyDamage(4, HitDirection.Front);
        viewModel.Refresh();

        changes.ShouldBe(1);
    }

    [Fact]
    public void DiagramData_ComparesStructurally_AcrossItsProjectedParts()
    {
        var unit = CreateMech(20, 10);
        var first = RecordSheetDiagramData.FromUnit(unit);

        RecordSheetDiagramData.FromUnit(unit).ShouldBe(first);
        RecordSheetDiagramData.FromUnit(unit).GetHashCode().ShouldBe(first.GetHashCode());

        // Each projected facet has to take part in the comparison, not just the armour numbers.
        (first with { Tonnage = first.Tonnage + 5 }).ShouldNotBe(first);
        (first with { FluffArtworkName = "other" }).ShouldNotBe(first);
        (first with { RecentDamageLocations = new HashSet<PartLocation> { PartLocation.Head } })
            .ShouldNotBe(first);

        unit.Parts[PartLocation.LeftArm].ApplyDamage(3, HitDirection.Front);
        RecordSheetDiagramData.FromUnit(unit).ShouldNotBe(first);
    }

    [Fact]
    public void DiagramData_Equality_HandlesNullSelfAndMismatchedCollections()
    {
        var unit = CreateMech(20, 10);
        var first = RecordSheetDiagramData.FromUnit(unit);

        first.Equals(null).ShouldBeFalse();
        first.Equals(first).ShouldBeTrue("the same instance short-circuits");

        // A shorter armour dictionary and one of equal length with a different key both differ.
        var fewerRegions = RecordSheetDiagramData.Create(20,
            [new(new ArmourRegion(PartLocation.Head, ArmourFace.Front), 8)]);
        fewerRegions.ShouldNotBe(first);
        first.ShouldNotBe(fewerRegions);

        var sameCountDifferentKey = RecordSheetDiagramData.Create(20,
            [new(new ArmourRegion(PartLocation.LeftLeg, ArmourFace.Rear), 8)]);
        sameCountDifferentKey.ShouldNotBe(fewerRegions);

        // Critical slots: same instance, a different count, and the same count with different slots.
        var slots = new Dictionary<PartLocation, IReadOnlyList<RecordSheetCriticalSlotData>>
        {
            [PartLocation.Head] = [new RecordSheetCriticalSlotData(1, "Sensors", CriticalSlotState.Intact)]
        };
        var withSlots = fewerRegions with { CriticalSlotsByLocation = slots };
        (fewerRegions with { CriticalSlotsByLocation = slots }).ShouldBe(withSlots);
        withSlots.ShouldNotBe(fewerRegions);
        (withSlots with
        {
            CriticalSlotsByLocation = new Dictionary<PartLocation, IReadOnlyList<RecordSheetCriticalSlotData>>
            {
                [PartLocation.Head] = [new RecordSheetCriticalSlotData(1, "Sensors", CriticalSlotState.Hit)]
            }
        }).ShouldNotBe(withSlots);
        (withSlots with
        {
            CriticalSlotsByLocation = new Dictionary<PartLocation, IReadOnlyList<RecordSheetCriticalSlotData>>
            {
                [PartLocation.CenterTorso] = [new RecordSheetCriticalSlotData(1, "Sensors", CriticalSlotState.Intact)]
            }
        }).ShouldNotBe(withSlots, "same count, different location");
    }

    [Fact]
    public void SelectUnit_ReportsDestroyedAndBlownOffAsDistinctStates()
    {
        var unit = CreateMech(20, 10);
        var leftArm = unit.Parts[PartLocation.LeftArm];
        var rightArm = unit.Parts[PartLocation.RightArm];
        leftArm.ApplyDamage(4, HitDirection.Front, isExplosion: true);
        rightArm.BlowOff();
        var viewModel = new RecordSheetViewModel();

        viewModel.SelectUnit(unit);

        var destroyed = viewModel.DiagramData!.Locations[PartLocation.LeftArm];
        var blownOff = viewModel.DiagramData.Locations[PartLocation.RightArm];
        destroyed.IsDestroyed.ShouldBeTrue();
        destroyed.IsBlownOff.ShouldBeFalse();
        blownOff.IsDestroyed.ShouldBeTrue();
        blownOff.IsBlownOff.ShouldBeTrue();
        viewModel.DiagramData.Armour[new(PartLocation.RightArm, ArmourFace.Front)].ShouldBe(0);
    }

    [Fact]
    public void QuickUnitSwitch_LeavesOnlyTheLatestSelectionProjected()
    {
        var first = CreateMech(20, 10);
        var second = CreateMech(100, 47);
        var viewModel = new RecordSheetViewModel();

        viewModel.SelectUnit(first);
        viewModel.SelectUnit(second);

        viewModel.Unit.ShouldBeSameAs(second);
        viewModel.DiagramData!.Tonnage.ShouldBe(100);
        viewModel.DiagramData.Armour[new(PartLocation.CenterTorso, ArmourFace.Front)].ShouldBe(47);
    }

    [Fact]
    public void RecentDamage_IsAccumulatedAndClearedWithoutChangingUnitState()
    {
        var unit = CreateMech(20, 10);
        var viewModel = new RecordSheetViewModel();
        viewModel.SelectUnit(unit);

        viewModel.AddRecentDamage([PartLocation.CenterTorso]);
        viewModel.AddRecentDamage([PartLocation.LeftArm, PartLocation.CenterTorso]);

        viewModel.DiagramData!.RecentlyDamagedLocations.ShouldBe(
            new HashSet<PartLocation> { PartLocation.CenterTorso, PartLocation.LeftArm });
        unit.Parts[PartLocation.CenterTorso].CurrentArmor.ShouldBe(10);

        viewModel.ClearRecentDamage();

        viewModel.DiagramData!.RecentlyDamagedLocations.ShouldBeEmpty();
    }

    [Fact]
    public void SelectUnit_ClearsRecentDamageOnlyWhenTheUnitChanges()
    {
        var first = CreateMech(20, 10);
        var second = CreateMech(100, 47);
        var viewModel = new RecordSheetViewModel();
        viewModel.SelectUnit(first);
        viewModel.AddRecentDamage([PartLocation.CenterTorso]);

        viewModel.SelectUnit(first);
        viewModel.DiagramData!.RecentlyDamagedLocations.ShouldContain(PartLocation.CenterTorso);

        viewModel.SelectUnit(second);
        viewModel.DiagramData!.RecentlyDamagedLocations.ShouldBeEmpty();
    }

    [Fact]
    public void SelectUnit_ProjectsEmptyIntactHitAndDestroyedCriticalSlots()
    {
        var unit = CreateMech(20, 10);
        var centerTorso = unit.Parts[PartLocation.CenterTorso];
        var gyro = centerTorso.Components.OfType<Gyro>().Single();
        var viewModel = new RecordSheetViewModel();

        viewModel.SelectUnit(unit);

        var slots = viewModel.DiagramData!.CriticalSlots[PartLocation.CenterTorso];
        slots.Count.ShouldBe(centerTorso.TotalSlots);
        slots[0].State.ShouldBe(CriticalSlotState.Empty);
        slots[3].ComponentName.ShouldBe("Gyro");
        slots[3].State.ShouldBe(CriticalSlotState.Intact);

        centerTorso.CriticalHit(3);
        viewModel.Refresh();
        viewModel.DiagramData!.CriticalSlots[PartLocation.CenterTorso][3].State.ShouldBe(CriticalSlotState.Hit);

        centerTorso.CriticalHit(4);
        viewModel.Refresh();
        viewModel.DiagramData!.CriticalSlots[PartLocation.CenterTorso][3].State.ShouldBe(CriticalSlotState.Destroyed);
        viewModel.DiagramData.CriticalSlots[PartLocation.CenterTorso][4].State.ShouldBe(CriticalSlotState.Destroyed);
        viewModel.DiagramData.CriticalSlots.ContainsKey(PartLocation.Head).ShouldBeTrue();
    }

    [Fact]
    public void SelectUnit_LeavesMissingLocationsAbsentFromTheSlotProjection()
    {
        var unit = new Mech("Test", "Partial", 20,
            [new CenterTorso("Center Torso", 10, 3, 6)]);
        var viewModel = new RecordSheetViewModel();

        viewModel.SelectUnit(unit);

        viewModel.DiagramData!.CriticalSlots.ContainsKey(PartLocation.CenterTorso).ShouldBeTrue();
        viewModel.DiagramData.CriticalSlots.ContainsKey(PartLocation.Head).ShouldBeFalse();
    }

    private static Mech CreateMech(int tonnage, int centerTorsoArmor) => new(
        "Test",
        "Record Sheet",
        tonnage,
        [
            new Head("Head", 8, 3),
            new CenterTorso("Center Torso", centerTorsoArmor, 3, 6),
            new SideTorso("Left Torso", PartLocation.LeftTorso, 8, 2, 5),
            new SideTorso("Right Torso", PartLocation.RightTorso, 8, 2, 5),
            new Arm("Left Arm", PartLocation.LeftArm, 4, 4),
            new Arm("Right Arm", PartLocation.RightArm, 4, 4),
            new Leg("Left Leg", PartLocation.LeftLeg, 8, 4),
            new Leg("Right Leg", PartLocation.RightLeg, 8, 4)
        ]);
}
