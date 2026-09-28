using Sanet.MakaMek.Core.Models.Units;
using Sanet.MakaMek.Presentation.RecordSheet;
using Shouldly;

namespace Sanet.MakaMek.Presentation.Tests.RecordSheet;

public class RecordSheetLayoutTests
{
    private readonly RecordSheetLayout _sut = new();

    [Theory]
    [InlineData(PartLocation.Head, ArmourFace.Front, "armorPipsHD", "Armor_Head_8_Humanoid.svg")]
    [InlineData(PartLocation.CenterTorso, ArmourFace.Front, "armorPipsCT", "Armor_CT_8_Humanoid.svg")]
    [InlineData(PartLocation.LeftArm, ArmourFace.Front, "armorPipsLA", "Armor_LArm_8_Humanoid.svg")]
    [InlineData(PartLocation.RightArm, ArmourFace.Front, "armorPipsRA", "Armor_RArm_8_Humanoid.svg")]
    [InlineData(PartLocation.LeftLeg, ArmourFace.Front, "armorPipsLL", "Armor_LLeg_8_Humanoid.svg")]
    [InlineData(PartLocation.RightLeg, ArmourFace.Front, "armorPipsRL", "Armor_RLeg_8_Humanoid.svg")]
    [InlineData(PartLocation.CenterTorso, ArmourFace.Rear, "armorPipsCTR", "Armor_CT_R_8_Humanoid.svg")]
    [InlineData(PartLocation.LeftTorso, ArmourFace.Rear, "armorPipsLTR", "Armor_LT_R_8_Humanoid.svg")]
    [InlineData(PartLocation.RightTorso, ArmourFace.Rear, "armorPipsRTR", "Armor_RT_R_8_Humanoid.svg")]
    public void ArmourLayoutMapsLocationAndFaceToTemplateAndCluster(
        PartLocation location, ArmourFace face, string region, string cluster)
    {
        _sut.TemplateRegionId(location, face).ShouldBe(region);
        _sut.ArmourClusterName(location, face, 8).ShouldBe(cluster);
    }

    [Theory]
    [InlineData(PartLocation.Head)]
    [InlineData(PartLocation.LeftArm)]
    [InlineData(PartLocation.RightArm)]
    [InlineData(PartLocation.LeftLeg)]
    [InlineData(PartLocation.RightLeg)]
    public void NonTorsoLocationsHaveNoRearRegion(PartLocation location)
    {
        _sut.TemplateRegionId(location, ArmourFace.Rear).ShouldBeNull();
        _sut.ArmourClusterName(location, ArmourFace.Rear, 1).ShouldBeNull();
    }

    [Theory]
    [InlineData(PartLocation.Head, "BipedIS20_HD.svg")]
    [InlineData(PartLocation.CenterTorso, "BipedIS20_CT.svg")]
    [InlineData(PartLocation.LeftArm, "BipedIS20_LA.svg")]
    [InlineData(PartLocation.RightLeg, "BipedIS20_RL.svg")]
    public void StructureClusterUsesTonnageAndPartLocation(PartLocation location, string expected)
    {
        _sut.StructureClusterName(location, 20).ShouldBe(expected);
    }

    [Theory]
    [InlineData(PartLocation.Head, "crits_HD")]
    [InlineData(PartLocation.CenterTorso, "crits_CT")]
    [InlineData(PartLocation.LeftTorso, "crits_LT")]
    [InlineData(PartLocation.RightTorso, "crits_RT")]
    [InlineData(PartLocation.LeftArm, "crits_LA")]
    [InlineData(PartLocation.RightArm, "crits_RA")]
    [InlineData(PartLocation.LeftLeg, "crits_LL")]
    [InlineData(PartLocation.RightLeg, "crits_RL")]
    public void CriticalSlotRegionsMapToTheTemplate(PartLocation location, string expected)
    {
        _sut.CriticalSlotRegionId(location).ShouldBe(expected);
    }

    [Fact]
    public void EveryLocationAndFaceIsMapped_OrDeliberatelyUnmapped()
    {
        // Walks every enum value so each arm of the region and cluster switches is exercised
        // rather than only the handful a composed sheet happens to touch.
        var layout = new RecordSheetLayout();

        foreach (var location in Enum.GetValues<PartLocation>())
        {
            foreach (var face in Enum.GetValues<ArmourFace>())
            {
                var region = layout.TemplateRegionId(location, face);
                var cluster = layout.ArmourClusterName(location, face, 4);

                if (region is null)
                {
                    cluster.ShouldBeNull($"{location}/{face} has no region, so it can have no cluster");
                    continue;
                }

                region.ShouldStartWith("armorPips");
                cluster.ShouldNotBeNull($"{location}/{face} has a region, so it needs a cluster");
            }

            // Structure is front-only, and every location that has one names both parts.
            var structureRegion = layout.StructureRegionId(location);
            if (structureRegion is not null)
            {
                structureRegion.ShouldStartWith("isPips");
                layout.StructureClusterName(location, 20).ShouldNotBeNull();
            }
        }
    }

    [Fact]
    public void AnUnrecognisedLocationMapsToNothing_RatherThanThrowing()
    {
        // The switches cover every PartLocation, so their default arms are only reachable through a
        // value outside the enum - which is exactly what a new location would look like here.
        var layout = new RecordSheetLayout();
        const PartLocation unknown = (PartLocation)999;

        layout.TemplateRegionId(unknown, ArmourFace.Front).ShouldBeNull();
        layout.TemplateRegionId(unknown, ArmourFace.Rear).ShouldBeNull();
        layout.ArmourClusterName(unknown, ArmourFace.Front, 4).ShouldBeNull();
        layout.StructureRegionId(unknown).ShouldBeNull();
        layout.StructureClusterName(unknown, 20).ShouldBeNull();
        layout.CriticalSlotRegionId(unknown).ShouldBeNull();
    }

    [Fact]
    public void ZeroArmourHasNoCluster()
    {
        _sut.ArmourClusterName(PartLocation.CenterTorso, ArmourFace.Front, 0).ShouldBeNull();
    }

    [Fact]
    public void LightAndAssaultSamplesDifferOnlyInTheirResolvedData()
    {
        RecordSheetSamples.LightMech.Tonnage.ShouldBe(20);
        RecordSheetSamples.AssaultMech.Tonnage.ShouldBe(100);
        RecordSheetSamples.LightMech.Armour[new(PartLocation.CenterTorso, ArmourFace.Front)].ShouldBe(10);
        RecordSheetSamples.AssaultMech.Armour[new(PartLocation.CenterTorso, ArmourFace.Front)].ShouldBe(47);
        _sut.ArmourClusterName(PartLocation.CenterTorso, ArmourFace.Front,
            RecordSheetSamples.LightMech.Armour[new(PartLocation.CenterTorso, ArmourFace.Front)])
            .ShouldNotBe(_sut.ArmourClusterName(PartLocation.CenterTorso, ArmourFace.Front,
                RecordSheetSamples.AssaultMech.Armour[new(PartLocation.CenterTorso, ArmourFace.Front)]));
    }
}
