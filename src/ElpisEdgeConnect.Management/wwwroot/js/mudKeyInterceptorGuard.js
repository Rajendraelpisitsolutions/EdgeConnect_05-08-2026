// ============================================================================
// mudKeyInterceptorGuard.js — null-guard for MudBlazor 7.15's key interceptor.
//
// MudBlazor's MudKeyInterceptor reads `args.key.toLowerCase()` unconditionally
// in its keydown/keyup listeners (MudBlazor.min.js:39:66 and :47:64). A
// `keydown`/`keyup` event that carries no `key` property — e.g. a bare
// `new Event('keydown')` dispatched by a browser extension (password manager,
// form filler, writing assistant) or by Chrome's autofill machinery — makes
// that call throw:
//
//     Uncaught TypeError: Cannot read properties of undefined (reading 'toLowerCase')
//         at HTMLInputElement.onKeyDown (MudBlazor.min.js:39:66)
//
// The exception is thrown inside a plain DOM listener, so it never reaches the
// Blazor circuit — the only visible effect is console noise, two entries per
// injected event pair. Upstream declined to fix it (MudBlazor issue #10408,
// closed as not planned), so the guard lives here rather than in the package.
//
// Skipping a key event that has no key is the correct behaviour: the
// interceptor's whole job is to match `args.key` against registered key
// options, and there is nothing to match. Events that do carry a key are
// passed through untouched, so arrow-key handling on MudNumericField,
// MudSelect, MudSwitch, MudRadio and MudChipSet is unaffected.
//
// Must run after MudBlazor.min.js and before any component connects an
// interceptor: `attachHandlers` passes the prototype method to
// `addEventListener`, which captures the function reference at that moment —
// patching the prototype later has no effect on already-attached listeners.
// The <script> tag in App.razor sits directly after MudBlazor.min.js, which
// is well before the first OnAfterRenderAsync, so this holds.
//
// `window.__mudKeylessKeyEvents` counts suppressed events so the condition
// stays diagnosable from the console without emitting noise of its own.
// ============================================================================

(function () {
  window.__mudKeylessKeyEvents = 0;

  // MudBlazor declares `class MudKeyInterceptor` at script top level, which
  // creates a global lexical binding rather than a property on `window` —
  // reachable by identifier from this classic script, but not via
  // `window.MudKeyInterceptor`. Resolve it defensively either way.
  var ctor = null;
  try {
    ctor = typeof MudKeyInterceptor === 'function' ? MudKeyInterceptor : null;
  } catch (e) {
    ctor = null;
  }
  if (!ctor || !ctor.prototype) {
    return;
  }

  ['onKeyDown', 'onKeyUp'].forEach(function (name) {
    var original = ctor.prototype[name];
    if (typeof original !== 'function' || original.__keyGuarded) {
      return;
    }
    var guarded = function (args) {
      if (!args || typeof args.key !== 'string') {
        window.__mudKeylessKeyEvents++;
        return;
      }
      return original.call(this, args);
    };
    guarded.__keyGuarded = true;
    ctor.prototype[name] = guarded;
  });
})();
