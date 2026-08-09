namespace M0LTE.Fm;

/// <summary>
/// How noisy the site is, which on VHF is worth more than most receiver improvements.
/// </summary>
/// <remarks>
/// Categories and coefficients from ITU-R P.372, which gives median man-made noise as
/// <c>Fam = c - d log10(f_MHz)</c>. At 145 MHz the spread between a quiet rural site and a business
/// one is about 25 dB of noise figure; at 433 MHz it has collapsed to about 25 dB below thermal,
/// so external noise stops mattering. That is why the same radio and the same signal can be
/// comfortable on one band and marginal on another.
/// </remarks>
public enum SiteNoise
{
    /// <summary>Business or industrial: c = 76.8, d = 27.7.</summary>
    Business,

    /// <summary>Residential: c = 72.5, d = 27.7.</summary>
    Residential,

    /// <summary>Rural: c = 67.2, d = 27.7.</summary>
    Rural,

    /// <summary>Quiet rural: c = 53.6, d = 28.6.</summary>
    QuietRural,

    /// <summary>External noise equal to the thermal reference, so the floor is the receiver's own
    /// noise figure and nothing else. The physical floor for an antenna looking at a 290 K
    /// environment, and useful for isolating a radio from its surroundings.</summary>
    None,
}

/// <summary>
/// One end of a link: what it transmits with, and what it hears with.
/// </summary>
/// <param name="TransmitPowerDbm">Power at the transmitter's output socket. 25 W is 44.0 dBm.</param>
/// <param name="FeederLossDb">One-way feeder loss, positive. Applies on transmit and on receive.
/// </param>
/// <param name="AntennaGainDbi">Antenna gain in dBi. A gain quoted in dBd is 2.15 dB less.</param>
/// <param name="ReceiverNoiseFigureDb">The receiver's own noise figure, at its antenna socket.
/// <b>Not published for any radio in the Tait set, so there is no default</b> - derive it from a
/// stated sensitivity with <see cref="LinkBudget.NoiseFigureFromSinadSensitivity"/>, or measure it.
/// </param>
public sealed record Station(
    double TransmitPowerDbm,
    double FeederLossDb,
    double AntennaGainDbi,
    double ReceiverNoiseFigureDb)
{
    /// <summary>Effective isotropic radiated power: transmit power, less feeder, plus antenna.
    /// </summary>
    public double EirpDbm => TransmitPowerDbm - FeederLossDb + AntennaGainDbi;

    /// <summary>Watts to dBm, for the usual case of knowing a radio's rating rather than its dBm.
    /// </summary>
    public static double Watts(double watts) => 10 * Math.Log10(watts * 1000);
}

/// <summary>
/// Turns two stations and a path into the carrier-to-noise ratio <see cref="FmChannel"/> takes.
/// </summary>
/// <remarks>
/// <para>The point of this is that <see cref="FmChannel.Apply"/> asks for a carrier-to-noise ratio,
/// which nobody knows about their own link. They know their power, their feeder, their antennas and
/// roughly what the path costs, and they can read an RSSI. This turns either of those into the
/// number the model wants, so a result can be quoted as "decodes at -118 dBm on this link" rather
/// than as an abstraction.</para>
/// <para><b>Path loss is deliberately not modelled.</b> Free space is wrong by tens of decibels on
/// any real VHF path, and a terrain model that looked authoritative would be worse than asking:
/// this library has already shipped one plausible-looking figure that was wrong. Supply a measured
/// path loss, or an RSSI reading, or a free-space figure you have chosen to accept.</para>
/// </remarks>
public static class LinkBudget
{
    /// <summary>Boltzmann's constant times 290 K, per hertz, in dBm. The usual -174.</summary>
    public const double ThermalNoiseDbmPerHz = -174.0;

    /// <summary>Free space path loss, for when you have decided that is what you want.</summary>
    /// <remarks>
    /// Real VHF and UHF paths are worse than this by anything from a few decibels to sixty, so this
    /// is a floor rather than an estimate. It is here because it is occasionally the right question
    /// (a clear line of sight, an aircraft, a satellite) and because omitting it invites someone to
    /// reimplement it worse.
    /// </remarks>
    public static double FreeSpacePathLossDb(double frequencyMHz, double distanceKm) =>
        32.44 + (20 * Math.Log10(frequencyMHz)) + (20 * Math.Log10(distanceKm));

    /// <summary>Median man-made noise figure at a frequency, from ITU-R P.372.</summary>
    public static double ExternalNoiseFigureDb(SiteNoise site, double frequencyMHz)
    {
        (double c, double d) = site switch
        {
            SiteNoise.Business => (76.8, 27.7),
            SiteNoise.Residential => (72.5, 27.7),
            SiteNoise.Rural => (67.2, 27.7),
            SiteNoise.QuietRural => (53.6, 28.6),
            _ => (double.NegativeInfinity, 0.0),
        };

        // Zero dB, not minus infinity. The system noise factor is f_ext + f_rx - 1, which counts
        // the thermal reference once; an external factor of zero would subtract it again and make
        // the floor a fraction of a decibel too low. "No external noise" means external noise equal
        // to the thermal reference, which is also the physical floor for an antenna looking at a
        // 290 K environment.
        return site == SiteNoise.None ? 0 : c - (d * Math.Log10(frequencyMHz));
    }

    /// <summary>
    /// The noise floor at the receiver's antenna socket, in dBm, in the given bandwidth.
    /// </summary>
    /// <remarks>
    /// External and receiver noise add as powers, not as decibels: the system noise factor is
    /// <c>f_ext + f_rx - 1</c>. On 2 m the external term usually dominates and the receiver's own
    /// noise figure barely matters; on 70 cm it is the other way round.
    /// </remarks>
    public static double NoiseFloorDbm(
        double bandwidthHz, double receiverNoiseFigureDb, SiteNoise site, double frequencyMHz)
    {
        double fExt = Math.Pow(10, ExternalNoiseFigureDb(site, frequencyMHz) / 10);
        double fRx = Math.Pow(10, receiverNoiseFigureDb / 10);
        double system = 10 * Math.Log10(Math.Max(fExt + fRx - 1, 1e-12));
        return ThermalNoiseDbmPerHz + (10 * Math.Log10(bandwidthHz)) + system;
    }

    /// <summary>Received power in dBm, from two stations and a path loss.</summary>
    public static double ReceivedDbm(Station transmitter, Station receiver, double pathLossDb) =>
        transmitter.EirpDbm - pathLossDb + receiver.AntennaGainDbi - receiver.FeederLossDb;

    /// <summary>
    /// The carrier-to-noise ratio to hand <see cref="FmChannel.Apply"/>, from a received power.
    /// </summary>
    /// <remarks>
    /// Stated in the receiver's IF bandwidth, which is the convention this whole library uses and
    /// is NOT the SSB world's signal-to-noise in 3 kHz. An RSSI reading is measured in that same
    /// bandwidth, so a reading can be passed straight in here.
    /// </remarks>
    public static double CarrierToNoiseDb(
        double receivedDbm,
        double ifBandwidthHz,
        double receiverNoiseFigureDb,
        SiteNoise site,
        double frequencyMHz) =>
        receivedDbm - NoiseFloorDbm(ifBandwidthHz, receiverNoiseFigureDb, site, frequencyMHz);

    /// <summary>
    /// A receiver's noise figure, worked back from a published SINAD sensitivity.
    /// </summary>
    /// <param name="sensitivityDbm">The level at which the stated SINAD is met, e.g. -121 dBm.
    /// </param>
    /// <param name="ifBandwidthHz">The bandwidth that sensitivity was measured in.</param>
    /// <param name="requiredCarrierToNoiseDb">The carrier-to-noise ratio the stated SINAD
    /// corresponds to. <b>There is no universal figure and this is the weak term:</b> for
    /// narrowband FM at 12 dB SINAD it is commonly taken as around 4 to 6 dB, but it depends on the
    /// receiver's own filtering and de-emphasis. Pass what you believe and treat the answer as
    /// having that uncertainty in it.</param>
    /// <remarks>
    /// Offered because no radio in the Tait documentation set states a noise figure, while several
    /// state sensitivity, so this is the only route from a datasheet to the number a link budget
    /// needs. It is arithmetic, not a measurement, and the caller supplies the assumption that makes
    /// it work rather than having one chosen for them.
    /// </remarks>
    public static double NoiseFigureFromSinadSensitivity(
        double sensitivityDbm, double ifBandwidthHz, double requiredCarrierToNoiseDb) =>
        sensitivityDbm - requiredCarrierToNoiseDb
        - ThermalNoiseDbmPerHz - (10 * Math.Log10(ifBandwidthHz));
}
