// ============================================================================
// licenseMail.js — safe mailto opener for the Buy License dialog.
//
// The previous approach, window.open('mailto:...', '_blank'), creates a popup
// window that immediately unloads to hand the URL to the OS mail handler. Chrome
// now blocks that via Permissions-Policy ("unload is not allowed in this
// document"), so nothing opened. A same-tab anchor click to a mailto: invokes
// the OS mail handler WITHOUT navigating/unloading the Blazor page.
//
// Window-scoped (not a module) to match wizardValidation.js / bundleDownload.js
// and load via the <script> tag in App.razor.
// ============================================================================

window.licenseMail = {
  /** Open a mailto: (or any external-protocol) URL without unloading the page. */
  open: function (url) {
    try {
      const a = document.createElement('a');
      a.href = url;
      a.rel = 'noopener';
      // No target="_blank" — a same-tab external-protocol click does not
      // unload the document, so the Permissions-Policy is not tripped.
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      return true;
    } catch (e) {
      console.error('licenseMail.open failed', e);
      return false;
    }
  }
};
