namespace M0LTE.Fm.Tests;

using AwesomeAssertions;
using M0LTE.Fm;
using Xunit;

/// <summary>
/// The arithmetic that turns a station description into the number FmChannel actually takes.
/// </summary>
public class LinkBudgetTests
{
    [Fact]
    public void A_Worked_Link_Comes_Out_Where_The_Arithmetic_Says()
    {
        // 25 W into 2 dB of feeder and a 6 dBi colinear, 97.3 dB of path, into a 2 dBi antenna
        // behind 1 dB of feeder. EIRP is 44.0 - 2 + 6 = 48.0 dBm, so received is
        // 48.0 - 97.3 + 2 - 1 = -48.3 dBm.
        var node = new Station(Station.Watts(25), FeederLossDb: 2, AntennaGainDbi: 6, ReceiverNoiseFigureDb: 9);
        var user = new Station(Station.Watts(5), FeederLossDb: 1, AntennaGainDbi: 2, ReceiverNoiseFigureDb: 9);

        Station.Watts(25).Should().BeApproximately(43.98, 0.01);
        node.EirpDbm.Should().BeApproximately(47.98, 0.01);
        LinkBudget.ReceivedDbm(node, user, pathLossDb: 97.3).Should().BeApproximately(-48.32, 0.01);
    }

    [Fact]
    public void Free_Space_Loss_Matches_The_Standard_Formula()
    {
        // 145 MHz over 12 km: 32.44 + 20log10(145) + 20log10(12) = 97.3 dB.
        LinkBudget.FreeSpacePathLossDb(145, 12).Should().BeApproximately(97.28, 0.05);
    }

    [Fact]
    public void Site_Noise_Dominates_On_Two_Metres_And_Not_On_Seventy_Centimetres()
    {
        // The finding this exists to make checkable: where a station is sited is worth about 12 dB
        // on 2 m and almost nothing on 70 cm, because man-made noise falls away with frequency
        // faster than anything else in the budget.
        const double narrowIf = 7800;
        const double nf = 9;

        double vhfQuiet = LinkBudget.NoiseFloorDbm(narrowIf, nf, SiteNoise.QuietRural, 145);
        double vhfBusy = LinkBudget.NoiseFloorDbm(narrowIf, nf, SiteNoise.Business, 145);
        double uhfQuiet = LinkBudget.NoiseFloorDbm(narrowIf, nf, SiteNoise.QuietRural, 433);
        double uhfBusy = LinkBudget.NoiseFloorDbm(narrowIf, nf, SiteNoise.Business, 433);

        (vhfBusy - vhfQuiet).Should().BeGreaterThan(
            8, "siting is worth many decibels on 2 m, which is more than most receiver work");
        (uhfBusy - uhfQuiet).Should().BeLessThan(
            3, "on 70 cm man-made noise has fallen below the receiver's own, so siting stops "
            + "mattering and the noise figure starts to");
    }

    [Fact]
    public void A_Thermal_Only_Floor_Is_The_Textbook_Figure()
    {
        // -174 dBm/Hz + 10log10(7800) + 9 dB = -126.1 dBm.
        LinkBudget.NoiseFloorDbm(7800, 9, SiteNoise.None, 145)
            .Should().BeApproximately(-126.08, 0.1);
    }

    [Fact]
    public void A_Tait_Link_Is_Built_Only_From_Figures_With_A_Page_Number()
    {
        // The R1/T13 configuration bypasses every stage Tait do not publish, which is why it is the
        // only one offered: flat, un-emphasised, unlimited, bounded by the IF and nothing else.
        FmLinkProfile link = TaitTm8100.Link(TaitBandwidth.Narrow);

        link.PreEmphasisMicroseconds.Should().Be(
            0, "T13 is past pre-emphasis, so the unsourced time constant never has to be guessed");
        link.DeEmphasisMicroseconds.Should().Be(0, "R1 is ahead of de-emphasis");
        link.LimitAtDeviationHz.Should().BeNull(
            "T13 is past the limiter, so the drive is set against the waveform's own peak");
        link.PeakDeviationHz.Should().Be(
            1500, "the default is 60% of the class ceiling, which is what Tait's own 1200 baud "
            + "modem uses");
        link.RxAudioHighHz.Should().BeGreaterThan(
            3000, "R1 is ahead of the 3 kHz low pass, so the audio is as wide as the IF allows");
    }

    [Fact]
    public void A_Tait_Refuses_A_Deviation_Above_Its_Own_Class_Ceiling()
    {
        FluentActions.Invoking(() => TaitTm8100.Link(TaitBandwidth.Narrow, 3000))
            .Should().Throw<ArgumentOutOfRangeException>(
                "2.5 kHz is 100% modulation on a 12.5 kHz channel, so 3 kHz is over-deviation and "
                + "silently simulating it would be modelling an illegal station");
    }

    [Fact]
    public void The_Derived_Noise_Figure_Is_Plausible_For_A_Commercial_Mobile()
    {
        // Tait publish sensitivity, not noise figure, so this is the only route from the datasheet
        // to the number a budget needs. It carries the assumption it is given, which is why the
        // bound here is loose rather than exact.
        TaitTm8100.NoiseFigureDb(TaitBandwidth.Narrow)
            .Should().BeInRange(5, 14, "anything outside that would suggest the arithmetic is wrong");
    }
}
