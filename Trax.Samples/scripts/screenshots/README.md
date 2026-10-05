# Sample screenshots

The image at the top of each GUI sample's README, and the copies traxsharp.net shows, come from this script. It
drives a running sample through what it demonstrates and takes one shot in dark mode: 1600×900, or 1600×1080 for
StateMachine so its payment provider fits.

```bash
cd scripts/screenshots
npm ci
npx playwright install chromium

# Start one sample as its README says (fresh data gives a clean shot), then:
node shoot.mjs recovery              # samples/Recovery/screenshot.png
node shoot.mjs chat-service          # samples/ChatService/screenshot.png
node shoot.mjs state-machine         # samples/StateMachine/screenshot.png
node shoot.mjs signalr-broadcaster   # samples/SignalRBroadcaster/screenshot.png
```

`--copy-to <dir>` also writes the image to `<dir>/<scene>.png`. traxsharp.net no longer shows these shots: its landing
page plays recordings of the samples instead, from [`scripts/recordings`](../recordings/README.md). The three React clients all listen on port 5173, so run one sample at a time.
