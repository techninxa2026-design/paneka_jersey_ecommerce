/* PANENKA product form only. Deliberately never manipulates sidebar or topbar. */
(() => {
  'use strict';
  const form = document.getElementById('ppProductForm');
  if (!form) return;
  const parse = id => { try { return JSON.parse(document.getElementById(id)?.textContent || '[]'); } catch { return []; } };
  const sizes = parse('ppSizeData');
  const colors = parse('ppColorData');
  const initial = parse('ppVariantData');
  const variants = new Map(initial.map(x => [`${x.sizeId}:${x.colorId}`, x]));
  const sizePanel = document.getElementById('ppSizeChoices');
  const sizeGroups = document.getElementById('ppSizeGroups');
  const note = document.getElementById('ppVariantMessage');
  const variantFields = document.getElementById('ppVariantFormFields');
  const gallery = document.getElementById('ppGalleryInput');
  const galleryPreview = document.getElementById('ppGalleryPreview');
  const selected = new Set(initial.map(x => Number(x.sizeId)));
  const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
  const key = (s, c) => `${s}:${c}`;
  const escapeSelector = value => String(value).replace(/[^\d]/g, '');

  function makeSizeChoices() {
    sizePanel.innerHTML = sizes.length ? sizes.map(s => `<label class="pp-choice"><input type="checkbox" data-size="${s.id}" ${selected.has(s.id) ? 'checked' : ''}><span>${escapeHtml(s.name)}</span></label>`).join('')
      : '<span class="pp-hint">Create sizes in Catalog → Sizes & Colors first.</span>';
    sizePanel.addEventListener('change', e => {
      const input = e.target.closest('input[data-size]');
      if (!input) return;
      const id = Number(input.dataset.size);
      if (input.checked) selected.add(id);
      else {
        selected.delete(id);
        [...variants.keys()].filter(k => Number(k.split(':')[0]) === id).forEach(k => variants.delete(k));
      }
      syncGroups();
    });
  }

  function createGroup(size) {
    const group = document.createElement('section');
    group.className = 'pp-size-group';
    group.dataset.sizeGroup = size.id;
    group.innerHTML = `<div class="pp-size-head"><div><span class="pp-size-badge">${escapeHtml(size.name)}</span><h3>Choose colorways</h3></div><span class="pp-hint">Every color creates a separate stock entry</span></div>
      <div class="pp-choice-list pp-color-choices">${colors.map(c => `<label class="pp-choice pp-choice-color"><input type="checkbox" data-color="${c.id}" ${variants.has(key(size.id, c.id)) ? 'checked' : ''}><span>${escapeHtml(c.name)}</span></label>`).join('') || '<span class="pp-hint">Add colors in Sizes & Colors first.</span>'}</div><div class="pp-variant-rows"></div>`;
    group.addEventListener('change', e => {
      const colorCheckbox = e.target.closest('input[data-color]');
      if (!colorCheckbox) return;
      const colorId = Number(colorCheckbox.dataset.color);
      const k = key(size.id, colorId);
      if (colorCheckbox.checked) {
        variants.set(k, { sizeId: size.id, colorId, stockQuantity: 0, price: null, sku: '', isActive: true, existingImageUrl: null });
        mountRow(group, size, colors.find(x => x.id === colorId));
      } else {
        variants.delete(k);
        group.querySelector(`.pp-variant-row[data-variant="${k}"]`)?.remove();
      }
      updateNote();
    });
    return group;
  }

  function syncGroups() {
    [...sizeGroups.querySelectorAll('.pp-size-group')].forEach(group => {
      if (!selected.has(Number(group.dataset.sizeGroup))) group.remove();
    });
    sizes.filter(s => selected.has(s.id)).forEach(size => {
      if (sizeGroups.querySelector(`[data-size-group="${size.id}"]`)) return;
      const group = createGroup(size);
      sizeGroups.append(group);
      colors.filter(c => variants.has(key(size.id, c.id))).forEach(color => mountRow(group, size, color));
    });
    updateNote();
  }

  function mountRow(group, size, color) {
    if (!color) return;
    const k = key(size.id, color.id);
    const data = variants.get(k);
    const row = document.createElement('div');
    row.className = 'pp-variant-row'; row.dataset.variant = k;
    row.innerHTML = `<div class="pp-variant-row-title"><strong>${escapeHtml(size.name)} <span>/</span> ${escapeHtml(color.name)}</strong><label class="pp-active-check"><input type="checkbox" data-field="active" ${data.isActive ? 'checked' : ''}> Active</label></div>
      <div class="pp-variant-inputs"><label>Stock units<input data-field="stock" type="number" min="0" step="1" value="${Math.max(0, Number(data.stockQuantity) || 0)}" required></label>
      <label>Variant SKU<input data-field="sku" maxlength="100" placeholder="Auto if empty" value="${escapeHtml(data.sku ?? '')}"></label>
      <label>Override price (৳)<input data-field="price" type="number" min="0" step="0.01" placeholder="Use base price" value="${data.price ?? ''}"></label></div>
      <div class="pp-variant-media"><label class="pp-photo-input"><i class="bi bi-image"></i> Colorway image (optional)<input data-field="image" type="file" accept="image/jpeg,image/png,image/webp"></label>
      ${data.existingImageUrl ? `<div class="pp-variant-existing"><img src="${escapeHtml(data.existingImageUrl)}" alt="Current variant image"><label><input data-field="removeImage" type="checkbox" ${data.removeImage ? 'checked' : ''}> Remove existing image</label></div>` : ''}
      <span class="pp-file-label" data-filename></span></div>`;
    group.querySelector('.pp-variant-rows').append(row);
    row.querySelector('[data-field="image"]').addEventListener('change', e => {
      const file = e.target.files?.[0];
      row.querySelector('[data-filename]').textContent = file ? file.name : '';
      if (file && file.size > 5 * 1024 * 1024) { alert('Variant image must be 5 MB or smaller.'); e.target.value = ''; }
    });
  }

  function updateNote() {
    note.textContent = `${variants.size} configured size/color combination${variants.size === 1 ? '' : 's'}. ${!selected.size ? 'Select a size to begin.' : !variants.size ? 'Now choose one or more colors for the selected sizes.' : 'You can edit inventory and upload a different image for each.'}`;
  }

  function assignFields() {
    variantFields.innerHTML = '';
    let index = 0;
    sizeGroups.querySelectorAll('.pp-variant-row').forEach(row => {
      const [sizeId, colorId] = row.dataset.variant.split(':');
      const name = suffix => `Variants[${index}].${suffix}`;
      const hidden = (field, value) => {
        const node = document.createElement('input');
        node.type = 'hidden'; node.name = name(field); node.value = value;
        variantFields.append(node);
      };
      hidden('SizeId', sizeId); hidden('ColorId', colorId);
      const stock = row.querySelector('[data-field="stock"]');
      const sku = row.querySelector('[data-field="sku"]');
      const price = row.querySelector('[data-field="price"]');
      const active = row.querySelector('[data-field="active"]');
      hidden('StockQuantity', stock.value);
      hidden('SKU', sku.value);
      if (price.value !== '') hidden('Price', price.value);
      hidden('IsActive', active.checked ? 'true' : 'false');
      const remove = row.querySelector('[data-field="removeImage"]');
      if (remove?.checked) hidden('RemoveImage', 'true');
      const file = row.querySelector('[data-field="image"]');
      file.name = name('Image');
      index++;
    });
  }

  gallery?.addEventListener('change', () => {
    galleryPreview.innerHTML = '';
    const files = [...(gallery.files || [])];
    if (files.length > 10) { alert('Select no more than 10 gallery images.'); gallery.value = ''; return; }
    for (const file of files) {
      if (file.size > 5 * 1024 * 1024) { alert(`${file.name} exceeds the 5 MB limit.`); gallery.value = ''; galleryPreview.innerHTML = ''; return; }
      const picture = document.createElement('div'); picture.className = 'pp-preview-photo';
      const img = document.createElement('img'); img.src = URL.createObjectURL(file);
      img.onload = () => URL.revokeObjectURL(img.src); img.alt = file.name;
      const label = document.createElement('small'); label.textContent = file.name;
      picture.append(img, label); galleryPreview.append(picture);
    }
  });

  form.addEventListener('submit', e => {
    assignFields();
    if (form.querySelector('[name="IsPublished"]:checked') && variants.size === 0) {
      e.preventDefault();
      alert('Please choose at least one size/color combination before publishing.');
      document.getElementById('ppVariantCard')?.scrollIntoView({ behavior: 'smooth' });
      return;
    }
    const button = form.querySelector('[type="submit"]');
    if (button && form.checkValidity()) {
      button.disabled = true;
      button.innerHTML = '<i class="bi bi-hourglass-split"></i> Saving...';
    }
  });

  makeSizeChoices(); syncGroups();
})();
