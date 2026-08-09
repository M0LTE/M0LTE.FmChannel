namespace M0LTE.Fm;

/// <summary>Channel bandwidth class, as Tait define it.</summary>
/// <remarks>
/// MMA-00072-03 p.6: "Narrow bandwidth | NB | 12.5kHz | +/-2.5kHz", "Mid bandwidth | MB | 20kHz |
/// +/-4kHz", "Wide bandwidth | WB | 25kHz | +/-5.0kHz". The deviation column is 100% modulation.
/// </remarks>
public enum TaitBandwidth
{
    /// <summary>12.5 kHz channel, 100% deviation +/-2.5 kHz.</summary>
    Narrow,

    /// <summary>20 kHz channel, 100% deviation +/-4 kHz.</summary>
    Mid,

    /// <summary>25 kHz channel, 100% deviation +/-5 kHz.</summary>
    Wide,
}

/// <summary>
/// A Tait TM8100 as a link, for the one tap configuration this documentation can fully support.
/// </summary>
/// <remarks>
/// <para><b>Only the R1/T13 configuration is offered, and that is on purpose.</b> Those two taps sit
/// at the ends of the radio's audio chains: R1 takes raw demodulated audio before the
/// bandwidth-dependent scaling, the decimation to 8 kHz, the 0.3 to 3 kHz bandpass and de-emphasis
/// (MMA-00005-05 p.56); T13 injects after compression, encryption, the 300 Hz high pass,
/// pre-emphasis, the limiter, the 3 kHz low pass and the peak-system-deviation scaler. Every stage
/// whose behaviour Tait do NOT publish is bypassed at those taps, so this profile can be built
/// entirely from figures that carry a page number.</para>
/// <para>A microphone-path model would need the emphasis time constant, which appears nowhere in
/// the 1083 pages (only its slope, 6 dB per octave over 300 Hz to 3 kHz, +1/-3 dB), and the
/// limiter's knee, attack and release, which appear nowhere either. Rather than invent them and
/// present the result as a model of a real radio, that configuration is not offered. Build it
/// yourself from <see cref="FmLinkProfile.MicAndSpeaker"/> if you want it, and know which of its
/// numbers you chose.</para>
/// <para>What is NOT modelled here, and would matter to somebody: the receive AGC, which begins
/// acting above about -70 dBm of combined wanted and first-adjacent power (p.54); the front-end
/// attenuator, above -30 dBm (calibration manual p.17); and the third-order Butterworth at about
/// 12 kHz that the tap output passes through on its way to the connector (p.84), which is well
/// above any voice-band mode and only matters to a wideband one.</para>
/// </remarks>
public static class TaitTm8100
{
    /// <summary>100% deviation for a bandwidth class. MMA-00072-03 p.6.</summary>
    public static double FullDeviationHz(TaitBandwidth bandwidth) => bandwidth switch
    {
        TaitBandwidth.Narrow => 2500,
        TaitBandwidth.Mid => 4000,
        _ => 5000,
    };

    /// <summary>Channel spacing for a bandwidth class. MMA-00072-03 p.6.</summary>
    public static double ChannelSpacingHz(TaitBandwidth bandwidth) => bandwidth switch
    {
        TaitBandwidth.Narrow => 12500,
        TaitBandwidth.Mid => 20000,
        _ => 25000,
    };

    /// <summary>
    /// Total IF bandwidth at the <b>-3 dB</b> points, as Tait state it. MMA-00005-05 p.73,
    /// Table 3.1, "All bands except K5" column; the K5 band is 12.0, 9.0 and 7.6 kHz.
    /// </summary>
    /// <remarks>
    /// <b>This is not the number to hand to <see cref="FmLinkProfile.IfBandwidthHz"/>,</b> which is
    /// a -6 dB width. Use <see cref="Link"/>, which does the conversion, rather than passing this
    /// figure across. Mixing the two models a filter narrower than the radio, and has already once
    /// taken a 25 kHz mode from every frame to none and been reported as a property of the radio.
    /// </remarks>
    public static double IfBandwidthAtThreeDbHz(TaitBandwidth bandwidth) => bandwidth switch
    {
        TaitBandwidth.Narrow => 7800,
        TaitBandwidth.Mid => 12000,
        _ => 12600,
    };

    /// <summary>
    /// Receiver sensitivity for 12 dB SINAD, measured performance, B1 band (136 to 174 MHz).
    /// MMA-00072-03 p.14. The compliance limit is looser, at better than -117 dBm.
    /// </summary>
    public const double TwelveDbSinadSensitivityDbm = -121.0;

    /// <summary>
    /// The lowest RSSI reading the specification covers. MMA-00072-03 p.14 gives the range as
    /// -115 to -50 dBm; the firmware carries values to -119 dBm, which several thresholds default
    /// to, but nothing in the documentation supports their accuracy.
    /// </summary>
    public const double LowestSpecifiedRssiDbm = -115.0;

    /// <summary>
    /// The link a modem sees when it taps R1 on receive and T13 on transmit.
    /// </summary>
    /// <param name="bandwidth">The channel the radio is programmed for.</param>
    /// <param name="deviationHz">Peak deviation the drive is calibrated to. Defaults to 60% of the
    /// class ceiling, which is what Tait's own internal 1200 baud modem uses (calibration manual
    /// p.15) and a sensible starting point for a data mode. Must not exceed
    /// <see cref="FullDeviationHz"/>.</param>
    /// <remarks>
    /// <para>Flat, un-emphasised and unlimited in both directions, because that is what those taps
    /// give. The audio band is bounded only by the IF filter, so it is set from that rather than
    /// from a voice passband.</para>
    /// <para>No limiter is configured, and that is the correct model here rather than an omission:
    /// T13 is past the radio's limiter, so nothing protects the modulator and the operator sets the
    /// drive once against the waveform's own peak. That is what the default drive mode does.</para>
    /// </remarks>
    public static FmLinkProfile Link(TaitBandwidth bandwidth, double? deviationHz = null)
    {
        double full = FullDeviationHz(bandwidth);
        double deviation = deviationHz ?? (full * 0.6);
        if (deviation <= 0 || deviation > full)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviationHz),
                deviation,
                $"peak deviation must be above zero and no more than {full} Hz, which is 100% "
                + $"modulation for a {ChannelSpacingHz(bandwidth) / 1000:0.#} kHz channel");
        }

        // Tait quote -3 dB; this model's parameter is a -6 dB width. A real crystal-plus-FPGA
        // cascade has gentler skirts than the windowed sinc used here, so its -6 dB width is well
        // above its -3 dB one. 1.25 is a shape factor for a filter of that kind and is the one
        // figure in this file that is NOT from the documentation - it is an assumption, named here
        // so nobody mistakes it for a Tait number.
        const double ThreeToSixDbShapeFactor = 1.25;
        double ifBandwidth = IfBandwidthAtThreeDbHz(bandwidth) * ThreeToSixDbShapeFactor;

        // A discriminator cannot hand back more audio bandwidth than half its IF.
        double audioHigh = ifBandwidth / 2;

        return new FmLinkProfile(
            PeakDeviationHz: deviation,
            IfBandwidthHz: ifBandwidth,
            TxAudioLowHz: 20,
            TxAudioHighHz: audioHigh,
            RxAudioLowHz: 20,
            RxAudioHighHz: audioHigh,
            PreEmphasisMicroseconds: 0,
            DeEmphasisMicroseconds: 0);
    }

    /// <summary>
    /// The receiver's noise figure, worked back from its published 12 dB SINAD sensitivity.
    /// </summary>
    /// <param name="bandwidth">The channel class, which sets the bandwidth the figure is in.</param>
    /// <param name="sinadCarrierToNoiseDb">What carrier-to-noise ratio 12 dB SINAD corresponds to on
    /// this receiver. <b>No radio in the set states this and there is no universal value</b>; 5 dB
    /// is a common figure for narrowband FM and is the default here, but it is an assumption and
    /// the answer carries its uncertainty.</param>
    /// <remarks>
    /// Tait publish sensitivity, not noise figure, so this is the only route from the datasheet to
    /// the number a link budget needs. At narrow bandwidth with the default assumption it gives
    /// about 9 dB, which is unremarkable for a commercial mobile.
    /// </remarks>
    public static double NoiseFigureDb(
        TaitBandwidth bandwidth, double sinadCarrierToNoiseDb = 5.0) =>
        LinkBudget.NoiseFigureFromSinadSensitivity(
            TwelveDbSinadSensitivityDbm,
            IfBandwidthAtThreeDbHz(bandwidth),
            sinadCarrierToNoiseDb);
}
