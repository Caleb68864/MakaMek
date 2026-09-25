using Sanet.MakaMek.Core.Models.Units;
using Sanet.MakaMek.Presentation.RecordSheet;
using Shouldly;

namespace Sanet.MakaMek.Presentation.Tests.RecordSheet;

public class RecordSheetLayoutTests
{
    private readonly RecordSheetLayout _sut = new();

    [Theory]
    [InlineData(PartLocation.Head, ArmourFace.Front, "armorPipsHD", "Armor_HD_8_Humanoid.svg")]
    [InlineData(PartLocation.CenterTorso, ArmourFace.Front, "armorPipsCT", "Armor_CT_8_Humanoid.svg")]
    [InlineData(PartLocation.LeftArm, ArmourFace.Front, "armorPipsLA", "Armor_LA_8_Humanoid.svg")]
    [InlineData(PartLocation.RightLeg, ArmourFace.Front, "armorPipsRL", "Armor_RL_8_Humanoid.svg")]
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
