/**
 * BREWMASTER 2000 — synthesized sound effects (WebAudio, no audio files).
 *
 * Exposes window.coffeeSounds = { setMuted(bool), toggleMuted(), muted(), play(name, arg) }
 * plus a small keyboard bridge (window.coffeeKeyboard) used by the app.
 *
 * The AudioContext is created lazily and only ever resumed inside a user
 * gesture (pointerdown/keydown) or a play() call that itself originates from
 * a gesture — never on load, so nothing autoplays.
 */
(function () {
    "use strict";

    var ctx = null;
    var master = null;
    var muted = false;
    try {
        muted = localStorage.getItem("cm-muted") === "1";
    } catch (e) { /* private mode */ }

    function ensureContext() {
        if (!ctx) {
            var AC = window.AudioContext || window.webkitAudioContext;
            if (!AC) return null;
            ctx = new AC();
            master = ctx.createGain();
            master.gain.value = 0.5;
            master.connect(ctx.destination);
        }
        if (ctx.state === "suspended") {
            ctx.resume().catch(function () { /* autoplay policy: retried on next gesture */ });
        }
        return ctx;
    }

    function unlock() { ensureContext(); }
    window.addEventListener("pointerdown", unlock, { once: true, passive: true });
    window.addEventListener("keydown", unlock, { once: true, passive: true });

    // ---------------------------------------------------------------- helpers

    function envelope(gainParam, t0, peak, attack, decay) {
        gainParam.cancelScheduledValues(t0);
        gainParam.setValueAtTime(0.0001, t0);
        gainParam.exponentialRampToValueAtTime(Math.max(peak, 0.0001), t0 + attack);
        gainParam.exponentialRampToValueAtTime(0.0001, t0 + attack + decay);
    }

    function blip(opts) {
        var c = ensureContext();
        if (!c) return;
        var t0 = c.currentTime;
        var osc = c.createOscillator();
        osc.type = opts.type || "triangle";
        osc.frequency.setValueAtTime(opts.f0, t0);
        if (opts.f1 && opts.f1 !== opts.f0) {
            osc.frequency.exponentialRampToValueAtTime(opts.f1, t0 + (opts.dur || 0.1));
        }
        var g = c.createGain();
        envelope(g.gain, t0, opts.gain || 0.5, opts.attack || 0.004, opts.dur || 0.1);
        osc.connect(g);
        g.connect(master);
        osc.start(t0);
        osc.stop(t0 + (opts.attack || 0.004) + (opts.dur || 0.1) + 0.05);
    }

    var noiseCache = null;
    function noiseBuffer(c) {
        if (!noiseCache) {
            var length = Math.floor(c.sampleRate * 1.5);
            var buffer = c.createBuffer(1, length, c.sampleRate);
            var data = buffer.getChannelData(0);
            for (var i = 0; i < length; i++) data[i] = Math.random() * 2 - 1;
            noiseCache = buffer;
        }
        return noiseCache;
    }

    function noiseBurst(opts) {
        var c = ensureContext();
        if (!c) return;
        var t0 = c.currentTime;
        var src = c.createBufferSource();
        src.buffer = noiseBuffer(c);
        src.loop = true;
        var filter = c.createBiquadFilter();
        filter.type = opts.filterType || "bandpass";
        filter.frequency.value = opts.freq || 1000;
        filter.Q.value = opts.q || 1;
        var g = c.createGain();
        envelope(g.gain, t0, opts.gain || 0.4, opts.attack || 0.005, opts.dur || 0.2);
        src.connect(filter);
        filter.connect(g);
        g.connect(master);
        src.start(t0);
        src.stop(t0 + (opts.attack || 0.005) + (opts.dur || 0.2) + 0.05);
    }

    // ---------------------------------------------------------------- effects

    var effects = {
        coinInsert: function () {
            blip({ f0: 2400, f1: 1700, type: "square", dur: 0.07, gain: 0.16 });
            blip({ f0: 3600, f1: 2500, type: "sine", dur: 0.05, gain: 0.09 });
        },
        coinClink: function () {
            blip({ f0: 2900 + Math.random() * 400, f1: 2100, type: "triangle", dur: 0.06, gain: 0.15 });
            blip({ f0: 4400, f1: 3300, type: "sine", dur: 0.04, gain: 0.06 });
        },
        coinReject: function () {
            noiseBurst({ filterType: "lowpass", freq: 900, dur: 0.12, gain: 0.32 });
            blip({ f0: 220, f1: 140, type: "sine", dur: 0.16, gain: 0.28 });
        },
        buttonPress: function () {
            noiseBurst({ filterType: "bandpass", freq: 1600, q: 2, dur: 0.04, gain: 0.18 });
            blip({ f0: 520, f1: 420, type: "square", dur: 0.03, gain: 0.07 });
        },
        pour: function (ms) {
            var c = ensureContext();
            if (!c) return;
            var dur = (ms || 2200) / 1000;
            var t0 = c.currentTime;
            var src = c.createBufferSource();
            src.buffer = noiseBuffer(c);
            src.loop = true;
            var filter = c.createBiquadFilter();
            filter.type = "lowpass";
            filter.frequency.setValueAtTime(500, t0);
            filter.frequency.linearRampToValueAtTime(300, t0 + dur);
            var g = c.createGain();
            g.gain.setValueAtTime(0.0001, t0);
            g.gain.exponentialRampToValueAtTime(0.12, t0 + 0.15);
            g.gain.setValueAtTime(0.12, t0 + Math.max(0.15, dur - 0.3));
            g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
            src.connect(filter);
            filter.connect(g);
            g.connect(master);
            src.start(t0);
            src.stop(t0 + dur + 0.1);
        },
        steam: function (ms) {
            var c = ensureContext();
            if (!c) return;
            var dur = (ms || 1200) / 1000;
            var t0 = c.currentTime;
            var src = c.createBufferSource();
            src.buffer = noiseBuffer(c);
            src.loop = true;
            var filter = c.createBiquadFilter();
            filter.type = "bandpass";
            filter.frequency.setValueAtTime(1800, t0);
            filter.frequency.linearRampToValueAtTime(2600, t0 + dur);
            filter.Q.value = 0.8;
            var g = c.createGain();
            g.gain.setValueAtTime(0.0001, t0);
            g.gain.exponentialRampToValueAtTime(0.07, t0 + dur * 0.5);
            g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
            src.connect(filter);
            filter.connect(g);
            g.connect(master);
            src.start(t0);
            src.stop(t0 + dur + 0.1);
        },
        coinCascade: function (count) {
            var n = Math.min(count || 4, 8);
            for (var i = 0; i < n; i++) {
                window.setTimeout(effects.coinClink, i * 90);
            }
        },
        successChime: function () {
            var notes = [880, 1108.73, 1318.51];
            notes.forEach(function (freq, i) {
                window.setTimeout(function () {
                    blip({ f0: freq, type: "sine", dur: 0.35, gain: 0.15, attack: 0.01 });
                    blip({ f0: freq * 2, type: "sine", dur: 0.25, gain: 0.05, attack: 0.01 });
                }, i * 130);
            });
        }
    };

    // ---------------------------------------------------------------- public API

    window.coffeeSounds = {
        setMuted: function (value) {
            muted = !!value;
            try { localStorage.setItem("cm-muted", muted ? "1" : "0"); } catch (e) { /* private mode */ }
            return muted;
        },
        toggleMuted: function () {
            return window.coffeeSounds.setMuted(!muted);
        },
        muted: function () {
            return muted;
        },
        play: function (name, arg) {
            if (muted) return;
            var fn = effects[name];
            if (!fn) return;
            if (!ensureContext()) return;
            fn(arg);
        }
    };

    // ---------------------------------------------------------------- keyboard bridge

    window.coffeeMotion = {
        reduced: function () {
            return !!(window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches);
        }
    };

    window.coffeeKeyboard = {
        register: function (dotnetRef) {
            if (window.coffeeKeyboard._handler) return;
            var handler = function (event) {
                var target = event.target;
                if (!target) return;
                var tag = (target.tagName || "").toLowerCase();
                if (tag === "input" || tag === "textarea" || tag === "select" || target.isContentEditable) return;
                if (event.metaKey || event.ctrlKey || event.altKey) return;
                var key = event.key;
                if (key === undefined || key === null) return;
                if (key.length === 1) key = key.toLowerCase();
                var map = {
                    "q": "coin-5", "w": "coin-10", "e": "coin-20", "r": "coin-50",
                    "t": "coin-100", "y": "coin-200",
                    "1": "select-1", "2": "select-2", "3": "select-3",
                    "c": "cancel", "m": "mute"
                };
                var action = map[key];
                if (action) {
                    event.preventDefault();
                    dotnetRef.invokeMethodAsync("OnKey", action);
                }
            };
            window.coffeeKeyboard._handler = handler;
            window.addEventListener("keydown", handler);
        },
        unregister: function () {
            if (window.coffeeKeyboard._handler) {
                window.removeEventListener("keydown", window.coffeeKeyboard._handler);
                window.coffeeKeyboard._handler = null;
            }
        }
    };
})();
