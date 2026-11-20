// Small, non-invasive accessibility and editor enhancements.
// Business data and all form submission logic stay on the server.
(() => {
  'use strict';
  const slugify = (value) => String(value || '').normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase().replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '').slice(0, 205).replace(/-+$/g, '');
  function init() {
    document.querySelectorAll('[data-auto-slug]').forEach(preview => {
      const form = preview.closest('form');
      const name = form?.querySelector('input[name="Name"]');
      if (!name) return;
      const originalName = name.value.trim();
      const originalSlug = preview.dataset.slugValue || '';
      const type = preview.dataset.autoSlug || 'item';
      const render = () => {
        const value = name.value.trim();
        const slug = originalSlug && value.toLowerCase() === originalName.toLowerCase()
          ? originalSlug : slugify(value) || type;
        preview.textContent = value ? '/Product/Details/' + slug : 'Enter the product name to generate a URL';
      };
      name.addEventListener('input', render);
      render();
    });
    // Make an error summary discoverable after validation without submitting twice.
    const errorSummary = document.querySelector('.validation-summary-errors, .pp-validation.validation-summary-errors');
    if (errorSummary) {
      errorSummary.setAttribute('role', 'alert');
      errorSummary.setAttribute('aria-live', 'polite');
    }
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init, { once: true });
  else init();
})();
