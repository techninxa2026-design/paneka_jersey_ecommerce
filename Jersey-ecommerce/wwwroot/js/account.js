(function () {
  document.querySelectorAll('[data-toggle-password]').forEach(function (button) {
    button.addEventListener('click', function () {
      var input = button.parentElement.querySelector('input');
      if (!input) return;
      var show = input.type === 'password';
      input.type = show ? 'text' : 'password';
      button.textContent = show ? 'Hide' : 'Show';
      button.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
    });
  });

  document.querySelectorAll('[data-image-input]').forEach(function (input) {
    var container = input.closest('.photo-upload');
    var preview = container && container.querySelector('[data-image-preview]');
    var placeholder = container && container.querySelector('[data-preview-placeholder]');
    var objectUrl = null;
    input.addEventListener('change', function () {
      if (objectUrl) URL.revokeObjectURL(objectUrl);
      var file = input.files && input.files[0];
      if (!file || !preview) return;
      if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type) || file.size > 2 * 1024 * 1024) {
        alert('Choose a JPG, PNG or WEBP image smaller than 2 MB.');
        input.value = '';
        return;
      }
      objectUrl = URL.createObjectURL(file);
      preview.src = objectUrl;
      preview.hidden = false;
      if (placeholder) placeholder.hidden = true;
    });
  });
})();
