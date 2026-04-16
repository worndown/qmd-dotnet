# Podcasting Basics

## Equipment Essentials

### Microphones

A good microphone is the single most important equipment investment for a podcast. There are two main types:

- **Dynamic microphones**: Robust, reject background noise, and don't require phantom power. The Shure SM7B and Rode PodMic are industry standards for voice. Dynamic mics need a preamp or audio interface with enough gain (60+ dB) — budget interfaces may produce a noisy signal.
- **Condenser microphones**: More sensitive, capturing a wider frequency range with more detail. Great for treated rooms but pick up room reflections, keyboard clicks, and background noise. The Audio-Technica AT2020 and Rode NT1 are popular choices.

USB microphones (Blue Yeti, Rode NT-USB Mini) connect directly to a computer without an audio interface. Convenient for solo recording, but multiple USB mics on one computer cause driver conflicts — multi-person setups need an XLR interface.

### Audio Interfaces

An audio interface converts analog microphone signals to digital audio for your computer. Popular interfaces for podcasting include the Focusrite Scarlett 2i2 (two XLR inputs), the Rode Rodecaster Pro (four inputs with built-in effects and sound pads), and the Zoom PodTrak P4 (portable, battery-powered, records to SD card).

Key specs: 24-bit / 48kHz recording resolution is the standard for voice. Higher sample rates (96kHz) are unnecessary for speech and waste storage space.

### Acoustic Treatment

Room acoustics affect recording quality more than microphone choice. Hard, parallel surfaces (walls, ceilings, desk) cause reflections that make audio sound hollow or echoey. Basic treatment includes:

- **Acoustic foam panels**: Placed at reflection points on walls. The 2-inch thick variety absorbs mid and high frequencies.
- **Moving blankets**: Hung behind the microphone or draped over a frame. A cheap, effective absorber.
- **Bass traps**: Thick foam or fiberglass panels placed in room corners where bass frequencies accumulate.
- **Proximity effect**: Getting closer to a directional microphone (5-15cm) increases bass response and reduces room sound. Use a pop filter or windscreen to control plosives (P and B sounds that blast air into the mic).

## Recording Workflow

### Software (DAWs)

Digital Audio Workstations for podcast recording and editing:

- **Audacity**: Free, open-source, cross-platform. Basic but effective. The noise reduction tool removes consistent background hum (record 10 seconds of room silence for a noise profile).
- **Adobe Audition**: Professional multitrack editing with spectral frequency display and AI-powered speech enhancement. Subscription-based.
- **Reaper**: Affordable ($60 license), extremely powerful and customizable. Steep learning curve but unmatched flexibility.
- **Descript**: Edits audio by editing a transcript — delete words from the text, and the audio follows. Includes AI filler word removal ("um", "uh", "you know") and studio sound processing.
- **GarageBand**: Free on macOS/iOS. Simple multitrack recording with built-in effects. Good enough for many podcasts.

### Recording Best Practices

Record in WAV or FLAC (lossless) format during production. Export the final episode as MP3 (128-192 kbps) or AAC (96-128 kbps) for distribution — these compressed formats reduce file size by 80-90% with minimal audible quality loss for speech.

Record each speaker on a separate track (multi-track recording). This allows independent editing, noise removal, and volume adjustment per speaker. If recording remote guests, use a double-ender setup: each person records locally and shares the high-quality file. Remote recording platforms (Riverside, SquadCast, Zencastr) handle this automatically.

## Editing Techniques

### Basic Editing Workflow

1. **Remove silence and dead air**: Cut or compress long pauses. Most editors have a silence detection tool.
2. **Cut mistakes**: Remove stumbles, restarts, and tangents. Cross-fade edits (10-50ms) to avoid clicks at cut points.
3. **Level the volume**: Use compression (3:1 ratio, threshold at -18 dB, fast attack, medium release) to even out loud and quiet moments. Then normalize peaks to -1 dB.
4. **Apply EQ**: A high-pass filter at 80-100 Hz removes low-frequency rumble (traffic, HVAC, handling noise). A gentle presence boost at 3-5 kHz improves speech intelligibility.
5. **Add intro/outro**: Layer music and voice with proper gain staging — music should sit 15-20 dB below speech.

### Audio Cleanup

- **De-essing**: Reduces harsh sibilance (sharp S and T sounds). Use a de-esser plugin targeting 5-8 kHz.
- **Noise gate**: Mutes the track when the signal drops below a threshold, eliminating background noise between speech. Set threshold just above the noise floor.
- **Noise reduction**: Use spectral noise reduction (Audacity, iZotope RX) to remove constant background sounds. Apply lightly — aggressive noise reduction creates metallic artifacts.

## Hosting and Distribution

A podcast host stores your audio files and generates the RSS feed that directories (Apple Podcasts, Spotify, Google Podcasts) subscribe to. Popular hosts include Buzzsprout, Libsyn, Podbean, and Anchor (free, owned by Spotify). Upload your exported MP3, add episode title, description, show notes, and chapter markers.

### RSS Feed

The RSS feed is the core distribution mechanism. It's an XML document containing your podcast metadata (title, author, artwork) and a list of episodes with their audio file URLs. When you publish a new episode, the host updates the RSS feed, and podcast apps check for updates periodically (every 1-12 hours depending on the app).

## Monetization

Podcasts generate revenue through advertising (CPM-based — typically $15-50 per thousand downloads for mid-roll ads), sponsorships (flat-rate deals), listener support (Patreon, membership tiers), and premium content (bonus episodes behind a paywall). Most podcasts need at least 1,000-5,000 downloads per episode to attract advertisers. Focus on growing a loyal audience through consistent publishing and genuine engagement before pursuing monetization.
