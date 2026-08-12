/*
  File:    docs/marketing/web/gate.js
  Purpose: Resource-download gate (MOCKUP). Per the gating decision (gate
           every download), any element marked [data-gate] opens a contact-
           capture form before the file is revealed. This is a VISUAL mockup
           only — no data is stored, validated, or sent. Functional capture /
           CRM is a Phase 4 deliverable.
  Usage:   <a href="assets/file.pdf" data-gate data-asset="Platform datasheet"
              class="btn ...">Download</a>
           <script src="gate.js" defer></script>
*/
(function () {
  var css = ''
    + '.gate-overlay{position:fixed;inset:0;background:rgba(15,20,25,0.66);display:flex;'
    + 'align-items:center;justify-content:center;padding:var(--space-5);z-index:200;}'
    + '.gate-overlay[hidden]{display:none;}'
    + '.gate-card{background:#fff;border-radius:14px;max-width:460px;width:100%;'
    + 'padding:var(--space-8);position:relative;box-shadow:0 24px 64px rgba(0,0,0,0.4);}'
    + '.gate-close{position:absolute;top:var(--space-4);right:var(--space-4);background:none;'
    + 'border:none;font-size:24px;line-height:1;color:var(--color-text-muted-light);cursor:pointer;}'
    + '.gate-eyebrow{color:var(--color-brand-teal);font-size:var(--size-xs);font-weight:700;'
    + 'letter-spacing:1.5px;text-transform:uppercase;margin:0 0 var(--space-2);}'
    + '.gate-title{font-size:var(--size-lg);font-weight:700;color:var(--color-text-heading-light);'
    + 'line-height:1.25;margin:0 0 var(--space-2);}'
    + '.gate-sub{color:var(--color-text-muted-light);font-size:var(--size-sm);margin:0 0 var(--space-5);}'
    + '.gate-form label{display:block;font-size:var(--size-sm);font-weight:600;'
    + 'color:var(--color-text-heading-light);margin-bottom:var(--space-3);}'
    + '.gate-form input{display:block;width:100%;margin-top:4px;padding:9px 10px;font:inherit;'
    + 'font-size:var(--size-sm);border:1px solid var(--color-border-light-strong);border-radius:7px;'
    + 'box-sizing:border-box;}'
    + '.gate-form button{width:100%;margin-top:var(--space-3);}'
    + '.gate-note{color:var(--color-text-muted-light);font-size:var(--size-xs);font-style:italic;'
    + 'margin-top:var(--space-3);}'
    + '.gate-success{text-align:center;}'
    + '.gate-success p{color:var(--color-text-heading-light);font-size:var(--size-md);'
    + 'font-weight:600;margin:0 0 var(--space-4);}';
  var st = document.createElement('style'); st.textContent = css; document.head.appendChild(st);

  var modal = document.createElement('div');
  modal.className = 'gate-overlay'; modal.id = 'gate-overlay'; modal.setAttribute('hidden', '');
  modal.innerHTML =
      '<div class="gate-card" role="dialog" aria-modal="true" aria-labelledby="gate-title">'
    + '<button class="gate-close" type="button" aria-label="Close">×</button>'
    + '<p class="gate-eyebrow">Resource download</p>'
    + '<h2 id="gate-title" class="gate-title">A few details before your download</h2>'
    + '<p class="gate-sub" id="gate-asset"></p>'
    + '<form class="gate-form" id="gate-form" novalidate>'
    + '<label>Full name<input required type="text" name="name" autocomplete="name"></label>'
    + '<label>Work email<input required type="email" name="email" autocomplete="email"></label>'
    + '<label>Company<input required type="text" name="company" autocomplete="organization"></label>'
    + '<label>Role (optional)<input type="text" name="role"></label>'
    + '<button class="btn btn--primary btn--lg" type="submit">Get the download →</button>'
    + '<p class="gate-note">Mockup only — no data is stored or sent. Functional capture is a Phase 4 deliverable.</p>'
    + '</form>'
    + '<div class="gate-success" hidden>'
    + '<p>✓ Thanks — your download is ready.</p>'
    + '<a class="btn btn--primary btn--lg" id="gate-dl" href="#">Download now →</a>'
    + '</div>'
    + '</div>';
  document.body.appendChild(modal);

  var assetEl = modal.querySelector('#gate-asset'),
      form = modal.querySelector('#gate-form'),
      success = modal.querySelector('.gate-success'),
      dl = modal.querySelector('#gate-dl');
  var target = '#';

  function openGate(file, asset) {
    target = file || '#';
    assetEl.textContent = 'Requesting: ' + (asset || 'this resource') + '. We send it to your work email and unlock it here.';
    form.hidden = false; success.hidden = true;
    try { form.reset(); } catch (e) {}
    modal.removeAttribute('hidden');
    var first = form.querySelector('input'); if (first) first.focus();
  }
  function closeGate() { modal.setAttribute('hidden', ''); }

  document.addEventListener('click', function (e) {
    var trigger = e.target.closest('[data-gate]');
    if (trigger) { e.preventDefault(); openGate(trigger.getAttribute('href') || trigger.getAttribute('data-file'), trigger.getAttribute('data-asset')); return; }
    if (e.target.closest('.gate-close') || e.target === modal) { closeGate(); }
  });
  document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeGate(); });
  form.addEventListener('submit', function (e) {
    e.preventDefault();
    form.hidden = true; success.hidden = false; dl.setAttribute('href', target);
  });
})();
