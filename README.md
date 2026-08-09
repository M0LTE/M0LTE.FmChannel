# M0LTE.FmChannel

A physical FM link simulator, for measuring what a soundcard modem really does over an FM voice
path.

Nothing about the FM impairments is approximated by a curve. The modem's audio is genuinely
frequency-modulated onto a carrier, complex noise is added *there* at a stated carrier-to-noise
ratio, and a limiter and a discriminator bring it back through the radio's real audio paths. The
threshold effect, the discriminator's triangular noise, pre/de-emphasis and the band limits all
emerge from that, rather than being asserted.

```csharp
using M0LTE.Fm;

var link = FmLinkProfile.MicAndSpeaker(peakDeviationHz: 3000);
var channel = new FmChannel(link, audioRate: 48000, seed: 1);

float[] heard = channel.Apply(transmitted, cnrDb: 20);
```

The namespace is `M0LTE.Fm`, not `M0LTE.FmChannel`: a type called `FmChannel` inside a namespace of
the same name makes `new FmChannel(...)` ambiguous, and the compiler resolves it to the namespace.
Package id and namespace are allowed to differ, and here they have to.

## Why it exists

Because an FM link is not a linear channel, and measuring an FM mode against flat AWGN gives
numbers that mean nothing. Three things fall out of doing it properly:

- **The threshold effect.** Well above threshold the discriminator suppresses noise and the output
  beats the input carrier-to-noise ratio. Below it, click noise takes over and the output collapses
  far faster than the input degrades. FM modes do not fade away gracefully - they fall off a cliff,
  and where that cliff sits is the number that matters.
- **Triangular output noise.** Discriminator noise power rises with the square of audio frequency,
  so a mode's high-frequency content is measurably noisier than its low. This is why pre/de-emphasis
  exists, and why a wideband audio mode cannot be masked honestly against flat AWGN.
- **Emphasis and band limits are the channel.** A microphone input's pre-emphasis and 300-3000 Hz
  passband are not incidental - for a mode designed to work through mic and speaker they define
  what is transmittable at all.

## The carrier-to-noise convention

`cnrDb` is carrier power over noise power in the **receiver's IF bandwidth**
(`FmLinkProfile.IfBandwidthHz`, about 8 kHz on a 12.5 kHz channel and 16 kHz on 25 kHz). That is
where an FM receiver's threshold is defined.

It is deliberately **not** the SNR-in-3-kHz convention the HF/SSB world uses. The two are different
quantities and a number moved between them without conversion would be wrong, so anything reporting
results from this should say which it is quoting.

## Link profiles

| | |
|---|---|
| `FmLinkProfile.MicAndSpeaker(dev)` | what an ordinary handheld or mobile gives you: microphone in, speaker out, both emphasised and both band-limited to voice |
| `FmLinkProfile.DataPort(dev)` | a radio's data port: flat audio in, discriminator audio out, no emphasis and a much wider passband |

Everything is settable on the record directly - deviation, IF bandwidth, the audio passband at each
end, the emphasis time constants, a deviation calibration error, and flat-Rayleigh flutter.

`FmLinkProfile.IfBandwidthForSpacing()` gives a generic IF bandwidth for a channel spacing, 16 kHz
at 20 kHz spacing and above and 8 kHz below. Which spacing a mode belongs on is a property of the
mode, not a preference.

**`IfBandwidthHz` is a -6 dB total width, and radio datasheets are not consistent about which point
they quote.** A windowed sinc is half amplitude at its design cutoff, so setting this to 12600 gives
12.6 kHz between the -6 dB points and 12.1 kHz between the -3 dB points. A Tait TM8100's service
manual quotes "total IF 3 dB bandwidths" of 12.6 kHz wide and 7.8 kHz narrow; those are not the same
quantity, and a real crystal-plus-FPGA cascade has far gentler skirts than this filter, so its -6 dB
width sits well above its -3 dB width.

Mixing the two is a real trap and it has already been sprung once here: substituting the -3 dB
figures modelled a filter narrower than the radio they came from, and took a 25 kHz mode from 25
frames of 25 to none at all. That read as a finding about the radio and was arithmetic about
definitions. A Tait's wide channel is an ordinary 25 kHz channel, not a tight one.

## From a station description to a carrier-to-noise ratio

`Apply` asks for a carrier-to-noise ratio, which nobody knows about their own link. They know their
power, their feeder, their antennas, roughly what the path costs, and they can read an RSSI.

```csharp
var node = new Station(Station.Watts(25), FeederLossDb: 2, AntennaGainDbi: 6,
    ReceiverNoiseFigureDb: TaitTm8100.NoiseFigureDb(TaitBandwidth.Narrow));

double received = LinkBudget.ReceivedDbm(node, user, pathLossDb: 120);
double cnr = LinkBudget.CarrierToNoiseDb(received, ifBandwidth, node.ReceiverNoiseFigureDb,
    SiteNoise.Residential, frequencyMHz: 145);
```

**Where a station is sited is worth more on 2 m than most receiver work.** Man-made noise
(ITU-R P.372) puts the floor about 12 dB higher at a business site than a quiet rural one at
145 MHz, and almost nothing apart at 433 MHz where it has fallen below the receiver's own noise.
`SiteNoise` carries that, and a test pins both halves.

**Path loss is deliberately not modelled beyond free space.** A terrain model that looked
authoritative would be worse than asking; supply a measured loss, or an RSSI reading, or a
free-space figure you have chosen to accept.

## A radio, for the one configuration the documentation can support

`TaitTm8100.Link(TaitBandwidth.Narrow)` gives a TM8100 tapped at R1 and T13, which is the
configuration where **every stage whose behaviour Tait do not publish is bypassed**: no
pre-emphasis, no de-emphasis, no limiter, no voice-band filtering. So it is built entirely from
figures that carry a page number, and it refuses a deviation above its own class ceiling rather than
silently simulating an illegal station.

A microphone-path model is deliberately NOT offered. It would need the emphasis time constant, which
appears nowhere in Tait's 1083 pages, and the limiter's knee, attack and release, which appear
nowhere either. Build it yourself from `MicAndSpeaker` if you want it, and know which of its numbers
you chose.

## Two drive modes, and which one you want depends on where you inject

**Default, no limiter: every burst is scaled so its own peak lands on `PeakDeviationHz`.** That is
the right model for a tap PAST the transmitter's limiter, where nothing protects the modulator and
the operator therefore sets the drive once against the waveform's own peak. A Tait TM8100's T13 is
such a point: it sits after compression, encryption, the 300 Hz high pass, pre-emphasis, the limiter,
the 3 kHz low pass and the peak-system-deviation scaler, so an injected signal reaches the modulator
having met none of them. There, a waveform with a higher peak-to-average ratio genuinely does cost
you level across the whole burst, because you have to turn everything down to keep the peak legal.

**Set `LimitAtDeviationHz` and the burst is driven at a fixed gain with anything past the ceiling
hard clipped.** That is the right model for the microphone path, or for a tap BEFORE the limiter
such as T5. A Tait hard limits the pre-emphasised signal "to prevent overdeviation"
(MMA-00005-05 p.58) at a programmable ceiling defaulting to 2500 Hz narrow, 4000 Hz mid and 5000 Hz
wide. There a peaky waveform pays in distortion on the peaks rather than in level everywhere.

**The two answer differently and the difference is large**, so choosing the wrong one will mislead
you about your own waveform. Measured on an audio-band OFDM mode whose last symbol had an unusually
high peak: fixing that peak was worth about 3 dB with no limiter, and nothing measurable at all with
one. Under peak scaling the spike drags the entire burst down; under a limiter it is simply clipped,
and one symbol of eight is distorted while the rest are untouched. Same waveform, same fix, two
honest answers to two different questions.

The limiter's position and ceiling are documented; its knee, attack and release are not, anywhere in
Tait's 1083 pages, so it is modelled as an instantaneous hard clip. That is a modelling choice and is
labelled as one.

## Filters are specified in hertz

A windowed-sinc's transition width is roughly `rate/taps`, so a fixed tap count makes a filter
twice as sloppy each time the sample rate doubles - which is not how a radio behaves, its audio
filters being analogue and no wider for a sound card sampling faster. Every filter here is
therefore specified by its shape and the tap count derived: the transition is a fixed fraction of
the passband, at whatever rate the stage runs at.

That matters because it is what makes a measurement at one rate comparable with one at another. It
was not always so: up to 0.2.0 the tap counts were fixed, and the same waveform measured at two
rates went through two different channels with the higher rate penalised. **Nothing measured
through 0.2.0 or earlier is comparable with anything measured through 0.3.0 or later.**

## Licence

GPL-3.0-or-later. Extracted from
[packet-net/pdn-soundmodem](https://github.com/packet-net/pdn-soundmodem), which is
GPL-3.0-or-later, so this is too and must stay that way. See [COPYING](COPYING).
