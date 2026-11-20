// Size -> many Colors -> one editable variant row per checked pair.
// File inputs are never recreated when another checkbox changes.
(() => {
  'use strict';
  const form = document.getElementById('productForm');
  if (!form) return;
  const container = document.getElementById('variantRows');
  const empty = document.getElementById('variantEmpty');
  const count = document.getElementById('variantCount');
  const basePrice = document.getElementById('BasePrice');
  let seed = [];
  try { seed = JSON.parse(document.getElementById('variantSeed').textContent || '[]'); }
  catch { seed = []; }

  const keyOf = (size, color) => `${size}:${color}`;
  const selectedRows = new Map();

  function input(type, field, value, attrs = {}) {
    const el = document.createElement('input');
    el.type = type;
    el.dataset.variantField = field;
    if (value !== undefined && value !== null) el.value = value;
    for (const [name, attrValue] of Object.entries(attrs)) el.setAttribute(name, attrValue);
    return el;
  }

  function field(title, el) {
    const wrap = document.createElement('label');
    wrap.className = 'px-field';
    const label = document.createElement('span');
    label.textContent = title;
    wrap.append(label, el);
    return wrap;
  }

  function makeRow(size, color, sizeName, colorName, existing) {
    const row = document.createElement('div');
    row.className = 'px-variant-card';
    row.dataset.key = keyOf(size, color);

    const head = document.createElement('div');
    head.className = 'px-variant-title';
    const tag = document.createElement('strong');
    tag.textContent = `${sizeName} / ${colorName}`;
    const hint = document.createElement('span');
    hint.textContent = 'Individual stock + optional photo';
    head.append(tag, hint);
    row.append(head);

    row.append(
      input('hidden', 'Id', existing?.Id || 0),
      input('hidden', 'SizeId', size),
      input('hidden', 'ColorId', color)
    );
    const columns = document.createElement('div');
    columns.className = 'px-variant-fields';
    const sku = input('text', 'SKU', existing?.SKU || '', { maxlength: '100', placeholder: 'Auto-generated if left blank' });
    const price = input('number', 'Price', existing ? existing.Price : (basePrice?.value || 0), { min: '0', step: '0.01', required: 'required' });
    const stock = input('number', 'StockQuantity', existing ? existing.StockQuantity : 0, { min: '0', step: '1', required: 'required' });
    const photo = input('file', 'ImageFile', undefined, { accept: '.jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp' });
    columns.append(field('Variant SKU', sku), field('Price (৳)', price), field('Stock units', stock), field('Optional image', photo));
    row.append(columns);

    if (existing?.ExistingImageUrl) {
      const imageOptions = document.createElement('div');
      imageOptions.className = 'px-variant-image';
      const img = document.createElement('img');
      img.src = existing.ExistingImageUrl;
      img.alt = `${sizeName} / ${colorName}`;
      const remove = input('checkbox', 'RemoveImage', 'true');
      remove.checked = Boolean(existing.RemoveImage);
      const removeLabel = document.createElement('label');
      removeLabel.append(remove, document.createTextNode(' Remove existing image (or upload a replacement)'));
      imageOptions.append(img, removeLabel);
      row.append(imageOptions);
    }
    return row;
  }

  function reindex() {
    [...container.children].forEach((row, i) => {
      row.querySelectorAll('[data-variant-field]').forEach(control => {
        control.name = `Variants[${i}].${control.dataset.variantField}`;
      });
    });
    const length = container.children.length;
    count.textContent = `${length} combination${length === 1 ? '' : 's'}`;
    empty.hidden = length !== 0;
  }

  function activateColor(checkbox, existing) {
    const sizeId = Number(checkbox.dataset.size);
    const colorId = Number(checkbox.dataset.color);
    const key = keyOf(sizeId, colorId);
    if (checkbox.checked) {
      if (!selectedRows.has(key)) {
        const row = makeRow(sizeId, colorId, checkbox.dataset.sizeName, checkbox.dataset.colorName, existing);
        container.appendChild(row);
        selectedRows.set(key, row);
      }
    } else {
      selectedRows.get(key)?.remove();
      selectedRows.delete(key);
    }
    reindex();
  }

  function activateSize(checkbox) {
    const size = checkbox.value;
    const colors = document.querySelector(`[data-color-list="${size}"]`);
    if (!colors) return;
    colors.hidden = !checkbox.checked;
    if (!checkbox.checked) {
      colors.querySelectorAll('.js-color').forEach(item => {
        if (item.checked) { item.checked = false; activateColor(item); }
      });
    }
  }

  document.querySelectorAll('.js-size').forEach(checkbox => {
    checkbox.addEventListener('change', () => activateSize(checkbox));
  });
  document.querySelectorAll('.js-color').forEach(checkbox => {
    checkbox.addEventListener('change', () => activateColor(checkbox));
  });

  // Restore all existing combinations during Edit, including after server-side validation errors.
  seed.forEach(existing => {
    const sizeToggle = document.querySelector(`.js-size[value="${Number(existing.SizeId)}"]`);
    const colorToggle = document.querySelector(`.js-color[data-size="${Number(existing.SizeId)}"][data-color="${Number(existing.ColorId)}"]`);
    if (!sizeToggle || !colorToggle) return;
    sizeToggle.checked = true;
    activateSize(sizeToggle);
    colorToggle.checked = true;
    activateColor(colorToggle, existing);
  });
  reindex();

  form.addEventListener('submit', e => {
    reindex();
    if (!selectedRows.size) {
      e.preventDefault();
      document.getElementById('sizeColorSection')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
      alert('Please select at least one size and one color.');
    }
  });
})();
