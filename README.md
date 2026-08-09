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

`FmLinkProfile.IfBandwidthForSpacing()` gives the IF bandwidth for a channel spacing, and the
figures are a real radio's rather than a rule of thumb: 12.6 kHz wide, 12.0 kHz medium, 7.8 kHz
narrow, from the Tait TM8100/TM8200 service manual MMA-00005-05 p.73 Table 3.1. Which spacing a mode
belongs on is a property of the mode, not a preference.

Up to 0.3.0 this returned 16 kHz for anything at or above 20 kHz spacing and 8 kHz below, which is
27 % too wide on a 25 kHz channel and 33 % too wide on a 20 kHz one. Since the carrier-to-noise
ratio is stated in this bandwidth, that moved every wide-channel measurement by about 1.1 dB, in
the direction that flatters the modem. **Wide and medium channel numbers measured before 0.4.0 are
not comparable with numbers measured after it.**

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
