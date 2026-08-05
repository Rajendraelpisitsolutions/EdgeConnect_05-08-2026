// ============================================================================
// wizardValidation.js — JS interop helpers for WizardValidationBanner.
//
// M.2d.4 / ADR-0015 Rule 5: clicking a validation message with a non-null
// FieldAnchor (a CSS selector, typically "#field-{kebab-case-path}") scrolls
// the referenced element into view and focuses it. Non-existent selectors
// are no-ops — never throws into the Blazor circuit.
//
// Window-scoped (not a JS module) so it loads via the <script> tag in
// App.razor without per-component dynamic import ceremony. This is the
// existing pattern (see Config.razor's navigator.clipboard.writeText call).
// ============================================================================

window.wizardValidation = {
  /**
   * Scroll the element matching `selector` into view and focus it.
   * @param {string|null} selector A CSS selector (e.g. "#field-instance-id").
   *                               Null/empty/no-match = no-op.
   */
  scrollToFieldAnchor: function (selector) {
    if (!selector) {
      return;
    }
    let el;
    try {
      el = document.querySelector(selector);
    } catch (e) {
      // Malformed selector — swallow rather than surface as an interop error.
      return;
    }
    if (!el) {
      return;
    }
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    // scrollIntoView already shifts viewport; preventScroll on focus avoids
    // a competing jump-to-element scroll from the browser's default focus
    // behaviour (which can land the element at the top edge rather than
    // the centred position scrollIntoView just set).
    if (typeof el.focus === 'function') {
      el.focus({ preventScroll: true });
    }
  },

  /**
   * Commit the currently-focused input's value before a Save reads the model.
   *
   * Blazor inputs that update @bind-Value on "change" only push their value to
   * the model when the element loses focus. If the operator types into a field
   * (e.g. a tag Address) and immediately clicks Save, the click can be
   * processed before the field's change/commit round-trip — so Save reads the
   * old value (typically 0). Blurring the active element fires its change
   * handler, flushing the typed value into the model. No-op when nothing is
   * focused. Never throws into the Blazor circuit.
   */
  flushActiveInput: function () {
    try {
      const el = document.activeElement;
      if (el && el !== document.body && typeof el.blur === 'function') {
        el.blur();
      }
    } catch (e) {
      // Swallow — flushing is best-effort and must never break Save.
    }
  }
};
