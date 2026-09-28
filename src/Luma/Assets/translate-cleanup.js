(() => {
  if (window.__lumaTranslateShieldInstalled) return;
  window.__lumaTranslateShieldInstalled = true;

  const bannerSelector = [
    'iframe.goog-te-banner-frame',
    '.goog-te-banner-frame',
    'iframe.skiptranslate',
    'body > .skiptranslate',
    'iframe.VIpgJd-ZVi9od-ORHb-OEVmcd',
    '.VIpgJd-ZVi9od-ORHb-OEVmcd',
    '.VIpgJd-ZVi9od-aZ2wEe-wOHMyf',
    '#goog-gt-tt',
    '.goog-te-balloon-frame'
  ].join(',');

  const css = `${bannerSelector}{display:none!important;visibility:hidden!important;opacity:0!important;height:0!important;min-height:0!important;max-height:0!important;border:0!important;pointer-events:none!important}html,body{top:0!important;margin-top:0!important;transform:none!important}.goog-text-highlight{background:inherit!important;box-shadow:none!important}`;

  const shield = node => {
    if (!(node instanceof Element)) return node;
    const cls = String(node.className || '');
    const src = node.tagName === 'IFRAME' ? String(node.getAttribute('src') || '') : '';
    if (node.matches?.(bannerSelector) || (node.tagName === 'IFRAME' && (/skiptranslate|VIpgJd-ZVi9od-ORHb/.test(cls) || /translate.*(banner|element)/i.test(src)))) {
      node.style.setProperty('display','none','important');
      node.style.setProperty('visibility','hidden','important');
      node.style.setProperty('height','0','important');
      node.style.setProperty('opacity','0','important');
    }
    return node;
  };

  const nativeAppendChild = Node.prototype.appendChild;
  Node.prototype.appendChild = function(node) { shield(node); return nativeAppendChild.call(this,node); };
  const nativeInsertBefore = Node.prototype.insertBefore;
  Node.prototype.insertBefore = function(node,ref) { shield(node); return nativeInsertBefore.call(this,node,ref); };
  const nativeAppend = Element.prototype.append;
  Element.prototype.append = function(...nodes) { nodes.forEach(shield); return nativeAppend.apply(this,nodes); };
  const nativePrepend = Element.prototype.prepend;
  Element.prototype.prepend = function(...nodes) { nodes.forEach(shield); return nativePrepend.apply(this,nodes); };

  const clean = () => {
    const root = document.documentElement;
    if (!root) return;
    let style = document.getElementById('luma_translate_cleanup');
    if (!style) {
      style = document.createElement('style');
      style.id = 'luma_translate_cleanup';
      style.textContent = css;
      nativeAppendChild.call(document.head || root, style);
    }
    root.style.setProperty('top','0px','important');
    root.style.setProperty('margin-top','0px','important');
    if (document.body) {
      document.body.style.setProperty('top','0px','important');
      document.body.style.setProperty('margin-top','0px','important');
    }
    document.querySelectorAll(bannerSelector).forEach(element => { shield(element); element.remove(); });
  };

  const start = () => { clean(); const root=document.documentElement; if(root)new MutationObserver(clean).observe(root,{childList:true,subtree:true}); };
  if (document.documentElement) start(); else document.addEventListener('DOMContentLoaded',start,{once:true});
  setInterval(clean,80);
})()
