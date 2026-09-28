using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Luma;

public static class BrowserScripts
{
    private const string TranslationCleanupResourceName = "Luma.Assets.translate-cleanup.js";
    private const string ContextMenuResourceName = "Luma.Assets.context-menu.js";
    private static string? _translationCleanup;
    private static string? _contextMenu;
    public static string TranslationCleanup => _translationCleanup ??= ReadResource(TranslationCleanupResourceName);
    /// <summary>Presents the page with plain Chrome brands instead of the WebView2 ones.</summary>
    private const string ClientHintsTemplate = """
    (() => { try {
      const brands = [
        { brand: 'Chromium', version: '__MAJOR__' },
        { brand: 'Google Chrome', version: '__MAJOR__' },
        { brand: 'Not?A_Brand', version: '8' }
      ];
      const full = brands.map(b => ({ brand: b.brand, version: b.brand === 'Not?A_Brand' ? '8.0.0.0' : '__VERSION__' }));
      const data = {
        brands: brands,
        mobile: false,
        platform: 'Windows',
        toJSON: () => ({ brands: brands, mobile: false, platform: 'Windows' }),
        getHighEntropyValues: () => Promise.resolve({
          architecture: 'x86', bitness: '64', brands: brands, fullVersionList: full,
          mobile: false, model: '', platform: 'Windows', platformVersion: '15.0.0',
          uaFullVersion: '__VERSION__', wow64: false
        })
      };
      Object.defineProperty(Navigator.prototype, 'userAgentData', { get: () => data, configurable: true });
    } catch (e) {} })()
    """;
    public static string ClientHints(string fullVersion)
    {
        if (!Version.TryParse(fullVersion, out var version) || version.Major < 120) fullVersion = "140.0.0.0";
        var major = (Version.TryParse(fullVersion, out version) ? version.Major : 140).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return ClientHintsTemplate.Replace("__MAJOR__", major, StringComparison.Ordinal).Replace("__VERSION__", fullVersion, StringComparison.Ordinal);
    }

    /// <summary>Reader overlay in the Luma palette. Toggles itself off on a second call.</summary>
    /// <summary>Warms up same-site links the pointer rests on, so the click feels instant.</summary>
    public const string LinkPrefetch = """
    (()=>{try{const done=new Set();const add=u=>{if(!u||done.has(u)||done.size>80)return;done.add(u);try{const l=document.createElement('link');l.rel='prefetch';l.as='document';l.href=u;(document.head||document.documentElement).appendChild(l)}catch(e){}};let t=null;document.addEventListener('mouseover',e=>{const a=e.target&&e.target.closest?e.target.closest('a[href]'):null;if(!a)return;clearTimeout(t);t=setTimeout(()=>{try{const u=new URL(a.getAttribute('href'),location.href);if(u.origin===location.origin&&/^https?:$/.test(u.protocol)&&u.href!==location.href)add(u.href)}catch(x){}},200)},true);document.addEventListener('mouseout',()=>clearTimeout(t),true)}catch(e){}})()
    """;

    /// <summary>Remembers where a video was left off and adds Alt+[ / Alt+] speed control anywhere.</summary>
    public const string MediaMemory = """
    (()=>{try{
      const key=v=>'luma:v:'+location.host+location.pathname+':'+String(v.currentSrc||v.src||'').slice(-48);
      const save=v=>{try{if(v.duration>90&&v.currentTime>30&&v.currentTime<v.duration-30)localStorage.setItem(key(v),String(Math.floor(v.currentTime)))}catch(e){}};
      const restore=v=>{try{const s=parseInt(localStorage.getItem(key(v))||'0',10);if(s>20&&v.duration>s+20&&v.currentTime<5)v.currentTime=s}catch(e){}};
      const hook=v=>{if(v.__lumaMedia)return;v.__lumaMedia=1;v.addEventListener('loadedmetadata',()=>restore(v));v.addEventListener('pause',()=>save(v));if(v.readyState>0)restore(v);setInterval(()=>{if(!v.paused)save(v)},5000)};
      const scan=()=>{try{document.querySelectorAll('video').forEach(hook)}catch(e){}};
      document.addEventListener('DOMContentLoaded',scan);setInterval(scan,2500);scan();
      document.addEventListener('keydown',e=>{if(!e.altKey||e.ctrlKey||e.metaKey)return;const v=document.querySelector('video');if(!v)return;if(e.code==='BracketRight'){v.playbackRate=Math.min(4,Math.round((v.playbackRate+.25)*100)/100);e.preventDefault()}else if(e.code==='BracketLeft'){v.playbackRate=Math.max(.25,Math.round((v.playbackRate-.25)*100)/100);e.preventDefault()}},true)
    }catch(e){}})()
    """;

    /// <summary>Dark palette for sites that never shipped one.</summary>
    /// <summary>Turns the dark palette on: counter-inverts real media (img/video/canvas/svg/
    /// iframe) and anything with a CSS background-image — not just inline styles, which is all
    /// the previous version caught, so class-based hero/card backgrounds used to stay inverted
    /// too. A MutationObserver keeps marking new elements as the page changes (SPA-friendly).</summary>
    private const string DarkApplyBody = """
    if (!document.getElementById('__lumaDark')) {
      const s = document.createElement('style');
      s.id = '__lumaDark';
      s.textContent = 'html{filter:invert(1) hue-rotate(180deg) contrast(.92) !important;background:#0B0912 !important}' +
        'img,video,canvas,svg,picture,iframe,embed,object,[style*="background-image"],.__lumaDarkBg{filter:invert(1) hue-rotate(180deg) !important}';
      (document.head || document.documentElement).appendChild(s);
      const mark = (root) => {
        try {
          if (!root.querySelectorAll) return;
          root.querySelectorAll('*').forEach((el) => {
            if (el.classList.contains('__lumaDarkBg')) return;
            const bg = getComputedStyle(el).backgroundImage;
            if (bg && bg !== 'none') el.classList.add('__lumaDarkBg');
          });
        } catch (e) {}
      };
      mark(document.documentElement);
      if (window.__lumaDarkObserver) { try { window.__lumaDarkObserver.disconnect(); } catch (e) {} }
      const obs = new MutationObserver((muts) => {
        muts.forEach((m) => {
          if (m.addedNodes) m.addedNodes.forEach((n) => { if (n.nodeType === 1) mark(n); });
          if (m.type === 'attributes' && m.target.nodeType === 1) mark(m.target);
        });
      });
      obs.observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'class'] });
      window.__lumaDarkObserver = obs;
    }
    """;

    public const string DarkOn = "(() => { try { " + DarkApplyBody + " return 'on'; } catch (e) { return 'off'; } })()";

    public const string DarkOff = """
    (() => { try {
      const s = document.getElementById('__lumaDark'); if (s) s.remove();
      if (window.__lumaDarkObserver) { try { window.__lumaDarkObserver.disconnect(); } catch (e) {} window.__lumaDarkObserver = null; }
      document.querySelectorAll('.__lumaDarkBg').forEach((el) => el.classList.remove('__lumaDarkBg'));
      return 'off';
    } catch (e) { return 'off'; } })()
    """;

    /// <summary>Applies the dark palette immediately, before first paint, on domains already
    /// marked dark from a previous session — avoids a flash of the normal light page while the
    /// slower NavigationCompleted fallback (ApplyDomainPrefsAsync) is still catching up.</summary>
    public static string DarkAuto(IEnumerable<string> domains)
    {
        var json = JsonSerializer.Serialize(domains.ToArray());
        return "(() => { try { const __lumaDarkDomains = " + json + "; const __lumaDarkHost = location.hostname.replace(/^www\\./, ''); if (!__lumaDarkDomains.includes(__lumaDarkHost)) return; "
            + DarkApplyBody + " } catch (e) {} })()";
    }

    /// <summary>Reports whatever &lt;audio&gt;/&lt;video&gt; element is currently playing on the
    /// page (title/artist from the Media Session API when the site sets it, otherwise the tab
    /// title) so the titlebar now-playing widget can show and control it. Play/pause/seek/volume
    /// act on that same element directly, which works well on plain HTML5 players; "next"/
    /// "previous" fall back to clicking whatever button on the page looks like a skip control,
    /// which is best-effort and not guaranteed on every site.
    /// <para>Elements are found two ways on purpose: (1) patching
    /// <c>HTMLMediaElement.prototype.play</c> catches an element the instant the page calls
    /// <c>.play()</c> on it, no matter where it lives in the DOM — including inside an open OR
    /// closed shadow root, which a plain <c>document.querySelectorAll</c> or a
    /// <c>document</c>-level event listener cannot see (player widgets built as web components,
    /// like SoundCloud's, keep their &lt;audio&gt; there, and its play/pause events don't cross
    /// the shadow boundary since they aren't "composed"). (2) a periodic scan that also recurses
    /// into open shadow roots catches elements that started via the <c>autoplay</c> attribute,
    /// which bypasses the patched .play().</para></summary>
    public const string MediaWatch = """
    (() => { try {
      if (window.__lumaMediaWatch) return;
      window.__lumaMediaWatch = true;
      const post = (msg) => { try { window.chrome.webview.postMessage(msg); } catch (e) {} };

      function absolute(src) { try { return src ? new URL(src, location.href).href : ''; } catch (e) { return ''; } }
      function pageArtwork() {
        try {
          const url = new URL(location.href); const pathVideo = location.pathname.match(/\/(?:shorts|embed)\/([^/?]+)/); const yt = location.hostname.includes('youtube.com') ? (url.searchParams.get('v') || (pathVideo && pathVideo[1]) || '') : (location.hostname.includes('youtu.be') ? location.pathname.split('/').filter(Boolean)[0] : '');
          if (yt) return 'https://i.ytimg.com/vi/' + encodeURIComponent(yt) + '/hqdefault.jpg';
          const image = document.querySelector('meta[property="og:image"],meta[name="twitter:image"],meta[itemprop="image"]');
          if (image) return absolute(image.content);
          const icon = document.querySelector('link[rel="apple-touch-icon"],link[rel="icon"]');
          return icon ? absolute(icon.href) : '';
        } catch (e) { return ''; }
      }
      function pageArtist() {
        try {
          const meta = document.querySelector('meta[name="author"],meta[property="music:musician"],meta[name="twitter:creator"]');
          if (meta && meta.content) return meta.content;
          const channel = document.querySelector('#channel-name a,ytd-channel-name a,[itemprop="author"] [itemprop="name"]');
          return channel ? (channel.textContent || '').trim() : '';
        } catch (e) { return ''; }
      }
      function metaOf() {
        try {
          const m = navigator.mediaSession && navigator.mediaSession.metadata;
          const art = m && m.artwork && m.artwork.length ? absolute(m.artwork[m.artwork.length - 1].src) : '';
          const ogTitle = document.querySelector('meta[property="og:title"]');
          return { title: (m && m.title) || (ogTitle && ogTitle.content) || document.title || '', artist: (m && m.artist) || pageArtist(), artwork: art || pageArtwork() };
        } catch (e) { return { title: document.title || '', artist: '', artwork: pageArtwork() }; }
      }

      function siteState() {
        try {
          const host = location.hostname.replace(/^www\./, '').toLowerCase();
          const text = (selector) => (document.querySelector(selector)?.textContent || '').trim();
          const image = (selector) => absolute(document.querySelector(selector)?.currentSrc || document.querySelector(selector)?.src || '');
          const playback = navigator.mediaSession && navigator.mediaSession.playbackState;
          if (host === 'music.youtube.com') {
            const button = document.querySelector('#play-pause-button,ytmusic-player-bar tp-yt-paper-icon-button.play-pause-button');
            const label = ((button?.getAttribute('aria-label') || button?.title || '') + ' ' + (button?.getAttribute('title') || '')).toLowerCase();
            const slider = document.querySelector('#progress-bar,[role="slider"][aria-valuenow]');
            return {
              playing: playback === 'playing' || /pause|пауза|приостанов|призупин/.test(label),
              title: text('ytmusic-player-bar .title,ytmusic-player-bar .yt-formatted-string.title') || metaOf().title,
              artist: text('ytmusic-player-bar .byline,ytmusic-player-bar .subtitle') || metaOf().artist,
              artwork: image('ytmusic-player-bar #song-image img,ytmusic-player-bar img.image') || metaOf().artwork,
              position: Number(slider?.getAttribute('aria-valuenow') || 0),
              duration: Number(slider?.getAttribute('aria-valuemax') || 0),
              mediaType: 'audio'
            };
          }
          if (host === 'open.spotify.com') {
            const button = document.querySelector('[data-testid="control-button-playpause"],[aria-label="Pause"],[aria-label="Пауза"]');
            const label = ((button?.getAttribute('aria-label') || button?.title || '') + ' ' + (button?.getAttribute('data-testid') || '')).toLowerCase();
            const slider = document.querySelector('[data-testid="playback-progressbar"],[role="slider"][aria-valuenow]');
            const widget = document.querySelector('[data-testid="now-playing-widget"]') || document;
            const titleNode = widget.querySelector?.('[data-testid="context-item-info-title"],a[href*="/track/"]');
            const artistNode = widget.querySelector?.('[data-testid="context-item-info-subtitles"],a[href*="/artist/"]');
            const artNode = widget.querySelector?.('img');
            return {
              playing: playback === 'playing' || /pause|пауза|приостанов|призупин/.test(label),
              title: (titleNode?.textContent || '').trim() || metaOf().title,
              artist: (artistNode?.textContent || '').trim() || metaOf().artist,
              artwork: absolute(artNode?.currentSrc || artNode?.src || '') || metaOf().artwork,
              position: Number(slider?.getAttribute('aria-valuenow') || 0),
              duration: Number(slider?.getAttribute('aria-valuemax') || 0),
              mediaType: 'audio'
            };
          }
          return null;
        } catch (e) { return null; }
      }

      let current = null;
      let lastSent = 0;

      function report(force) {
        const el = current;
        const site = siteState();
        const now = Date.now();
        if (!el && !site) { if (force) post({ kind: 'luma-media-state', playing: false }); return; }
        if (!force && now - lastSent < 900) return;
        lastSent = now;
        const meta = metaOf();
        post({
          kind: 'luma-media-state',
          playing: site ? site.playing : !el.paused && !el.ended,
          title: site?.title || meta.title,
          artist: site?.artist || meta.artist,
          artwork: site?.artwork || meta.artwork,
          position: site?.position || el?.currentTime || 0,
          duration: site?.duration || (el && isFinite(el.duration) ? el.duration : 0),
          volume: el ? (el.muted ? 0 : (el.volume != null ? el.volume : 1)) : 1,
          mediaType: site?.mediaType || (el?.tagName || '').toLowerCase(),
        });
      }

      // Listeners go directly on the element, not on document — that way it does not
      // matter whether the element sits inside a shadow root.
      function track(el) {
        if (!el || el.tagName !== 'AUDIO' && el.tagName !== 'VIDEO') return;
        current = el;
        if (el.__lumaTracked) return;
        el.__lumaTracked = true;
        el.addEventListener('play', () => { current = el; report(true); });
        el.addEventListener('playing', () => { current = el; report(true); });
        el.addEventListener('pause', () => report(true));
        el.addEventListener('volumechange', () => report(true));
        el.addEventListener('timeupdate', () => report(false));
        el.addEventListener('emptied', () => report(true));
      }

      try {
        const proto = HTMLMediaElement.prototype;
        const origPlay = proto.play;
        proto.play = function () { track(this); return origPlay.apply(this, arguments); };
      } catch (e) {}

      function scan(root) {
        try {
          if (!root.querySelectorAll) return;
          root.querySelectorAll('audio, video').forEach(track);
          root.querySelectorAll('*').forEach((el) => { if (el.shadowRoot) scan(el.shadowRoot); });
        } catch (e) {}
      }
      scan(document);
      setInterval(() => scan(document), 2000);
      document.addEventListener('play', (e) => track(e.target), true);
      setInterval(() => report(false), 1000);

      // Same shadow-root-piercing search, generalised for the skip-button lookup below —
      // player controls on sites like SoundCloud tend to live in the same web component as
      // the <audio> element itself.
      function deepQueryAll(sel) {
        const found = [];
        (function walk(root) {
          try {
            root.querySelectorAll(sel).forEach((el) => found.push(el));
            root.querySelectorAll('*').forEach((el) => { if (el.shadowRoot) walk(el.shadowRoot); });
          } catch (e) {}
        })(document);
        return found;
      }

      function siteControl(action, value) {
        const host = location.hostname.replace(/^www\./, '').toLowerCase();
        const first = (selectors) => selectors.map((selector) => document.querySelector(selector)).find(Boolean);
        let button = null;
        if (action === 'toggle' || action === 'play' || action === 'pause') {
          button = host === 'music.youtube.com'
            ? first(['#play-pause-button','ytmusic-player-bar tp-yt-paper-icon-button.play-pause-button'])
            : first(['[data-testid="control-button-playpause"]','[aria-label="Pause"]','[aria-label="Play"]','[aria-label="Пауза"]','[aria-label="Воспроизвести"]']);
        } else if (action === 'skip') {
          const forward = Number(value) >= 0;
          button = host === 'music.youtube.com'
            ? first(forward ? ['ytmusic-player-bar .next-button','#next-button'] : ['ytmusic-player-bar .previous-button','#previous-button'])
            : first(forward ? ['[data-testid="control-button-skip-forward"]'] : ['[data-testid="control-button-skip-back"]']);
        }
        if (button) { button.click(); return true; }
        return false;
      }

      window.__lumaMediaControl = (action, value) => {
        try {
          const el = current;
          if (!el) { siteControl(action, value); setTimeout(() => report(true), 120); return; }
          if (action === 'toggle') { el.paused ? el.play().catch(() => {}) : el.pause(); }
          else if (action === 'play') el.play().catch(() => {});
          else if (action === 'pause') el.pause();
          else if (action === 'seek') { const d = el.duration; el.currentTime = isFinite(d) ? Math.max(0, Math.min(value, d)) : Math.max(0, value); }
          else if (action === 'seek-relative') { const d = el.duration; const next = (el.currentTime || 0) + Number(value || 0); el.currentTime = isFinite(d) ? Math.max(0, Math.min(next, d)) : Math.max(0, next); }
          else if (action === 'repeat') { el.loop = Number(value) > 0; }
          else if (action === 'volume') {
            // Set + explicitly unmute right away, then re-apply once more shortly after: some
            // players re-assert their own last known volume on their <audio> element right
            // after a change, which can otherwise make an external volume change look like it
            // "didn't take" (especially turning it down after having turned it up).
            const v = Math.max(0, Math.min(1, value));
            const apply = () => { try { el.volume = v; el.muted = v <= 0.001; } catch (e) {} };
            apply();
            setTimeout(apply, 60);
            setTimeout(apply, 250);
          }
          else if (action === 'skip') {
            const forward = value >= 0;
            const specific = forward
              ? ['.skipControl__next', '[data-testid="control-button-skip-forward"]', '[aria-label="Next"]', '.next-button', '#next-button']
              : ['.skipControl__previous', '[data-testid="control-button-skip-back"]', '[aria-label="Previous"]', '.previous-button', '#previous-button'];
            let btn = null;
            for (const sel of specific) { const hits = deepQueryAll(sel); if (hits.length) { btn = hits[0]; break; } }
            if (!btn) {
              const words = forward ? ['next', 'следующ', 'вперёд', 'скип', 'forward'] : ['previous', 'prev', 'предыдущ', 'назад', 'back'];
              btn = deepQueryAll('button, [role="button"], a').find((b) => {
                const label = ((b.getAttribute('aria-label') || '') + ' ' + (b.title || '') + ' ' + (b.className || '')).toLowerCase();
                return words.some((w) => label.includes(w));
              });
            }
            if (btn) btn.click();
          }
          report(true);
        } catch (e) {}
      };

      window.chrome.webview.addEventListener('message', (e) => {
        const m = e.data;
        if (!m || m.kind !== 'luma-media-control') return;
        window.__lumaMediaControl(m.action, m.value);
      });
    } catch (e) {} })()
    """;

    /// <summary>Moves the fullscreen page video into Document Picture-in-Picture when Luma
    /// loses focus. The PiP document is browser-themed and owns its Lucide controls. Standard
    /// element PiP remains a compatibility fallback for runtimes without Document PiP.</summary>
    public const string FloatingVideo = """
    (() => { try {
      if (window.__lumaFloatingVideoInstalled) return;
      window.__lumaFloatingVideoInstalled = true;
      let lastFullscreenVideo = null, restore = null, pipWindow = null;
      const findVideo = () => {
        const full = document.fullscreenElement;
        if (full) {
          if (full.tagName === 'VIDEO') return full;
          const nested = full.querySelector && full.querySelector('video');
          if (nested) return nested;
        }
        return lastFullscreenVideo || [...document.querySelectorAll('video')].find(v => !v.paused && v.readyState > 1) || null;
      };
      document.addEventListener('fullscreenchange', () => {
        const full = document.fullscreenElement;
        const video = full && (full.tagName === 'VIDEO' ? full : full.querySelector && full.querySelector('video'));
        if (video) lastFullscreenVideo = video;
      }, true);
      const svg = (path) => `<svg viewBox="0 0 24 24" aria-hidden="true"><path d="${path}"/></svg>`;
      const fmt = value => { value=Math.max(0,Number(value)||0); const h=Math.floor(value/3600),m=Math.floor(value%3600/60),s=Math.floor(value%60); return h?`${h}:${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`:`${m}:${String(s).padStart(2,'0')}`; };
      window.__lumaEnterFloatingVideo = async (palette) => {
        const video = findVideo();
        if (!video || pipWindow || document.pictureInPictureElement) return false;
        if ('documentPictureInPicture' in window) {
          try {
            const pip = await documentPictureInPicture.requestWindow({width:640,height:380});
            pipWindow = pip;
            // Never leave fullscreen until a floating window really exists. Previously Luma
            // exited first, so a denied PiP request threw the user straight back to the page.
            try { if (document.fullscreenElement) await document.exitFullscreen(); } catch(e) {}
            const parent=video.parentNode, next=video.nextSibling, oldStyle=video.getAttribute('style');
            restore=()=>{try{ if(parent){ next&&next.parentNode===parent?parent.insertBefore(video,next):parent.appendChild(video); } oldStyle===null?video.removeAttribute('style'):video.setAttribute('style',oldStyle); }catch(e){} pipWindow=null;restore=null;};
            const d=pip.document, p=palette||{};
            d.head.innerHTML=`<meta charset="utf-8"><style>*{box-sizing:border-box}html,body{margin:0;width:100%;height:100%;overflow:hidden;background:${p.inset||'#17181A'};font:13px Inter,'Segoe UI',sans-serif}.shell{position:relative;width:100%;height:100%;background:#000}.stage,.stage video{width:100%;height:100%}.stage video{display:block!important;object-fit:contain!important;max-width:none!important;max-height:none!important}.controls{position:absolute;inset:auto 10px 10px;display:grid;gap:9px;padding:10px 12px;border:1px solid ${p.border||'#45464E'};border-radius:12px;background:${p.surface||'#202123'}ee;color:white;opacity:0;transform:translateY(8px);pointer-events:none;transition:.16s}.shell.controls-visible .controls{opacity:1;transform:none;pointer-events:auto}.row{display:flex;align-items:center;justify-content:center;gap:8px}.timeline{display:grid;grid-template-columns:44px 1fr 44px;align-items:center;gap:8px;color:${p.muted||'#8B8C92'};font-size:10px}.timeline span:last-child{text-align:right}button{width:42px;height:42px;border:0;border-radius:9px;background:transparent;color:${p.secondary||'#D1D2D6'};display:grid;place-items:center;cursor:pointer}button:hover{background:${p.raised||'#27282B'};color:white}.primary{background:${p.accent||'#7468C7'};color:white}svg{width:19px;height:19px;fill:none;stroke:currentColor;stroke-width:2;stroke-linecap:round;stroke-linejoin:round}input{width:100%;accent-color:${p.accent||'#7468C7'}}@media(max-width:420px),(max-height:250px){.controls{inset:auto 6px 6px;padding:7px 8px;gap:5px}.row{gap:4px}button{width:34px;height:34px}.timeline{grid-template-columns:36px 1fr 36px;gap:5px;font-size:9px}svg{width:16px;height:16px}}</style>`;
            d.body.innerHTML='<main class="shell"><div class="stage"></div><section class="controls"><div class="row"><button data-a="back" title="Назад на 15 секунд"></button><button class="primary" data-a="toggle" title="Пауза / воспроизведение"></button><button data-a="forward" title="Вперёд на 15 секунд"></button></div><div class="timeline"><span class="pos">0:00</span><input type="range" min="0" max="1" step="0.001"><span class="left">−0:00</span></div></section></main>';
            const shell=d.querySelector('.shell'), stage=d.querySelector('.stage'), controls=d.querySelector('.controls'), range=d.querySelector('input'), pos=d.querySelector('.pos'), left=d.querySelector('.left');
            stage.appendChild(video); video.setAttribute('style','width:100%!important;height:100%!important;display:block!important;object-fit:contain!important');
            const paths={back:'M3 12a9 9 0 1 0 3-6.7 M3 3v5h5 M12 8v4l-3 2',forward:'M21 12a9 9 0 1 1-3-6.7 M21 3v5h-5 M12 8v4l3 2',play:'M6 3l14 9-14 9z',pause:'M8 5v14 M16 5v14'};
            const back=d.querySelector('[data-a=back]'), toggle=d.querySelector('[data-a=toggle]'), forward=d.querySelector('[data-a=forward]'); back.innerHTML=svg(paths.back);forward.innerHTML=svg(paths.forward);
            const sync=()=>{const duration=isFinite(video.duration)?video.duration:0;range.value=duration?String(video.currentTime/duration):'0';pos.textContent=fmt(video.currentTime);left.textContent='−'+fmt(Math.max(0,duration-video.currentTime));toggle.innerHTML=svg(video.paused?paths.play:paths.pause)};
            back.onclick=()=>video.currentTime=Math.max(0,video.currentTime-15); forward.onclick=()=>video.currentTime=Math.min(isFinite(video.duration)?video.duration:video.currentTime+15,video.currentTime+15); toggle.onclick=()=>video.paused?video.play().catch(()=>{}):video.pause(); range.oninput=()=>{if(isFinite(video.duration))video.currentTime=Number(range.value)*video.duration};
            shell.onclick=e=>{if(!controls.contains(e.target))shell.classList.toggle('controls-visible')}; shell.classList.add('controls-visible'); setTimeout(()=>shell.classList.remove('controls-visible'),2400);
            video.addEventListener('timeupdate',sync);video.addEventListener('play',sync);video.addEventListener('pause',sync);sync(); pip.addEventListener('pagehide',()=>restore&&restore(),{once:true});
            // Document PiP itself is a native window. Accept four-corner resizes (both axes
            // change), but roll back a straight side drag where only one axis changed.
            let acceptedW=pip.outerWidth,acceptedH=pip.outerHeight,resizeTimer=0,correcting=false;
            pip.addEventListener('resize',()=>{if(correcting)return;clearTimeout(resizeTimer);resizeTimer=setTimeout(()=>{const w=pip.outerWidth,h=pip.outerHeight,dw=Math.abs(w-acceptedW),dh=Math.abs(h-acceptedH);if((dw>2&&dh<=2)||(dh>2&&dw<=2)){try{correcting=true;pip.resizeTo(acceptedW,acceptedH)}catch(e){}setTimeout(()=>correcting=false,80);return}acceptedW=w;acceptedH=h},90)});
            return true;
          } catch(e) {}
        }
        try {
          await video.requestPictureInPicture();
          // Native PiP normally exits fullscreen itself; this is only a safe fallback after
          // requestPictureInPicture has definitely succeeded.
          try { if (document.fullscreenElement) await document.exitFullscreen(); } catch(e) {}
          return true;
        } catch(e) { return false; }
      };
    } catch(e) {} })()
    """;

    public const string Reader = """
    (()=>{try{
      const ID='__lumaReader';
      const restore=()=>{document.documentElement.style.overflow=window.__lumaReaderOverflow||''};
      const open=document.getElementById(ID);
      if(open){open.remove();restore();return 'off'}
      const pick=()=>{
        const direct=document.querySelector('article,main,[role=main]');
        if(direct&&direct.innerText&&direct.innerText.trim().length>400)return direct;
        let best=null,score=0;
        document.querySelectorAll('div,section').forEach(el=>{
          if(el.querySelectorAll(':scope > p').length<3)return;
          const len=el.innerText?el.innerText.trim().length:0;
          if(len>score){score=len;best=el}
        });
        return best};
      const source=pick();
      if(!source)return 'none';
      const title=(document.querySelector('h1')||{}).innerText||document.title||'';
      const wrap=document.createElement('div');
      wrap.id=ID;
      wrap.setAttribute('style','position:fixed;inset:0;z-index:2147483646;overflow:auto;background:#0B0912;color:#E7E2F0;font:17px/1.75 \"Segoe UI Variable Display\",\"Segoe UI\",sans-serif;padding:58px 20px 90px');
      const art=document.createElement('div');
      art.setAttribute('style','max-width:720px;margin:0 auto');
      const head=document.createElement('div');
      head.textContent=title;
      head.setAttribute('style','font-size:29px;line-height:1.25;font-weight:650;margin:0 0 6px;color:#F5F2FA');
      const meta=document.createElement('div');
      meta.textContent=location.host.replace(/^www\./,'')+' \u00b7 \u0420\u0435\u0436\u0438\u043c \u0447\u0442\u0435\u043d\u0438\u044f';
      meta.setAttribute('style','font-size:12.5px;color:#8B8598;margin-bottom:26px;letter-spacing:.2px');
      const body=document.createElement('div');
      body.innerHTML=source.innerHTML;
      body.querySelectorAll('script,style,iframe,ins,noscript,form,button,nav,aside').forEach(n=>n.remove());
      body.querySelectorAll('img').forEach(n=>n.setAttribute('style','max-width:100%;height:auto;border-radius:12px;margin:14px 0'));
      body.querySelectorAll('a').forEach(n=>n.style.color='__LUMA_READER_ACCENT__');
      body.querySelectorAll('h1,h2,h3').forEach(n=>n.style.color='#F5F2FA');
      const close=document.createElement('div');
      close.innerHTML='<svg viewBox="0 0 24 24" aria-hidden="true" style="width:16px;height:16px;fill:none;stroke:currentColor;stroke-width:2;stroke-linecap:round;stroke-linejoin:round"><path d="M18 6 6 18"></path><path d="m6 6 12 12"></path></svg>';
      close.setAttribute('style','position:fixed;top:18px;right:22px;width:34px;height:34px;border-radius:11px;background:#17181A;border:1px solid #323338;color:#CFC7D5;display:flex;align-items:center;justify-content:center;cursor:pointer;font-size:13px');
      close.addEventListener('click',()=>{wrap.remove();restore()});
      art.appendChild(head);art.appendChild(meta);art.appendChild(body);
      wrap.appendChild(art);wrap.appendChild(close);
      window.__lumaReaderOverflow=document.documentElement.style.overflow;
      document.documentElement.appendChild(wrap);
      document.documentElement.style.overflow='hidden';
      document.addEventListener('keydown',function esc(e){if(e.key==='Escape'){const live=document.getElementById(ID);if(live){live.remove();restore()}document.removeEventListener('keydown',esc)}});
      return 'on'
    }catch(e){return 'err'}})()
    """;

    /// <summary>Replaces white Chromium scrollbars in normal pages, internal pages and open
    /// shadow roots with the same compact Luma treatment used by native WPF panels.</summary>
    public static string Scrollbars(string accent)
    {
        var safe = string.IsNullOrWhiteSpace(accent) ? "#7468C7" : accent;
        var script = """
        (()=>{try{
          window.__lumaScrollbarAccent=__LUMA_SCROLLBAR_ACCENT__;
          const css=()=>{
            const a=window.__lumaScrollbarAccent||'#7468C7';
            return `:root,*{scrollbar-width:thin;scrollbar-color:${a} #17181a}`+
              `::-webkit-scrollbar{width:10px;height:10px}`+
              `::-webkit-scrollbar-track{background:#17181a;border-radius:8px}`+
              `::-webkit-scrollbar-thumb{background:${a};background:linear-gradient(180deg,color-mix(in srgb,${a} 72%,#cbbcff),${a});border:2px solid #17181a;border-radius:8px;min-height:34px;min-width:34px}`+
              `::-webkit-scrollbar-thumb:hover{background:color-mix(in srgb,${a} 82%,white)}`+
              `::-webkit-scrollbar-corner{background:#17181a}`;
          };
          const apply=root=>{try{if(!root)return;let style=root.getElementById?root.getElementById('__lumaScrollbars'):null;if(!style){style=document.createElement('style');style.id='__lumaScrollbars';(root.head||root).appendChild(style)}style.textContent=css()}catch(e){}};
          const scan=root=>{try{apply(root);if(!root.querySelectorAll)return;root.querySelectorAll('*').forEach(el=>{if(el.shadowRoot)scan(el.shadowRoot)})}catch(e){}};
          window.__lumaSetScrollbar=a=>{window.__lumaScrollbarAccent=a||'#7468C7';scan(document)};
          const run=()=>scan(document);
          if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',run,{once:true});else run();
          new MutationObserver(m=>m.forEach(x=>x.addedNodes&&x.addedNodes.forEach(n=>{if(n.nodeType===1){if(n.shadowRoot)scan(n.shadowRoot);if(n.querySelectorAll)n.querySelectorAll('*').forEach(el=>{if(el.shadowRoot)scan(el.shadowRoot)})}}))).observe(document.documentElement,{childList:true,subtree:true});
          setInterval(run,3000);
        }catch(e){}})()
        """;
        return script.Replace("__LUMA_SCROLLBAR_ACCENT__", JsonSerializer.Serialize(safe), StringComparison.Ordinal);
    }
    public static string SetScrollbarAccent(string accent) => $"window.__lumaSetScrollbar?.({JsonSerializer.Serialize(accent)})";

    public static string ContextMenu(string pane) => (_contextMenu ??= ReadResource(ContextMenuResourceName)).Replace("__PANE__", pane == "secondary" ? "secondary" : "primary", StringComparison.Ordinal);
    private static string ReadResource(string name) { using var stream = typeof(BrowserScripts).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Встроенный ресурс {name} не найден."); using var reader = new StreamReader(stream, Encoding.UTF8, true); return reader.ReadToEnd(); }
}
