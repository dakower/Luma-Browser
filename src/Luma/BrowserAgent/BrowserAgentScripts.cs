namespace Luma.BrowserAgent;

/// <summary>
/// Page-side helpers for the universal browser agent. Every script returns a JSON string so the
/// C# side can parse results uniformly. Interactive elements found by <see cref="ObserveScript"/>
/// are kept in <c>window.__lumaAgentEls</c>; later actions refer to them by index.
/// </summary>
public static class BrowserAgentScripts
{
    /// <summary>
    /// Snapshot of what a human would see right now: indexed interactive elements inside the
    /// viewport (shadow DOM included, elements hidden behind modals skipped), the visible text,
    /// scroll position and focus.
    /// </summary>
    public const string ObserveScript = """
    (() => {
      try {
        const vw = innerWidth, vh = innerHeight;
        const SEL = 'a[href],button,input:not([type=hidden]),textarea,select,summary,label,[role=button],[role=link],[role=tab],[role=menuitem],[role=menuitemcheckbox],[role=menuitemradio],[role=option],[role=checkbox],[role=radio],[role=switch],[role=textbox],[role=combobox],[role=searchbox],[role=treeitem],[contenteditable=""],[contenteditable="true"],[onclick],[tabindex]:not([tabindex="-1"]),.answer,.option,.choice,[data-answer],[data-option],[data-choice]';
        const found = [];
        const seen = new Set();
        const collect = root => {
          root.querySelectorAll(SEL).forEach(e => { if (!seen.has(e)) { seen.add(e); found.push(e); } });
          root.querySelectorAll('*').forEach(e => { if (e.shadowRoot) collect(e.shadowRoot); });
        };
        collect(document);
        const clean = s => (s || '').replace(/\s+/g, ' ').trim();
        const up = n => n ? (n.parentNode || n.host || null) : null;
        const containsDeep = (a, b) => { let n = b; while (n) { if (n === a) return true; n = up(n); } return false; };
        const topAt = (x, y) => {
          let t = document.elementFromPoint(x, y);
          while (t && t.shadowRoot) { const inner = t.shadowRoot.elementFromPoint(x, y); if (!inner || inner === t) break; t = inner; }
          return t;
        };
        const labelOf = e => {
          const tag = e.tagName.toLowerCase();
          let t = clean(e.getAttribute('aria-label'));
          if (!t && (tag === 'input' || tag === 'textarea' || tag === 'select')) {
            t = clean(e.getAttribute('placeholder')) || clean(e.labels && e.labels[0] && e.labels[0].innerText) || clean(e.getAttribute('title')) || clean(e.getAttribute('name'));
            if (!t && (e.type === 'checkbox' || e.type === 'radio')) {
              const p = e.closest('label, li, .answer, .option, tr, div');
              if (p) t = clean(p.innerText);
            }
          }
          if (!t) t = clean(e.innerText || e.textContent);
          if (!t) t = clean(e.getAttribute('title')) || clean(e.getAttribute('alt')) || clean(e.getAttribute('data-tooltip'));
          if (!t && e.querySelector) {
            const img = e.querySelector('img[alt],[aria-label],[title]');
            if (img) t = clean(img.getAttribute('alt') || img.getAttribute('aria-label') || img.getAttribute('title'));
          }
          return t.slice(0, 110);
        };
        const kindOf = e => {
          const tag = e.tagName.toLowerCase(), role = e.getAttribute('role');
          if (tag === 'input') return 'input[' + (e.type || 'text') + ']';
          if (tag === 'textarea') return 'textarea';
          if (tag === 'select') return 'select';
          if (e.isContentEditable) return 'editable';
          if (tag === 'a') return 'link';
          if (tag === 'button') return 'button';
          if (tag === 'label' || e.classList.contains('answer') || e.classList.contains('option')) {
            const inp = e.querySelector('input');
            return inp ? 'option[' + (inp.type || 'choice') + ']' : 'option';
          }
          if (role) return role;
          return tag;
        };
        const included = new Set();
        const els = [], lines = [];
        const iframes = Array.from(document.querySelectorAll('iframe')).filter(f => f.offsetWidth > 60 && f.offsetHeight > 60);
        if (iframes.length > 0) lines.push('[инфо] На странице есть встроенные фреймы/виджеты (iframe: ' + iframes.length + ')');
        for (const e of found) {
          if (els.length >= 240) break;
          const r = e.getBoundingClientRect();
          if (r.width < 2 || r.height < 2 || r.bottom < 0 || r.top > vh || r.right < 0 || r.left > vw) continue;
          const st = getComputedStyle(e);
          if (st.visibility === 'hidden' || st.display === 'none') continue;
          if (parseFloat(st.opacity) < 0.05 && tag !== 'input' && !e.querySelector('input')) continue;
          const cx = Math.min(vw - 1, Math.max(0, r.left + Math.min(r.width / 2, 40))), cy = Math.min(vh - 1, Math.max(0, r.top + r.height / 2));
          const top = topAt(cx, cy);
          if (top && !containsDeep(e, top) && !containsDeep(top, e)) continue;
          const tag = e.tagName.toLowerCase();
          const isField = tag === 'input' || tag === 'textarea' || tag === 'select' || e.isContentEditable;
          let parentIncluded = false, n = up(e), depth = 0;
          while (n && depth < 6) { if (included.has(n)) { parentIncluded = true; break; } n = up(n); depth++; }
          const label = labelOf(e);
          if (parentIncluded && !isField) continue;
          if (!label && !isField && tag !== 'button' && e.getAttribute('role') !== 'button') continue;
          included.add(e);
          const id = els.length;
          els.push(e);
          let line = '[' + id + '] ' + kindOf(e) + ' "' + (label || 'без подписи') + '"';
          const innerInp = (tag === 'input') ? e : e.querySelector('input[type=checkbox],input[type=radio]');
          if (innerInp && (innerInp.type === 'checkbox' || innerInp.type === 'radio')) {
            line += innerInp.checked ? ' (отмечено)' : ' (не отмечено)';
            const fs = e.closest('fieldset, .question, [data-question], .test-question, .quiz-question, form, li');
            if (fs) {
              const leg = fs.querySelector('legend, h2, h3, h4, .question-text, .q-title, .title');
              if (leg) line += ' [контекст вопроса: "' + clean(leg.innerText).slice(0, 80) + '"]';
            }
          } else if (tag === 'input' || tag === 'textarea') {
            if (e.value) line += ' value="' + clean(e.value).slice(0, 60) + '"';
          } else if (tag === 'select') {
            const opts = Array.from(e.options || []).slice(0, 12).map(o => clean(o.text)).join(' / ');
            line += ' выбрано="' + clean(e.options[e.selectedIndex] ? e.options[e.selectedIndex].text : '') + '" варианты: ' + opts;
          } else if (e.isContentEditable) {
            const v = clean(e.innerText);
            if (v) line += ' содержит="' + v.slice(0, 60) + '"';
          }
          const ariaChecked = e.getAttribute('aria-checked') || e.getAttribute('aria-pressed') || e.getAttribute('aria-selected');
          if (ariaChecked === 'true') line += ' (активно)';
          if (tag === 'a' && e.href) {
            try {
              const u = new URL(e.href);
              const path = (u.host !== location.host ? u.host : '') + u.pathname + (u.search.length < 60 ? u.search : '');
              if (path && path !== '/') line += ' → ' + path.slice(0, 90);
            } catch (_) {}
          }
          if (document.activeElement === e) line += ' [в фокусе]';
          lines.push(line);
        }
        window.__lumaAgentEls = els;

        const texts = [];
        let total = 0, last = '';
        if (document.body) {
          const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, {
            acceptNode: node => {
              const p = node.parentElement;
              if (!p || !node.nodeValue || !node.nodeValue.trim()) return NodeFilter.FILTER_REJECT;
              const tg = p.tagName;
              if (tg === 'SCRIPT' || tg === 'STYLE' || tg === 'NOSCRIPT' || tg === 'TEMPLATE') return NodeFilter.FILTER_REJECT;
              return NodeFilter.FILTER_ACCEPT;
            }
          });
          let node;
          while ((node = walker.nextNode()) && total < 6000) {
            const r = node.parentElement.getBoundingClientRect();
            if (r.width === 0 || r.height === 0 || r.bottom < 0 || r.top > vh) continue;
            const s = clean(node.nodeValue);
            if (!s || s === last) continue;
            last = s;
            texts.push(s);
            total += s.length + 1;
          }
        }
        const se = document.scrollingElement || document.documentElement;
        const maxScroll = Math.max(0, (se ? se.scrollHeight : 0) - vh);
        return JSON.stringify({
          title: document.title || '',
          url: location.href,
          scrollY: Math.round(scrollY),
          maxScroll: Math.round(maxScroll),
          vw, vh,
          dialog: !!document.querySelector('[role=dialog]:not([aria-hidden=true]),dialog[open]'),
          elements: lines,
          text: texts.join('\n')
        });
      } catch (err) {
        return JSON.stringify({ title: document.title || '', url: location.href, elements: [], text: '', error: String(err) });
      }
    })()
    """;

    /// <summary>Scrolls element N to the centre and returns its centre point for a trusted click.</summary>
    public static string PointScript(int id) => $$"""
    (() => {
      const e = (window.__lumaAgentEls || [])[{{id}}];
      if (!e || !e.isConnected) return JSON.stringify({ ok: false, reason: 'stale' });
      try { e.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' }); } catch (_) { e.scrollIntoView(); }
      const r = e.getBoundingClientRect();
      e.style.outline = '2px solid #7468c7'; e.style.outlineOffset = '2px';
      setTimeout(() => { try { e.style.outline = ''; e.style.outlineOffset = ''; } catch (_) {} }, 900);
      return JSON.stringify({ ok: true, x: r.left + Math.min(r.width / 2, 40), y: r.top + r.height / 2, tag: e.tagName.toLowerCase() });
    })()
    """;

    /// <summary>Fallback synthetic click when trusted input is not available.</summary>
    public static string JsClickScript(int id) => $$"""
    (() => {
      const e = (window.__lumaAgentEls || [])[{{id}}];
      if (!e || !e.isConnected) return JSON.stringify({ ok: false, reason: 'stale' });
      ['pointerdown', 'mousedown', 'pointerup', 'mouseup'].forEach(t => e.dispatchEvent(new MouseEvent(t, { bubbles: true, cancelable: true, composed: true, view: window })));
      e.click();
      return JSON.stringify({ ok: true });
    })()
    """;

    /// <summary>Focuses field N and selects its current content so typed text replaces it.</summary>
    public static string FocusFieldScript(int id, bool clear) => $$"""
    (() => {
      const e = (window.__lumaAgentEls || [])[{{id}}];
      if (!e || !e.isConnected) return JSON.stringify({ ok: false, reason: 'stale' });
      try { e.scrollIntoView({ block: 'center', behavior: 'instant' }); } catch (_) {}
      const tag = e.tagName.toLowerCase();
      if (tag === 'select') return JSON.stringify({ ok: true, select: true });
      e.focus();
      if ({{(clear ? "true" : "false")}}) {
        if (tag === 'input' || tag === 'textarea') { try { e.select(); } catch (_) {} }
        else if (e.isContentEditable) {
          const range = document.createRange(); range.selectNodeContents(e);
          const sel = getSelection(); sel.removeAllRanges(); sel.addRange(range);
        }
      }
      const r = e.getBoundingClientRect();
      return JSON.stringify({ ok: true, x: r.left + Math.min(r.width / 2, 40), y: r.top + r.height / 2, focused: document.activeElement === e || e.contains(document.activeElement) });
    })()
    """;

    /// <summary>Picks an option of a native select by visible text or value.</summary>
    public static string SelectOptionScript(int id, string text) => $$"""
    (() => {
      const e = (window.__lumaAgentEls || [])[{{id}}];
      if (!e || e.tagName.toLowerCase() !== 'select') return JSON.stringify({ ok: false });
      const want = {{System.Text.Json.JsonSerializer.Serialize(text)}}.toLowerCase().trim();
      const opt = Array.from(e.options).find(o => o.text.toLowerCase().trim() === want || o.value.toLowerCase() === want)
               || Array.from(e.options).find(o => o.text.toLowerCase().includes(want));
      if (!opt) return JSON.stringify({ ok: false, reason: 'no option' });
      e.value = opt.value;
      e.dispatchEvent(new Event('input', { bubbles: true }));
      e.dispatchEvent(new Event('change', { bubbles: true }));
      return JSON.stringify({ ok: true, chosen: opt.text });
    })()
    """;

    /// <summary>
    /// Searches the whole document (shadow DOM included) for text, scrolls to and highlights the
    /// first hit, and returns surrounding context of up to eight hits.
    /// </summary>
    public static string FindTextScript(string query) => $$"""
    (() => {
      const q = {{System.Text.Json.JsonSerializer.Serialize(query)}}.toLowerCase().replace(/\s+/g, ' ').trim();
      if (!q) return JSON.stringify({ count: 0, matches: [] });
      const clean = s => (s || '').replace(/\s+/g, ' ').trim();
      const hits = [];
      const scan = root => {
        const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        let n;
        while ((n = walker.nextNode())) {
          const v = (n.nodeValue || '').replace(/\s+/g, ' ').toLowerCase();
          if (v.includes(q)) hits.push(n);
        }
        root.querySelectorAll('*').forEach(e => { if (e.shadowRoot) scan(e.shadowRoot); });
      };
      if (document.body) scan(document.body);
      // Text split across several nodes: fall back to the smallest elements whose text contains it.
      if (!hits.length && document.body) {
        const all = Array.from(document.body.querySelectorAll('p,li,span,div,td,article,yt-formatted-string,h1,h2,h3,h4'));
        for (const e of all) {
          const t = clean(e.innerText).toLowerCase();
          if (t.includes(q) && !Array.from(e.children).some(c => clean(c.innerText).toLowerCase().includes(q))) { hits.push(e.firstChild || e); if (hits.length > 40) break; }
        }
      }
      const blockOf = n => {
        let e = n.nodeType === 1 ? n : n.parentElement;
        const stop = e && e.closest && e.closest('ytd-comment-thread-renderer,ytd-comment-view-model,ytd-comment-renderer,article,li,tr,[role=article],[role=listitem]');
        if (stop) return stop;
        let depth = 0;
        while (e && e.parentElement && clean(e.innerText).length < 160 && depth < 5) { e = e.parentElement; depth++; }
        return e;
      };
      const matches = [];
      const used = new Set();
      for (const h of hits) {
        const b = blockOf(h);
        if (!b || used.has(b)) continue;
        used.add(b);
        let t = clean(b.innerText);
        const i = t.toLowerCase().indexOf(q);
        if (t.length > 420) t = (i > 150 ? '…' : '') + t.slice(Math.max(0, i - 150), i + 270) + '…';
        matches.push(t);
        if (matches.length >= 8) break;
      }
      const first = hits[0] && (hits[0].nodeType === 1 ? hits[0] : hits[0].parentElement);
      if (first) {
        try { first.scrollIntoView({ block: 'center', behavior: 'instant' }); } catch (_) { first.scrollIntoView(); }
        const b = blockOf(hits[0]) || first;
        b.style.outline = '2px solid #7468c7'; b.style.outlineOffset = '3px'; b.style.borderRadius = '6px';
      }
      return JSON.stringify({ count: hits.length, matches });
    })()
    """;

    /// <summary>Full readable text of the page in 6000-character chunks.</summary>
    public static string ReadScript(int offset) => $$"""
    (() => {
      const t = ((document.body && document.body.innerText) || '').replace(/[ \t]+/g, ' ').replace(/\n{3,}/g, '\n\n');
      const start = Math.max(0, Math.min({{offset}}, t.length));
      return JSON.stringify({ total: t.length, offset: start, text: t.slice(start, start + 6000) });
    })()
    """;

    /// <summary>Plain scroll fallback; also scrolls the main inner container on app-like pages.</summary>
    public static string ScrollScript(int deltaY) => $$"""
    (() => {
      const before = scrollY;
      scrollBy(0, {{deltaY}});
      if (Math.abs(scrollY - before) < 5) {
        let e = document.elementFromPoint(innerWidth / 2, innerHeight / 2);
        while (e && e !== document.body) {
          const st = getComputedStyle(e);
          if (/(auto|scroll)/.test(st.overflowY) && e.scrollHeight > e.clientHeight + 10) { e.scrollBy(0, {{deltaY}}); break; }
          e = e.parentElement;
        }
      }
      return JSON.stringify({ ok: true });
    })()
    """;

    /// <summary>Dispatches pointer and mouse hover events over element N.</summary>
    public static string HoverScript(int id) => $$"""
    (() => {
      const e = (window.__lumaAgentEls || [])[{{id}}];
      if (!e || !e.isConnected) return JSON.stringify({ ok: false, reason: 'stale' });
      try { e.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' }); } catch (_) { e.scrollIntoView(); }
      const r = e.getBoundingClientRect();
      const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
      ['pointerenter', 'mouseenter', 'pointerover', 'mouseover', 'pointermove', 'mousemove'].forEach(t => {
        e.dispatchEvent(new MouseEvent(t, { bubbles: true, cancelable: true, composed: true, clientX: cx, clientY: cy, view: window }));
      });
      return JSON.stringify({ ok: true, x: cx, y: cy });
    })()
    """;

    /// <summary>Explicitly toggles or sets checkboxes, radio buttons, and aria-checked controls.</summary>
    public static string CheckScript(int id, bool? targetChecked) => $$"""
    (() => {
      const e = (window.__lumaAgentEls || [])[{{id}}];
      if (!e || !e.isConnected) return JSON.stringify({ ok: false, reason: 'stale' });
      try { e.scrollIntoView({ block: 'center', behavior: 'instant' }); } catch (_) {}
      const want = {{(targetChecked.HasValue ? (targetChecked.Value ? "true" : "false") : "null")}};
      const tag = e.tagName.toLowerCase();
      if (tag === 'input' && (e.type === 'checkbox' || e.type === 'radio')) {
        const next = want !== null ? want : (e.type === 'checkbox' ? !e.checked : true);
        e.checked = next;
        e.dispatchEvent(new Event('input', { bubbles: true }));
        e.dispatchEvent(new Event('change', { bubbles: true }));
        e.click();
        return JSON.stringify({ ok: true, checked: e.checked });
      }
      if (e.hasAttribute('aria-checked')) {
        const cur = e.getAttribute('aria-checked') === 'true';
        const next = want !== null ? want : !cur;
        e.setAttribute('aria-checked', next ? 'true' : 'false');
        e.click();
        return JSON.stringify({ ok: true, checked: next });
      }
      e.click();
      return JSON.stringify({ ok: true, clicked: true });
    })()
    """;

    /// <summary>Simulates mouse drag from element centre by deltaX and deltaY (sliders, drag handles).</summary>
    public static string DragScript(int id, double deltaX, double deltaY) => $$"""
    (() => {
      const e = (window.__lumaAgentEls || [])[{{id}}];
      if (!e || !e.isConnected) return JSON.stringify({ ok: false, reason: 'stale' });
      try { e.scrollIntoView({ block: 'center', behavior: 'instant' }); } catch (_) {}
      const r = e.getBoundingClientRect();
      const sx = r.left + r.width / 2, sy = r.top + r.height / 2;
      const ex = sx + {{deltaX}}, ey = sy + {{deltaY}};
      e.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true, clientX: sx, clientY: sy, buttons: 1 }));
      e.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, clientX: sx, clientY: sy, button: 0 }));
      window.dispatchEvent(new PointerEvent('pointermove', { bubbles: true, cancelable: true, clientX: ex, clientY: ey, buttons: 1 }));
      window.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, cancelable: true, clientX: ex, clientY: ey, button: 0 }));
      window.dispatchEvent(new PointerEvent('pointerup', { bubbles: true, cancelable: true, clientX: ex, clientY: ey, buttons: 0 }));
      window.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, clientX: ex, clientY: ey, button: 0 }));
      return JSON.stringify({ ok: true, sx, sy, ex, ey });
    })()
    """;

    /// <summary>Runs custom JavaScript in the page context and returns the result safely.</summary>
    public static string EvalScript(string code) => $$"""
    (() => {
      try {
        const fn = new Function({{System.Text.Json.JsonSerializer.Serialize(code)}});
        const res = fn();
        return JSON.stringify({ ok: true, value: typeof res === 'undefined' ? null : res });
      } catch (err) {
        return JSON.stringify({ ok: false, error: String(err) });
      }
    })()
    """;
}
