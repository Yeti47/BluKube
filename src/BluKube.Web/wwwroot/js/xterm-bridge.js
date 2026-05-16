// xterm-bridge.js
// Thin JS layer between Blazor/Spectre and xterm.js.
// Loaded after xterm.js and xterm-addon-fit.js from CDN.

// ── localStorage helpers (used by LocalStorageConfigStore) ──────────────────
window.bluKubeStorage = {
  load: (key) => localStorage.getItem(key),
  save: (key, value) => localStorage.setItem(key, value),
  remove: (key) => localStorage.removeItem(key),
};

window.xtermBridge = (() => {
  let _terminal = null;
  let _fitAddon = null;
  let _resizeHandler = null;

  // ── Web Audio / PCM playback ──────────────────────────────────────────────
  let _audioCtx = null; // AudioContext (48 kHz stereo)
  let _scheduleTime = 0; // next buffer start time in AudioContext clock

  const SAMPLE_RATE = 48000;
  const CHANNELS = 2;
  const SCHEDULE_AHEAD_SEC = 0.15; // 150 ms scheduling look-ahead

  function initAudio() {
    if (_audioCtx) return;
    if (!window.AudioContext && !window.webkitAudioContext) return;

    _audioCtx = new (window.AudioContext || window.webkitAudioContext)({
      sampleRate: SAMPLE_RATE,
    });
    _scheduleTime = _audioCtx.currentTime + SCHEDULE_AHEAD_SEC;
  }

  function resumeAudio() {
    initAudio();
    if (_audioCtx?.state === "suspended") _audioCtx.resume();
  }

  function schedulePcm(bytes) {
    if (!_audioCtx) return;

    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const frameCount = Math.floor(bytes.byteLength / (CHANNELS * 2));
    if (frameCount <= 0) return;

    const buffer = _audioCtx.createBuffer(CHANNELS, frameCount, SAMPLE_RATE);
    const channelData = [];
    for (let ch = 0; ch < CHANNELS; ch++)
      channelData.push(buffer.getChannelData(ch));

    let offset = 0;
    for (let frame = 0; frame < frameCount; frame++) {
      for (let ch = 0; ch < CHANNELS; ch++) {
        channelData[ch][frame] = view.getInt16(offset, true) / 32768;
        offset += 2;
      }
    }

    const now = _audioCtx.currentTime;
    if (_scheduleTime < now + SCHEDULE_AHEAD_SEC) {
      _scheduleTime = now + SCHEDULE_AHEAD_SEC;
    }

    const source = _audioCtx.createBufferSource();
    source.buffer = buffer;
    source.connect(_audioCtx.destination);
    source.start(_scheduleTime);
    _scheduleTime += buffer.duration;
  }

  return {
    prefersNativeClient() {
      const coarsePointer =
        window.matchMedia?.("(pointer: coarse)").matches ?? false;
      const narrowViewport =
        window.matchMedia?.("(max-width: 720px)").matches ?? false;
      return coarsePointer || narrowViewport || navigator.maxTouchPoints > 0;
    },

    resumeAudio() {
      resumeAudio();
    },

    /**
     * Initialise an xterm.js Terminal inside the given container element.
     *
     * @param {DotNet.DotNetObject} dotNetRef - .NET callback target
     *        that exposes [JSInvokable] OnXtermKey.
     * @param {string} containerId - id of the host <div>.
     * @returns {{ cols: number, rows: number }} terminal dimensions.
     */
    init(dotNetRef, containerId) {
      this.dispose();

      const container = document.getElementById(containerId);

      _terminal = new Terminal({
        convertEol: true, // \n → \r\n so Spectre line-endings work
        cursorBlink: true,
        fontSize: 14,
        scrollback: 0,
        fontFamily:
          '"Cascadia Mono", "Cascadia Code", "Fira Mono", "DejaVu Sans Mono", "Liberation Mono", "Noto Sans Mono", monospace',
        fontWeight: 400,
        fontWeightBold: 700,
        letterSpacing: 0,
        lineHeight: 1.0,
        theme: {
          background: "#0d1117",
          foreground: "#c9d1d9",
        },
      });

      // Fit addon resizes the terminal to fill the container.
      const FitAddonClass = window.FitAddon?.FitAddon ?? window.FitAddon;
      _fitAddon = new FitAddonClass();
      _terminal.loadAddon(_fitAddon);
      _terminal.open(container);
      _fitAddon.fit();

      document.fonts?.ready.then(() => _fitAddon?.fit());

      // Forward key events to the .NET terminal key dispatcher.
      // Also initialise/resume AudioContext here — onKey fires in a user-gesture context.
      _terminal.onKey(({ domEvent }) => {
        const key = domEvent.key?.toLowerCase();
        if (
          domEvent.altKey &&
          !domEvent.ctrlKey &&
          (key === "c" || key === "x")
        ) {
          domEvent.preventDefault();
          domEvent.stopPropagation();
        }

        resumeAudio();
        dotNetRef.invokeMethodAsync(
          "OnXtermKey",
          domEvent.key,
          domEvent.shiftKey,
          domEvent.ctrlKey,
          domEvent.altKey,
        );
      });

      // Re-fit when the browser window resizes.
      _resizeHandler = () => {
        _fitAddon?.fit();
      };
      window.addEventListener("resize", _resizeHandler);

      return { cols: _terminal.cols, rows: _terminal.rows };
    },

    /**
     * Write an ANSI string to the terminal. Called by the Blazor output
     * pump for each chunk arriving from Spectre.Console's TextWriter.
     *
     * @param {string} data - raw ANSI/VT100 text.
     */
    write(data) {
      _terminal?.write(data);
    },

    /**
     * Schedule one chunk of interleaved signed 16-bit little-endian PCM.
     * Called by the Blazor audio pump after server-side Opus decoding.
     *
     * @param {string} base64Data - base64-encoded PCM bytes.
     */
    writeAudio(base64Data) {
      resumeAudio();
      if (!_audioCtx) return;

      // Decode base64 → Uint8Array.
      const binary = atob(base64Data);
      const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);

      schedulePcm(bytes);
    },

    dispose() {
      if (_resizeHandler) {
        window.removeEventListener("resize", _resizeHandler);
        _resizeHandler = null;
      }

      try {
        _terminal?.dispose();
      } catch {}
      try {
        _audioCtx?.close();
      } catch {}

      _terminal = null;
      _fitAddon = null;
      _audioCtx = null;
      _scheduleTime = 0;
    },
  };
})();
