(() => {
  if (window.__lumaShellHooks) return;
  window.__lumaShellHooks = true;
  const pane = '__PANE__';
  const sendFocus = () => chrome.webview.postMessage({kind:'luma-pane-focus', pane});
  document.addEventListener('pointerdown', sendFocus, true);
  document.addEventListener('focusin', sendFocus, true);
  document.addEventListener('contextmenu', event => {
    const target = event.target;
    const closest = selector => target && target.closest ? target.closest(selector) : null;
    const link = closest('a[href],[data-url]'), image = closest('img'), video = closest('video,audio'), editable = closest('input,textarea,select,[contenteditable=true]');
    const selected = String(window.getSelection ? window.getSelection() : '').trim();
    window.__lumaContextTarget = video || editable || target;
    let mode = 'page';
    if (editable) mode = 'editable'; else if (selected) mode = 'selection'; else if (video) mode = 'video'; else if (image) mode = 'image'; else if (link) mode = 'link';
    event.preventDefault();
    const linkUrl = link ? (link.href || (link.dataset && link.dataset.url) || '') : '';
    const imageUrl = image ? ((image.dataset && image.dataset.fullImage) || image.currentSrc || image.src || '') : '';
    chrome.webview.postMessage({kind:'luma-context', pane, mode, link:linkUrl, image:imageUrl});
  }, true);
})()
