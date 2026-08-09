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

`FmLinkProfile.IfBandwidthForSpacing()` gives the usual IF bandwidth for a channel spacing. Which
spacing a mode belongs on is a property of the mode, not a preference.

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
