/* Mobile navigation toggle for the Elpis site nav.
   The header markup ships a .nav__mobile-toggle button and a .nav__items list
   that CSS hides below 1024px; this script opens/closes it (and the in-panel
   dropdowns are shown inline via CSS, since hover doesn't work on touch). */
(function () {
  var nav = document.querySelector('.nav');
  var btn = nav && nav.querySelector('.nav__mobile-toggle');
  if (!nav || !btn) return;

  function setOpen(open) {
    nav.classList.toggle('nav--open', open);
    btn.setAttribute('aria-expanded', open ? 'true' : 'false');
    btn.setAttribute('aria-label', open ? 'Close menu' : 'Open menu');
  }

  btn.addEventListener('click', function () {
    setOpen(!nav.classList.contains('nav--open'));
  });

  // Close after tapping any link in the menu.
  nav.querySelectorAll('.nav__items a').forEach(function (a) {
    a.addEventListener('click', function () { setOpen(false); });
  });

  // Close on Escape, and when resizing up to desktop.
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') setOpen(false);
  });
  window.addEventListener('resize', function () {
    if (window.innerWidth >= 1024) setOpen(false);
  });
})();
