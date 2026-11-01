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

    private static string? _autourokScript;
    public static string AutourokScript
    {
        get
        {
            try
            {
                var baseDir = AppContext.BaseDirectory;
                var diskJs = Path.Combine(baseDir, "Assets", "autourok.js");
                if (File.Exists(diskJs))
                {
                    var js = File.ReadAllText(diskJs);
                    var iconPath = Path.Combine(baseDir, "Assets", "autourok-icon.png");
                    string iconB64 = "";
                    if (File.Exists(iconPath))
                        iconB64 = Convert.ToBase64String(File.ReadAllBytes(iconPath));
                    if (string.IsNullOrEmpty(iconB64))
                    {
                        using var s = typeof(BrowserScripts).Assembly.GetManifestResourceStream("Luma.Assets.autourok-icon.png");
                        if (s is not null) { using var ms = new MemoryStream(); s.CopyTo(ms); iconB64 = Convert.ToBase64String(ms.ToArray()); }
                    }
                    return js.Replace("__AUTOUROK_ICON_BASE64__", iconB64, StringComparison.Ordinal);
                }
            }
            catch { }
            if (_autourokScript is not null) return _autourokScript;
            var resJs = ReadResource("Luma.Assets.autourok.js");
            string iconB64Res = "";
            try
            {
                using var stream = typeof(BrowserScripts).Assembly.GetManifestResourceStream("Luma.Assets.autourok-icon.png");
                if (stream is not null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    iconB64Res = Convert.ToBase64String(ms.ToArray());
                }
            }
            catch { }
            return _autourokScript = resJs.Replace("__AUTOUROK_ICON_BASE64__", iconB64Res, StringComparison.Ordinal);
        }
    }
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
    (()=>{try{const done=new Set();const add=u=>{if(!u||done.has(u)||done.size>20)return;done.add(u);try{const l=document.createElement('link');l.rel='prefetch';l.as='document';l.href=u;(document.head||document.documentElement).appendChild(l)}catch(e){}};let t=null;document.addEventListener('mouseover',e=>{const a=e.target&&e.target.closest?e.target.closest('a[href]'):null;if(!a)return;clearTimeout(t);t=setTimeout(()=>{try{const u=new URL(a.getAttribute('href'),location.href);if(u.origin===location.origin&&/^https?:$/.test(u.protocol)&&u.href!==location.href)add(u.href)}catch(x){}},350)},true);document.addEventListener('mouseout',()=>clearTimeout(t),true)}catch(e){}})()
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

    /// <summary>
    /// YouTube Ambient Light: creates real-time reactive dynamic glow around the video player
    /// matching the edges and colors of the playing video, identical to the Ambient Light for YouTube extension.
    /// </summary>
    public static string AmbientLight(bool enabled) => $$"""
    (() => {
      try {
        if (window.__lumaAmbientInitialized) {
          if (typeof window.__lumaSetAmbientEnabled === 'function') {
            window.__lumaSetAmbientEnabled({{enabled.ToString().ToLowerInvariant()}});
          }
          return;
        }
        window.__lumaAmbientInitialized = true;

        let enabled = {{enabled.ToString().ToLowerInvariant()}};
        window.__lumaSetAmbientEnabled = (val) => {
          enabled = Boolean(val);
          const container = document.getElementById('luma-ambient-container');
          if (container) {
            container.style.display = enabled ? 'block' : 'none';
          }
        };

        let container = null, canvas = null, ctx = null, activeVideo = null;

        function initDOM() {
          if (document.getElementById('luma-ambient-container')) return;

          const style = document.createElement('style');
          style.id = 'luma-ambient-style';
          style.textContent = `
            #luma-ambient-container {
              position: absolute;
              inset: -14% -18%;
              width: 136%;
              height: 128%;
              pointer-events: none;
              z-index: 0;
              overflow: visible;
              display: ${enabled ? 'block' : 'none'};
              opacity: 0.95;
              transition: opacity 0.4s ease;
            }
            #luma-ambient-canvas {
              position: absolute;
              top: 50%;
              left: 50%;
              transform: translate(-50%, -50%);
              width: 100%;
              height: 100%;
              pointer-events: none;
              filter: blur(80px) saturate(2.4) brightness(1.3);
              transform-origin: center center;
              will-change: transform, filter;
            }
            #movie_player, .html5-video-player, #player-container, #ytd-player {
              overflow: visible !important;
            }
            #movie_player video, .html5-video-player video {
              position: relative !important;
              z-index: 1 !important;
            }
            .ytp-chrome-bottom, .ytp-chrome-top, .ytp-gradient-bottom, .ytp-gradient-top {
              z-index: 35 !important;
            }
            ytd-watch-flexy[theater] #player-theater-container,
            #player-container-outer,
            #player-container-inner {
              overflow: visible !important;
            }
            #cinematics {
              display: none !important;
            }
          `;
          (document.head || document.documentElement).appendChild(style);

          container = document.createElement('div');
          container.id = 'luma-ambient-container';

          canvas = document.createElement('canvas');
          canvas.id = 'luma-ambient-canvas';
          canvas.width = 64;
          canvas.height = 36;
          ctx = canvas.getContext('2d', { alpha: false, desynchronized: true });

          container.appendChild(canvas);
        }

        function mount() {
          initDOM();
          if (!container) return;
          const player = document.querySelector('#movie_player') ||
                        document.querySelector('.html5-video-player') ||
                        document.querySelector('#player-container') ||
                        document.querySelector('ytd-player');

          if (player && container.parentElement !== player) {
            player.insertBefore(container, player.firstChild);
          }
        }

        function tick() {
          if (enabled) {
            if (!activeVideo || !activeVideo.isConnected) {
              activeVideo = document.querySelector('video.html5-main-video') || document.querySelector('video');
              mount();
            }
            if (activeVideo && ctx && !activeVideo.paused && !activeVideo.ended && activeVideo.readyState >= 2) {
              try {
                ctx.drawImage(activeVideo, 0, 0, canvas.width, canvas.height);
              } catch(e) {}
            }
          }
          requestAnimationFrame(tick);
        }

        const obs = new MutationObserver(() => mount());
        obs.observe(document.body || document.documentElement, { childList: true, subtree: true });

        mount();
        requestAnimationFrame(tick);
      } catch (e) {}
    })()
    """;

    /// <summary>Customizes Chrome Web Store and Microsoft Edge Add-ons buttons to "Добавить в Luma" and intercepts installation requests.</summary>
    public const string ChromeWebStoreScript = """
    (() => { try {
      const host = (location.hostname || '').toLowerCase();
      const isChromeStore = host.includes('chromewebstore.google.com') || host.includes('chrome.google.com');
      const isEdgeStore = host.includes('microsoftedge.microsoft.com') || host.includes('edge.microsoft.com');
      if (!isChromeStore && !isEdgeStore) return;

      function extractInfo(details, fallbackId) {
        let id = (details && details.id) ? details.id : fallbackId;
        if (!id) {
          const match = location.pathname.match(/\/detail\/(?:[^\/]+\/)?([a-z0-9]{32})/i) ||
                        location.pathname.match(/\/detail\/([a-z0-9]+)/i);
          if (match && match[1]) id = match[1];
        }

        let title = (details && details.localizedName) ? details.localizedName : '';
        if (!title) {
          const h1 = document.querySelector('h1');
          if (h1) title = (h1.textContent || '').trim();
        }
        if (!title) title = (document.title || '').split(/[-–|]/)[0].trim();

        let icon = (details && details.iconUrl) ? details.iconUrl : '';
        if (!icon) {
          const img = document.querySelector('img[src*="googleusercontent.com"], img[src*="chrome-extension"], #product-identity img, .identity-section img, img[src*="store-images"], img[src*="azurefd.net"]');
          if (img) icon = img.src;
        }

        const manifest = (details && details.manifest) ? details.manifest : '';
        return {
          id: id || '',
          title: title || 'Расширение',
          icon: icon || '',
          manifest: manifest,
          source: isEdgeStore ? 'edge' : 'chrome'
        };
      }

      function sendInstallPrompt(info) {
        if (!info || !info.id) return;
        try {
          window.chrome.webview.postMessage({
            kind: 'luma-prompt-install-extension',
            id: info.id,
            title: info.title,
            icon: info.icon,
            manifest: info.manifest || '',
            source: info.source
          });
        } catch (e) {}
      }

      // 1. INTERCEPT CHROMIUM / EDGE NATIVE WEBSTORE APIS
      // This completely suppresses Chromium's native "Добавить в Microsoft Edge?" dialog
      // and eliminates the broken internal download ("Download interrupted")!
      function hookWebstorePrivate() {
        try {
          if (!window.chrome) window.chrome = {};

          function applyHooks(wp) {
            if (!wp || wp.__lumaHooked) return;
            try {
              wp.__lumaHooked = true;

              wp.beginInstallWithManifest3 = function(details, callback) {
                window.__lumaEdgeCallback = callback;
                const info = extractInfo(details, details ? details.id : '');
                sendInstallPrompt(info);
              };

              wp.install = function(id, callback) {
                window.__lumaEdgeCallback = callback;
                const info = extractInfo(null, id);
                sendInstallPrompt(info);
              };

              if (wp.completeInstall) {
                wp.completeInstall = function(id, callback) {
                  if (typeof callback === 'function') {
                    try { callback(); } catch(e) {}
                  }
                };
              }
            } catch (ex) {}
          }

          if (window.chrome.webstorePrivate) {
            applyHooks(window.chrome.webstorePrivate);
          }

          let currentWp = window.chrome.webstorePrivate;
          try {
            Object.defineProperty(window.chrome, 'webstorePrivate', {
              get: () => currentWp,
              set: (v) => {
                currentWp = v;
                applyHooks(v);
              },
              configurable: true,
              enumerable: true
            });
          } catch(e) {}

          // Polling fallback to guarantee webstorePrivate is hooked as soon as Chromium exposes it
          setInterval(() => {
            if (window.chrome && window.chrome.webstorePrivate && !window.chrome.webstorePrivate.__lumaHooked) {
              applyHooks(window.chrome.webstorePrivate);
            }
          }, 60);
        } catch (e) {}
      }

      hookWebstorePrivate();

      // Callback handlers called by Luma C# after install or cancel
      window.__lumaOnInstalled = function() {
        if (window.__lumaEdgeCallback) {
          try { window.__lumaEdgeCallback('success'); } catch(e) {}
          window.__lumaEdgeCallback = null;
        }
        patchButtons(true);
      };

      window.__lumaOnCancelled = function() {
        if (window.__lumaEdgeCallback) {
          try { window.__lumaEdgeCallback('user_cancelled'); } catch(e) {}
          window.__lumaEdgeCallback = null;
        }
      };

      // 2. PATCH WEB STORE UI BUTTONS
      function patchButtons(isInstalled) {
        try {
          const candidates = document.querySelectorAll('button, a[role="button"], div[role="button"], [id*="install" i], [id*="get" i]');
          candidates.forEach(btn => {
            if (isInstalled && btn.dataset.lumaPatched) {
              const textSpan = btn.querySelector('.fui-Button__text, span, div') || btn;
              textSpan.innerText = '✓ Добавлено в Luma';
              btn.style.setProperty('background', 'linear-gradient(135deg, #10B981 0%, #059669 100%)', 'important');
              btn.disabled = true;
              return;
            }

            const txt = (btn.textContent || '').trim();
            const btnId = (btn.id || '').toLowerCase();
            const aria = (btn.getAttribute('aria-label') || '').toLowerCase();

            const isStoreBtn =
              btnId === 'getorinstallbutton' ||
              btnId.includes('installbutton') ||
              aria.includes('получить') ||
              aria.includes('get') ||
              aria.includes('add to') ||
              aria.includes('добавить') ||
              txt.includes('Add to Chrome') ||
              txt.includes('Установить в Chrome') ||
              txt.includes('Добавить в Microsoft Edge') ||
              txt.includes('Получить') ||
              txt.includes('Get') ||
              (txt === 'Установить' && !btn.dataset.lumaPatched);

            if (isStoreBtn && !btn.dataset.lumaPatched) {
              btn.dataset.lumaPatched = 'true';

              // Custom Luma Modern Styling
              btn.style.setProperty('background', 'linear-gradient(135deg, #8A7CF8 0%, #6353D8 100%)', 'important');
              btn.style.setProperty('color', '#FFFFFF', 'important');
              btn.style.setProperty('border-radius', '12px', 'important');
              btn.style.setProperty('font-weight', '600', 'important');
              btn.style.setProperty('box-shadow', '0 4px 16px rgba(138, 124, 248, 0.45)', 'important');
              btn.style.setProperty('border', '1px solid rgba(255, 255, 255, 0.25)', 'important');
              btn.style.setProperty('transition', 'all 0.2s cubic-bezier(0.16, 1, 0.3, 1)', 'important');
              btn.style.setProperty('cursor', 'pointer', 'important');

              const textSpan = btn.querySelector('.fui-Button__text, span, div') || btn;
              if (textSpan && textSpan !== btn) textSpan.innerText = 'Добавить в Luma';
              else btn.innerText = 'Добавить в Luma';

              const clickHandler = (e) => {
                e.preventDefault();
                e.stopPropagation();
                e.stopImmediatePropagation();
                const info = extractInfo(null, '');
                sendInstallPrompt(info);
              };

              btn.addEventListener('click', clickHandler, true);
              btn.onclick = clickHandler;
            }
          });
        } catch (e) {}
      }

      patchButtons(false);
      const obs = new MutationObserver(() => patchButtons(false));
      obs.observe(document.body || document.documentElement, { childList: true, subtree: true });
      setInterval(() => patchButtons(false), 900);
    } catch (e) {} })()
    """;


    /// <summary>
    /// LumaSkip & YouTube AdBlock: Eliminates all YouTube pre-rolls, mid-rolls, display ads,
    /// and automatically skips sponsor segments using SponsorBlock API with pure Lucide vector icons.
    /// </summary>
    public const string LumaSkipScript = """
    (() => {
      try {
        if (!location.hostname.includes('youtube.com')) return;

        // Idempotency: avoid duplicate observers or intervals if script is re-injected
        if (window.__lumaSkipInstalled) {
          if (window.__lumaCheckState) window.__lumaCheckState();
          return;
        }
        window.__lumaSkipInstalled = true;

        // 1. YouTube Built-in Ads Blocker (Instant CSS injection)
        const injectStyles = () => {
          try {
            if (document.getElementById('luma-adblock-styles')) return;
            const style = document.createElement('style');
            style.id = 'luma-adblock-styles';
            style.textContent = `
              /* Banners, sidebars, companion cards, masthead ads */
              ytd-companion-slot-renderer,
              ytd-action-companion-ad-renderer,
              #companion,
              #fulfilled-layout,
              ytd-ad-slot-renderer,
              ytd-in-feed-ad-layout-renderer,
              ytd-banner-promo-renderer,
              ytd-promoted-video-renderer,
              ytd-promoted-sparkles-web-renderer,
              ytd-statement-banner-renderer,
              #masthead-ad,
              #player-ads,
              ytd-engagement-panel-section-list-renderer[target-id="engagement-panel-ads"],
              ytd-merch-shelf-renderer,
              ytd-player-legacy-desktop-watch-ads-renderer,
              ytd-compact-promoted-video-renderer,
              ytd-brand-video-singleton-renderer,
              ytd-single-hero-image-ad-renderer,
              ytd-carousel-ad-renderer,
              #secondary ytd-companion-slot-renderer,
              #secondary ytd-ad-slot-renderer,
              #secondary ytd-in-feed-ad-layout-renderer,
              ytd-ad-hover-text-button-renderer {
                display: none !important;
                visibility: hidden !important;
                width: 0 !important;
                height: 0 !important;
                pointer-events: none !important;
              }

              /* Video Player Overlays (in-video ads & banners) */
              .video-ads,
              .ytp-ad-module,
              .ytp-ad-overlay-container,
              .ytp-ad-overlay-slot,
              .ytp-ad-image-overlay,
              .ytp-ad-text-overlay,
              .ytp-ad-player-overlay,
              .ytp-ad-player-overlay-layout,
              .ytp-ad-action-interstitial,
              .ytp-ad-action-interstitial-slot,
              .ytp-ad-action-interstitial-background-container,
              .ytp-ad-persistent-progress-bar-container,
              div[class*="ytp-ad-overlay"],
              div[class*="ytp-ad-action"],
              div[class*="ytp-ad-player-overlay"],
              div[class*="ytp-ad-message"],
              div[class*="ytp-ad-preview"],
              div[class*="ytp-ad-component"],
              div[class*="ytp-ad-avatar"] {
                display: none !important;
                visibility: hidden !important;
                width: 0 !important;
                height: 0 !important;
                opacity: 0 !important;
                pointer-events: none !important;
              }

              /* Anti-adblock dialogs */
              ytd-enforcement-message-view-model,
              tp-yt-paper-dialog:has(ytd-enforcement-message-view-model) {
                display: none !important;
              }

              /* LumaSkip Glassmorphism Pill */
              #lumaskip-pill {
                position: absolute;
                bottom: 74px;
                left: 24px;
                z-index: 999999;
                display: flex;
                align-items: center;
                gap: 10px;
                padding: 8px 14px;
                background: rgba(18, 18, 22, 0.88);
                backdrop-filter: blur(20px);
                -webkit-backdrop-filter: blur(20px);
                border: 1px solid rgba(255, 255, 255, 0.16);
                border-radius: 12px;
                box-shadow: 0 10px 30px rgba(0, 0, 0, 0.6), 0 2px 8px rgba(0, 0, 0, 0.35);
                color: #F4F4F5;
                font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
                font-size: 13px;
                font-weight: 500;
                pointer-events: auto;
                opacity: 0;
                transform: translateY(10px) scale(0.96);
                transition: opacity 0.22s cubic-bezier(0.16, 1, 0.3, 1), transform 0.22s cubic-bezier(0.16, 1, 0.3, 1);
              }
              #lumaskip-pill.lumaskip-visible {
                opacity: 1;
                transform: translateY(0) scale(1);
              }
              .lumaskip-icon-box {
                display: flex;
                align-items: center;
                justify-content: center;
                width: 24px;
                height: 24px;
                border-radius: 6px;
                background: rgba(167, 139, 250, 0.18);
                color: #A78BFA;
                flex-shrink: 0;
              }
              .lumaskip-content {
                display: flex;
                align-items: center;
                gap: 6px;
                white-space: nowrap;
              }
              .lumaskip-brand {
                font-weight: 700;
                color: #FFFFFF;
                letter-spacing: -0.2px;
              }
              .lumaskip-desc {
                color: #D4D4D8;
                font-size: 12.5px;
              }
              .lumaskip-undo-btn {
                display: inline-flex;
                align-items: center;
                gap: 5px;
                background: rgba(255, 255, 255, 0.12);
                border: 1px solid rgba(255, 255, 255, 0.18);
                color: #FFFFFF;
                border-radius: 7px;
                padding: 4px 10px;
                font-size: 12px;
                font-weight: 600;
                cursor: pointer;
                transition: background 0.15s ease, transform 0.15s ease;
                outline: none;
              }
              .lumaskip-undo-btn:hover {
                background: rgba(255, 255, 255, 0.24);
                transform: scale(1.02);
              }
              .lumaskip-undo-btn:active {
                transform: scale(0.96);
              }

              /* Seek Bar Sponsor Markers */
              .lumaskip-bar-marker {
                position: absolute;
                top: 0;
                bottom: 0;
                height: 100%;
                border-radius: 2px;
                opacity: 0.92;
                pointer-events: none;
                z-index: 35;
                box-shadow: 0 0 3px rgba(0, 0, 0, 0.4);
              }
            `;
            const root = document.head || document.documentElement;
            if (root) {
              root.appendChild(style);
            } else {
              document.addEventListener('DOMContentLoaded', () => {
                (document.head || document.documentElement)?.appendChild(style);
              }, { once: true });
            }
          } catch (e) {}
        };

        // Instant DOM ad purging
        const isAdText = (t) => {
          if (!t) return false;
          const s = t.toLowerCase();
          return s.includes('ad') || s.includes('sponsored') || s.includes('promoted') ||
                 s.includes('\u0440\u0435\u043a\u043b\u0430\u043c\u0430') || // реклама
                 s.includes('\u0441\u043f\u043e\u043d\u0441\u043e\u0440') || // спонсор
                 s.includes('\u043f\u0440\u043e\u043c\u043e') ||             // промо
                 s.includes('\u0430\u043a\u0446\u0438\u044f');               // акция
        };

        const purgeAdElements = () => {
          try {
            const selectors = [
              'ytd-companion-slot-renderer',
              'ytd-action-companion-ad-renderer',
              '#companion',
              '#fulfilled-layout',
              'ytd-ad-slot-renderer',
              'ytd-in-feed-ad-layout-renderer',
              'ytd-banner-promo-renderer',
              'ytd-promoted-video-renderer',
              'ytd-promoted-sparkles-web-renderer',
              'ytd-statement-banner-renderer',
              '#masthead-ad',
              '#player-ads',
              '.video-ads',
              '.ytp-ad-module',
              '.ytp-ad-overlay-container',
              '.ytp-ad-overlay-slot',
              '.ytp-ad-image-overlay',
              '.ytp-ad-text-overlay',
              '.ytp-ad-player-overlay-layout',
              '.ytp-ad-player-overlay-flyout-cta',
              '.ytp-ad-action-interstitial',
              'ytd-engagement-panel-section-list-renderer[target-id="engagement-panel-ads"]',
              'ytd-ad-hover-text-button-renderer'
            ];
            for (let i = 0; i < selectors.length; i++) {
              const items = document.querySelectorAll(selectors[i]);
              for (let j = 0; j < items.length; j++) {
                items[j].remove();
              }
            }

            // In-player overlay removal
            const playerOverlays = document.querySelectorAll('#movie_player [class*="ytp-ad"], .ytp-ad-module, .ytp-ad-overlay-container, .ytp-ad-overlay-slot');
            for (let i = 0; i < playerOverlays.length; i++) {
              playerOverlays[i].remove();
            }

            // Secondary column & feed ad cards removal
            const badges = document.querySelectorAll('ytd-badge-supported-renderer, .badge-style-type-ad, ytd-ad-slot-renderer, ytd-companion-slot-renderer, ytd-banner-promo-renderer, ytd-statement-banner-renderer, ytd-in-feed-ad-layout-renderer');
            for (let i = 0; i < badges.length; i++) {
              const b = badges[i];
              const tag = b.tagName.toLowerCase();
              if (tag.includes('ad-') || tag.includes('companion') || tag.includes('banner-promo') || tag.includes('statement-banner')) {
                const item = b.closest('ytd-compact-video-renderer, ytd-rich-item-renderer, ytd-item-section-renderer, #panels, ytd-banner-promo-renderer, ytd-statement-banner-renderer') || b;
                item.remove();
                continue;
              }
              const txt = (b.textContent || '').trim();
              const aria = b.getAttribute('aria-label') || '';
              if (isAdText(txt) || isAdText(aria)) {
                const item = b.closest('ytd-compact-video-renderer, ytd-rich-item-renderer, ytd-item-section-renderer, #panels');
                if (item) item.remove();
                else b.remove();
              }
            }

            const panels = document.querySelector('#secondary #panels, #panels');
            if (panels) {
              if (panels.querySelector('ytd-ad-slot-renderer, ytd-companion-slot-renderer') ||
                  isAdText(panels.textContent) ||
                  panels.querySelector('a[href*="googleadservices"], a[href*="doubleclick"]')) {
                panels.remove();
              }
            }
          } catch (e) {}
        };

        // 2. Video Ad Fast-Forward & Skipper (SAFE: never touches normal video)
        let weMutedAd = false;
        const handleVideoAds = () => {
          try {
            const player = document.getElementById('movie_player') || document.querySelector('.html5-video-player');
            if (!player) return;

            // Strictly check if player has ad-showing or ad-interrupting class
            const isAd = player.classList.contains('ad-showing') || player.classList.contains('ad-interrupting');
            const video = player.querySelector('video') || document.querySelector('video');

            if (isAd) {
              if (video) {
                if (!video.muted) {
                  video.muted = true;
                  weMutedAd = true;
                }
                video.playbackRate = 16.0;
                if (isFinite(video.duration) && video.duration > 0 && video.currentTime < video.duration) {
                  video.currentTime = video.duration;
                }
              }

              const skipBtn = player.querySelector(
                '.ytp-ad-skip-button, .ytp-ad-skip-button-modern, .ytp-skip-ad-button, ' +
                'button.ytp-ad-skip-button, .ytp-ad-skip-button-slot button, .ytp-ad-overlay-close-button'
              );
              if (skipBtn) skipBtn.click();

              try {
                if (typeof player.skipAd === 'function') player.skipAd();
                if (typeof player.cancelPlayback === 'function') player.cancelPlayback();
              } catch (e) {}
            } else {
              // NORMAL VIDEO PLAYBACK: ensure playbackRate is NEVER stuck above normal!
              if (video) {
                if (video.playbackRate > 2.0) {
                  video.playbackRate = 1.0;
                }
                if (weMutedAd) {
                  video.muted = false;
                  weMutedAd = false;
                }
              }
            }

            // Auto dismiss anti-adblock overlay
            const enforceDialog = document.querySelector('ytd-enforcement-message-view-model');
            if (enforceDialog) {
              enforceDialog.closest('tp-yt-paper-dialog')?.remove();
              document.querySelector('tp-yt-iron-overlay-backdrop')?.remove();
              if (video && video.paused) video.play();
            }
          } catch (e) {}
        };

        // 3. Lucide Vector Icons Generator (No innerHTML, TrustedHTML compliant)
        const createLucideIcon = (type) => {
          const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
          svg.setAttribute('viewBox', '0 0 24 24');
          svg.setAttribute('fill', 'none');
          svg.setAttribute('stroke', 'currentColor');
          svg.setAttribute('stroke-width', '2.2');
          svg.setAttribute('stroke-linecap', 'round');
          svg.setAttribute('stroke-linejoin', 'round');
          if (type === 'zap') {
            svg.setAttribute('width', '15');
            svg.setAttribute('height', '15');
            const poly = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');
            poly.setAttribute('points', '13 2 3 14 12 14 11 22 21 10 12 10 13 2');
            svg.appendChild(poly);
          } else if (type === 'undo') {
            svg.setAttribute('width', '12');
            svg.setAttribute('height', '12');
            const p1 = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            p1.setAttribute('d', 'M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8');
            const p2 = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            p2.setAttribute('d', 'M3 3v5h5');
            svg.appendChild(p1);
            svg.appendChild(p2);
          }
          return svg;
        };

        // 4. LumaSkip Core (SponsorBlock Integration via C# Bridge + Direct Fetch)
        let currentVideoId = null;
        let segments = [];
        const unskippedUUIDs = new Set();
        let pillTimer = null;

        const formatSec = (s) => {
          s = Math.floor(s || 0);
          const m = Math.floor(s / 60);
          const sec = s % 60;
          return `${m}:${sec < 10 ? '0' : ''}${sec}`;
        };

        const getCategoryName = (cat) => {
          switch (cat) {
            case 'sponsor': return 'спонсор';
            case 'selfpromo': return 'самореклама';
            case 'interaction': return 'напоминание';
            case 'intro': return 'интро';
            case 'outro': return 'концовка';
            case 'preview': return 'анонс';
            case 'filler': return 'пауза';
            default: return 'вставка';
          }
        };

        const getPill = () => {
          let pill = document.getElementById('lumaskip-pill');
          if (!pill) {
            pill = document.createElement('div');
            pill.id = 'lumaskip-pill';

            const iconBox = document.createElement('div');
            iconBox.className = 'lumaskip-icon-box';
            iconBox.appendChild(createLucideIcon('zap'));
            pill.appendChild(iconBox);

            const content = document.createElement('div');
            content.className = 'lumaskip-content';

            const brand = document.createElement('span');
            brand.className = 'lumaskip-brand';
            brand.textContent = 'LumaSkip';
            content.appendChild(brand);

            const desc = document.createElement('span');
            desc.className = 'lumaskip-desc';
            desc.id = 'lumaskip-desc';
            desc.textContent = 'Пропущен спонсор';
            content.appendChild(desc);

            pill.appendChild(content);

            const undoBtn = document.createElement('button');
            undoBtn.className = 'lumaskip-undo-btn';
            undoBtn.id = 'lumaskip-undo-btn';
            undoBtn.appendChild(createLucideIcon('undo'));
            const undoText = document.createElement('span');
            undoText.textContent = 'Отменить';
            undoBtn.appendChild(undoText);

            pill.appendChild(undoBtn);

            const container = document.getElementById('movie_player') || document.querySelector('.html5-video-player') || document.body;
            container.appendChild(pill);
          }
          return pill;
        };

        const showPill = (seg) => {
          const pill = getPill();
          const desc = document.getElementById('lumaskip-desc');
          const undoBtn = document.getElementById('lumaskip-undo-btn');

          const catName = getCategoryName(seg.category);
          if (desc) desc.textContent = `Пропущен(а) ${catName} (${formatSec(seg.segment[0])} — ${formatSec(seg.segment[1])})`;

          if (undoBtn) {
            undoBtn.onclick = (e) => {
              e.preventDefault();
              e.stopPropagation();
              const video = document.querySelector('video');
              if (video) video.currentTime = seg.segment[0];
              unskippedUUIDs.add(seg.UUID);
              hidePill();
            };
          }

          pill.classList.add('lumaskip-visible');
          clearTimeout(pillTimer);
          pillTimer = setTimeout(hidePill, 3600);
        };

        const hidePill = () => {
          const pill = document.getElementById('lumaskip-pill');
          if (pill) pill.classList.remove('lumaskip-visible');
        };

        const categoryColors = {
          'sponsor': '#00D26A',
          'selfpromo': '#FFD700',
          'interaction': '#CC44FF',
          'intro': '#00FFFF',
          'outro': '#3B82F6',
          'preview': '#60A5FA',
          'filler': '#A855F7'
        };

        const renderMarkers = () => {
          if (!segments || !segments.length) return;
          const progressBar = document.querySelector('.ytp-progress-bar') || document.querySelector('.ytp-progress-list');
          if (!progressBar) return;

          const existingMarkers = progressBar.querySelectorAll('.lumaskip-bar-marker');
          if (existingMarkers.length === segments.length) return;

          existingMarkers.forEach(m => m.remove());

          const video = document.querySelector('video');
          let duration = (video && isFinite(video.duration) && video.duration > 0) ? video.duration : 0;
          if (!duration && segments[0] && segments[0].videoDuration) {
            duration = segments[0].videoDuration;
          }
          if (!duration || duration <= 0) return;

          segments.forEach(seg => {
            if (!seg || !seg.segment || seg.segment.length < 2) return;
            const start = Math.max(0, seg.segment[0]);
            const end = Math.min(duration, seg.segment[1]);
            if (end <= start) return;

            const leftPercent = (start / duration) * 100;
            const widthPercent = Math.max(0.35, ((end - start) / duration) * 100);

            const marker = document.createElement('div');
            marker.className = 'lumaskip-bar-marker';
            marker.style.left = `${leftPercent.toFixed(2)}%`;
            marker.style.width = `${widthPercent.toFixed(2)}%`;
            marker.style.backgroundColor = categoryColors[seg.category] || '#00D26A';
            progressBar.appendChild(marker);
          });
        };

        const checkSponsorSegments = () => {
          if (!segments || !segments.length) return;
          const video = document.querySelector('video');
          if (!video || !isFinite(video.currentTime) || video.duration <= 0) return;

          // Never skip while player is playing an ad
          const player = document.getElementById('movie_player') || document.querySelector('.html5-video-player');
          if (player && (player.classList.contains('ad-showing') || player.classList.contains('ad-interrupting'))) {
            return;
          }

          const t = video.currentTime;
          for (const seg of segments) {
            if (!seg || !seg.segment || seg.segment.length < 2) continue;
            const start = seg.segment[0];
            const end = seg.segment[1];
            if (t >= start - 0.25 && t < end - 0.1 && !unskippedUUIDs.has(seg.UUID)) {
              video.currentTime = end;
              showPill(seg);
              break;
            }
          }
        };

        const hookVideo = () => {
          const v = document.querySelector('video');
          if (!v || v.__lumaSkipHooked) return;
          v.__lumaSkipHooked = true;
          v.addEventListener('timeupdate', checkSponsorSegments);
          v.addEventListener('seeking', checkSponsorSegments);
        };

        const requestSegments = (vid) => {
          if (!vid) {
            segments = [];
            return;
          }
          // 1. Ask C# WebBridge
          try {
            if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
              window.chrome.webview.postMessage({ kind: 'lumaskip-request', videoId: vid });
            }
          } catch (e) {}

          // 2. Direct browser fetch as parallel fast-fallback
          const catParam = '%5B%22sponsor%22,%22selfpromo%22,%22interaction%22,%22intro%22,%22outro%22,%22preview%22,%22filler%22%5D';
          const apiUrl = `https://sponsor.ajay.app/api/skipSegments?videoID=${encodeURIComponent(vid)}&categories=${catParam}`;
          fetch(apiUrl)
            .then(res => res.ok ? res.json() : [])
            .then(data => {
              if (Array.isArray(data) && currentVideoId === vid && (!segments || segments.length === 0)) {
                window.__lumaReceiveSegments({ videoId: vid, segments: data });
              }
            })
            .catch(() => {
              const mirrorUrl = `https://api.sponsor.ajay.app/api/skipSegments?videoID=${encodeURIComponent(vid)}&categories=${catParam}`;
              fetch(mirrorUrl)
                .then(res => res.ok ? res.json() : [])
                .then(data => {
                  if (Array.isArray(data) && currentVideoId === vid && (!segments || segments.length === 0)) {
                    window.__lumaReceiveSegments({ videoId: vid, segments: data });
                  }
                })
                .catch(() => {});
            });
        };

        window.__lumaReceiveSegments = (data) => {
          try {
            if (!data) return;
            if (data.videoId && currentVideoId && data.videoId !== currentVideoId) return;
            const segs = Array.isArray(data.segments) ? data.segments : [];
            segments = segs.filter(s => s && s.segment && s.segment.length === 2);
            renderMarkers();
            checkSponsorSegments();
          } catch (e) {}
        };

        if (window.chrome && window.chrome.webview) {
          window.chrome.webview.addEventListener('message', (event) => {
            try {
              const data = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
              if (data && data.kind === 'lumaskip-segments') {
                window.__lumaReceiveSegments(data);
              }
            } catch (e) {}
          });
        }

        const getVideoId = () => {
          try {
            const url = new URL(window.location.href);
            if (url.pathname.startsWith('/shorts/')) {
              return url.pathname.split('/shorts/')[1].split('/')[0].split('?')[0];
            }
            return url.searchParams.get('v');
          } catch (e) {
            return null;
          }
        };

        const checkVideoState = () => {
          injectStyles();
          purgeAdElements();
          handleVideoAds();
          hookVideo();

          const vid = getVideoId();
          if (vid && vid !== currentVideoId) {
            currentVideoId = vid;
            segments = [];
            unskippedUUIDs.clear();
            hidePill();
            document.querySelectorAll('.lumaskip-bar-marker').forEach(m => m.remove());
            requestSegments(vid);
          }

          checkSponsorSegments();
        };

        window.__lumaCheckState = checkVideoState;

        // MutationObserver for instant synchronous ad removal
        try {
          const obs = new MutationObserver(() => {
            purgeAdElements();
            hookVideo();
            renderMarkers();
          });
          const target = document.body || document.documentElement;
          if (target) {
            obs.observe(target, { childList: true, subtree: true });
          } else {
            document.addEventListener('DOMContentLoaded', () => {
              if (document.body) obs.observe(document.body, { childList: true, subtree: true });
            });
          }
        } catch (e) {}

        // Start intervals and navigation hooks
        setInterval(checkVideoState, 80);
        setInterval(renderMarkers, 1000);
        window.addEventListener('yt-navigate-start', checkVideoState);
        window.addEventListener('yt-navigate-finish', checkVideoState);
        window.addEventListener('yt-page-data-updated', checkVideoState);
        window.addEventListener('popstate', checkVideoState);
        document.addEventListener('DOMContentLoaded', checkVideoState);
        checkVideoState();

      } catch (e) {}
    })();
    """;

    private static string ReadResource(string name) { using var stream = typeof(BrowserScripts).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Встроенный ресурс {name} не найден."); using var reader = new StreamReader(stream, Encoding.UTF8, true); return reader.ReadToEnd(); }

    /// <summary>
    /// Luma Shield: In-page cosmetic ad blocker & network protection script.
    /// Defuses ad banners, hides ad placeholders, and rejects benchmark tracking endpoints.
    /// </summary>
    public static readonly string LumaShieldScript = """
    (() => {
      try {
        if (window.__lumaShieldActive) return;

        const host = location.hostname.toLowerCase();

        // Immune roots for essential daily work tools - NEVER break audio/video/canvas/webrtc
        const immuneHosts = [
          'meet.google.com', 'classroom.google.com', 'discord.com', 'discordapp.com',
          'discord.gg', 'teams.microsoft.com', 'zoom.us', 'web.telegram.org',
          'docs.google.com', 'drive.google.com', 'accounts.google.com'
        ];
        for (let i = 0; i < immuneHosts.length; i++) {
          if (host === immuneHosts[i] || host.endsWith('.' + immuneHosts[i])) return;
        }

        // 1. Block common ad container selectors with high specificity
        try {
          const style = document.createElement('style');
          style.id = '__luma_shield_style';
          style.textContent = `
            #testBait, .ad_unit, .ad-unit, .adsbox, .ad-banner, .banner-ad, .ad-placeholder,
            [id*="ad_unit"], [class*="adsbox"], [class*="ad-banner"],
            .ytd-ad-slot-renderer, ytd-ad-slot-renderer,
            ytd-banner-promo-renderer, ytd-in-feed-ad-layout-renderer,
            #player-ads, .video-ads, .ytp-ad-module {
              display: none !important;
              visibility: hidden !important;
              height: 0 !important;
              width: 0 !important;
              pointer-events: none !important;
            }
          `;
          const addStyle = () => {
            const root = document.head || document.documentElement;
            if (root) {
              root.appendChild(style);
            } else {
              document.addEventListener('DOMContentLoaded', () => {
                (document.head || document.documentElement)?.appendChild(style);
              }, { once: true });
            }
          };
          addStyle();
        } catch (e) {}

        // 2. Bait element trap neutralization
        try {
          const origGetComputedStyle = window.getComputedStyle;
          window.getComputedStyle = function(el, pseudo) {
            const res = origGetComputedStyle.apply(this, arguments);
            if (el && (el.id === 'testBait' || (el.className && typeof el.className === 'string' && el.className.includes('adsbox')))) {
              return new Proxy(res, {
                get(target, prop) {
                  if (prop === 'display') return 'none';
                  if (prop === 'visibility') return 'hidden';
                  if (prop === 'height' || prop === 'width') return '0px';
                  const val = target[prop];
                  return typeof val === 'function' ? val.bind(target) : val;
                }
              });
            }
            return res;
          };
        } catch (e) {}

        // 3. Canvas fingerprint protection (safely cloak only tiny honeypot probing canvases)
        try {
          const origToDataURL = HTMLCanvasElement.prototype.toDataURL;
          HTMLCanvasElement.prototype.toDataURL = function() {
            if (this.width <= 220 && this.height <= 60) {
              return 'data:,';
            }
            return origToDataURL.apply(this, arguments);
          };
        } catch (e) {}

        // 4. WebGL fingerprint protection (mask GPU renderer info)
        try {
          const maskParam = (proto) => {
            if (!proto || !proto.getParameter) return;
            const origGetParam = proto.getParameter;
            proto.getParameter = function(param) {
              if (param === 0x9245) return 'Google Inc. (Google)';
              if (param === 0x9246) return 'ANGLE (Google, Vulkan 1.3.0 (SwiftShader Device (Subzero)), SwiftShader)';
              return origGetParam.apply(this, arguments);
            };
          };
          if (typeof WebGLRenderingContext !== 'undefined') maskParam(WebGLRenderingContext.prototype);
          if (typeof WebGL2RenderingContext !== 'undefined') maskParam(WebGL2RenderingContext.prototype);
        } catch (e) {}

        // 5. Audio fingerprint protection (silent buffer on small script processor probes)
        try {
          if (typeof AudioBuffer !== 'undefined') {
            const origGetChannelData = AudioBuffer.prototype.getChannelData;
            AudioBuffer.prototype.getChannelData = function(channel) {
              const data = origGetChannelData.apply(this, arguments);
              if (this.length <= 4096) {
                data.fill(0);
              }
              return data;
            };
          }
        } catch (e) {}

        // 6. Font fingerprint protection (offscreen probe spans return uniform width)
        try {
          const origOffsetWidth = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetWidth');
          if (origOffsetWidth && origOffsetWidth.get) {
            Object.defineProperty(HTMLElement.prototype, 'offsetWidth', {
              get: function() {
                if (this.style && this.style.left && parseFloat(this.style.left) < -5000) {
                  return 400;
                }
                return origOffsetWidth.get.call(this);
              }
            });
          }
        } catch (e) {}

        // 7. WebRTC leak protection (mask public IP STUN probes on untrusted sites)
        try {
          const origPC = window.RTCPeerConnection;
          if (origPC) {
            window.RTCPeerConnection = function(config) {
              const pc = new origPC(config);
              let customHandler = null;
              Object.defineProperty(pc, 'onicecandidate', {
                get: () => customHandler,
                set: (fn) => {
                  customHandler = fn;
                  pc.addEventListener('icecandidate', (e) => {
                    if (fn) {
                      try { fn(new RTCPeerConnectionIceEvent('icecandidate', { candidate: null })); } catch (ex) {}
                    }
                  });
                }
              });
              const origAddEvent = pc.addEventListener;
              pc.addEventListener = function(type, listener, options) {
                if (type === 'icecandidate' && typeof listener === 'function') {
                  const wrapped = function(e) {
                    try { listener(new RTCPeerConnectionIceEvent('icecandidate', { candidate: null })); } catch (ex) {}
                  };
                  return origAddEvent.call(this, type, wrapped, options);
                }
                return origAddEvent.apply(this, arguments);
              };
              return pc;
            };
            window.RTCPeerConnection.prototype = origPC.prototype;
          }
        } catch (e) {}

        // 8. Comprehensive blocked domain set (668 domains)
        const blocked = new Set(["2giga.link", "a.lenovo.com", "a.teads.tv", "aan.amazon.com", "aax-eu.amazon.com", "aax-us-east.amazon-adsystem.com", "aax.amazon-adsystem.com", "ablink.kochava.com", "abtasty.com", "abtest.ai", "acdn.adnxs.com", "ad.admitad.com", "ad.dm.gg", "ad.doubleclick.net", "ad.gt", "ad.lgappstv.com", "ad.linksynergy.com", "ad.mail.ru", "ad.samsungadhub.com", "ad.smartadserver.com", "ad.turn.com", "adc3-launch.adcolony.com", "addthis.com", "adfox.yandex.ru", "adfstat.yandex.ru", "adjust.com", "adlog.vivo.com", "adnxs.com", "adroll.com", "ads-api.tiktok.com", "ads-api.twitter.com", "ads-api.vivo.com", "ads-api.x.com", "ads-mas-us.vivobrowser.com", "ads-mas.vivobrowser.com", "ads-sg.tiktok.com", "ads.dailymotion.com", "ads.huawei.com", "ads.linkedin.com", "ads.microsoft.com", "ads.pinterest.com", "ads.pubmatic.com", "ads.roku.com", "ads.snapchat.com", "ads.tiktok.com", "ads.tremorhub.com", "ads.vizio.com", "ads.vk.com", "ads.x.com", "ads.yahoo.com", "ads.youtube.com", "ads30.adcolony.com", "adsapi.snapchat.com", "adserver.unityads.unity3d.com", "adservetx.media.net", "adservice.google.com", "adsfs.oppomobile.com", "adsrvr.org", "adsserver.yadro.ru", "adtago.s3.amazonaws.com", "adtech.yahooinc.com", "adv.adriver.ru", "advertising.yandex.ru", "advice-ads.s3.amazonaws.com", "adx.ads.oppomobile.com", "afcyhf.com", "afminer.com", "afs.googlesyndication.com", "aggregator.service.usercentrics.eu", "alexametrics.com", "amazon-adsystem.com", "amplify.outbrain.com", "an.facebook.com", "an.yandex.ru", "analytics-api.samsunghealthcn.com", "analytics-sg.tiktok.com", "analytics.adobe.io", "analytics.amplitude.com", "analytics.google.com", "analytics.pinterest.com", "analytics.pointdrive.linkedin.com", "analytics.tiktok.com", "analytics.twitter.com", "analyticsengine.s3.amazonaws.com", "anrdoezrs.net", "apex.go.sonobi.com", "api-adservices.apple.com", "api-js.mixpanel.com", "api.ad.xiaomi.com", "api.airbrake.io", "api.amplitude.com", "api.bcovlive.io", "api.branch.io", "api.bugsnag.com", "api.crashlytics.com", "api.flagsmith.com", "api.fpjs.io", "api.fyber.com", "api.honeybadger.io", "api.impact.com", "api.launchdarkly.com", "api.luckyorange.com", "api.mixpanel.com", "api.mouseflow.com", "api.optimizely.com", "api.partnerstack.com", "api.privacy-center.org", "api.raygun.io", "api.refersion.com", "api.rlcdn.com", "api.rollbar.com", "api.segment.io", "api.taboola.com", "api.usercentrics.eu", "api.viglink.com", "api.vungle.com", "api2.amplitude.com", "api2.branch.io", "apis.google.com", "apmebf.com", "app-measurement.com", "app.adjust.com", "app.appsflyer.com", "app.bugsnag.com", "app.convertkit.com", "app.getsentry.com", "app.launchdarkly.com", "app.link", "app.pendo.io", "app.usercentrics.eu", "applovin.com", "appmetrica.yandex.ru", "appsflyer.com", "as.casalemedia.com", "assets.mailerlite.com", "ats.rlcdn.com", "auction.unityads.unity3d.com", "audit.quantcast.mgr.consensu.org", "authedmine.com", "awin1.com", "awltovhc.com", "b.scorecardresearch.com", "badges.instagram.com", "bam-cell.nr-data.net", "bam.nr-data.net", "banners.adfox.ru", "bat.bing.com", "bcvp0rtal.com", "bdapi-ads.realmemobile.com", "bdapi-in-ads.realmemobile.com", "beacon.krxd.net", "bidder.criteo.com", "bidswitch.net", "bingads.microsoft.com", "bitcoinplus.com", "bluekai.com", "bnc.lt", "books-analytics-events.apple.com", "browser.sentry-cdn.com", "bugsnag.com", "business-api.tiktok.com", "c.amazon-adsystem.com", "c.betrad.com", "c.bing.com", "c.clarity.ms", "c.gumgum.com", "capi.connatix.com", "capture.trackjs.com", "casalemedia.com", "cd.connatix.com", "cdn-eu.configcat.com", "cdn-pci.optimizely.com", "cdn.abtest.ai", "cdn.amplitude.com", "cdn.branch.io", "cdn.configcat.com", "cdn.cookielaw.org", "cdn.doubleverify.com", "cdn.dynamicyield.com", "cdn.heapanalytics.com", "cdn.indexexchange.com", "cdn.jwplayer.com", "cdn.kargo.com", "cdn.krxd.net", "cdn.logrocket.io", "cdn.lr-ingest.com", "cdn.luckyorange.com", "cdn.mgid.com", "cdn.mouseflow.com", "cdn.mxpnl.com", "cdn.onetrust.com", "cdn.optimizely.com", "cdn.pendo.io", "cdn.permutive.com", "cdn.piwik.pro", "cdn.privacy-mgmt.com", "cdn.ravenjs.com", "cdn.raygun.io", "cdn.rollbar.com", "cdn.segment.com", "cdn.segment.io", "cdn.siftscience.com", "cdn.split.io", "cdn.spotxcdn.com", "cdn.springserve.com", "cdn.taboola.com", "cdn.teads.tv", "cdn.viglink.com", "cdn.vwo.com", "cdn2.teads.tv", "ce.lijit.com", "certify.alexametrics.com", "chartbeat.com", "ck.ads.oppomobile.com", "clarity.ms", "clevertap-prod.com", "click.em.pardot.com", "click.googleanalytics.com", "click.linksynergy.com", "click.mailerlite.com", "click.oneplus.cn", "clickadu.com", "clicks.aweber.com", "clientservices.googleapis.com", "cm.g.doubleclick.net", "cmp.quantcast.com", "cnhv.co", "coin-have.com", "coin-hive-proxy.com", "coin-hive.com", "coinerra.com", "coinhive-proxy.com", "coinhive.com", "coinhiveproxy.com", "coinimp.com", "commission-junction.com", "config.samsungads.com", "config.unityads.unity3d.com", "connect.facebook.net", "consent-api.exponential.com", "consent-pref.trustarc.com", "consent.cookiebot.com", "consent.trustarc.com", "consentcdn.cookiebot.com", "consumer.krxd.net", "contextual.media.net", "contextweb.com", "control.adjust.com", "control.kochava.com", "cookiebot.com", "cookies-data.onetrust.io", "cpu.js.org", "crwdcntrl.net", "crypto-loot.com", "crypto-loot.org", "cryptoloot.org", "cryptoloot.pro", "cs.luckyorange.net", "ct.pinterest.com", "customeriomail.com", "d.adroll.com", "d.applovin.com", "d.impactradius-event.com", "d.reddit.com", "d2wy8f7a9ursnm.cloudfront.net", "dai.google.com", "data.ads.oppomobile.com", "data.kameleoon.io", "data.mistat.india.xiaomi.com", "data.mistat.rus.xiaomi.com", "data.mistat.xiaomi.com", "decide.mixpanel.com", "dev.visualwebsiteoptimizer.com", "device-metrics-us-2.amazon.com", "device-metrics-us.amazon.com", "didomi.io", "dis.criteo.com", "doubleverify.com", "dpbolvw.net", "dwin1.com", "eb2.3lift.com", "edge.api.flagsmith.com", "edge.fullstory.com", "email-open.dripemail2.com", "email.getresponse.com", "email.mailgun.net", "emjcd.com", "events.launchdarkly.com", "events.reddit.com", "events.redditmedia.com", "events.split.io", "events3alt.adcolony.com", "exoclick.com", "extmaps-api.yandex.net", "f.vimeocdn.com", "facebook.net", "fastlane.rubiconproject.com", "fastpool.xyz", "featuregates.org", "fingerprintjs.com", "firebase-settings.crashlytics.com", "firebaselogging.googleapis.com", "fls-na.amazon-adsystem.com", "fpjs.io", "freshmarketer.com", "fresnel.vimeocdn.com", "ftjcfx.com", "fullstory.com", "fundingchoicesmessages.google.com", "fw.adsafeprotected.com", "g.jwpsrv.com", "gads.pubmatic.com", "gdeslon.ru", "gdpr.trustarc.com", "gemini.yahoo.com", "geolocation.onetrust.com", "getdrip.com", "getresponse.com", "go.skimresources.com", "google-analytics.com", "googleads.g.doubleclick.net", "googleadservices.com", "googletagmanager.com", "graph.facebook.com", "graph.instagram.com", "greatis.com", "grs.hicloud.com", "hashforcash.us", "hashing.win", "hbopenbid.pubmatic.com", "heapanalytics.com", "hotjar.com", "htlb.casalemedia.com", "i.instagram.com", "i.liadm.com", "iadsdk.apple.com", "ib.adnxs.com", "id5-sync.com", "idsync.rlcdn.com", "image2.pubmatic.com", "image6.pubmatic.com", "images.taboola.com", "imasdk.googleapis.com", "impact.com", "in.hotjar.com", "indexexchange.com", "info.lgsmartad.com", "informer.yandex.ru", "ingest.sentry.io", "init.supersonicads.com", "inmobi.com", "insight.adsrvr.org", "insightexpressai.com", "insights.hotjar.com", "iot-eu-logser.realme.com", "iot-logser.realme.com", "ir-na.amazon-adsystem.com", "ironsource.mobi", "is.com", "jdoqocy.com", "js-agent.newrelic.com", "js.driftt.com", "js.honeybadger.io", "jsecoin.com", "juicyads.com", "kameleoon.eu", "kochava.com", "kqzyfj.com", "launches.appsflyer.com", "lduhtrp.net", "liadm.com", "liftoff.io", "list-manage.com", "live.chartboost.com", "log.byteoversea.com", "log.outbrain.com", "log.pinterest.com", "logbak.hicloud.com", "logs.logrocket.io", "logs.roku.com", "logservice.hicloud.com", "logservice1.hicloud.com", "logx.optimizely.com", "lt.activecampaign.com", "lt.pardot.com", "luckyorange.com", "m.doubleclick.net", "mads-eu.amazon.com", "mads.amazon.com", "mailchimp.com", "mandrillapp.com", "match.adsrvr.org", "matomo.cloud", "mc.yandex.com", "mc.yandex.ru", "media.net", "mediavisor.doubleclick.net", "metrics.brightcove.com", "metrics.data.hicloud.com", "metrics.icloud.com", "metrics.mzstatic.com", "metrics2.data.hicloud.com", "metrika.yandex.com", "metrika.yandex.ru", "mgid.com", "minemytraffic.com", "mineralt.io", "minergate.com", "minero-proxy.com", "minero.cc", "mixpanel.com", "mobile.launchdarkly.com", "mon.byteoversea.com", "monerominer.rocks", "mouseflow.com", "ms.applovin.com", "mssl.fwmrm.net", "munchkin.marketo.net", "ngfts.lge.com", "nmetrics.samsung.com", "notes-analytics-events.apple.com", "notifier.airbrake.io", "notify.bugsnag.com", "nr.taboola.com", "o0.ingest.sentry.io", "o2.mouseflow.com", "oca.telemetry.microsoft.com", "odb.outbrain.com", "offerwall.yandex.net", "onclickads.net", "onetag-sys.com", "open.activecampaign.com", "open.convertkit.com", "open.oneplus.net", "openx.net", "optanon.blob.core.windows.net", "outbrain.com", "outcome-ssp.supersonicads.com", "pagead.l.doubleclick.net", "pagead2.googleadservices.com", "pagead2.googlesyndication.com", "pagead2.googlevideo.com", "pangleglobal.com", "partnerstack.com", "pendo.io", "permutive.com", "pi.pardot.com", "pippio.com", "piwik.pro", "pixel.adsafeprotected.com", "pixel.advertising.com", "pixel.aweber.com", "pixel.facebook.com", "pixel.mailchimp.com", "pixel.mathtag.com", "pixel.quantserve.com", "pixel.quora.com", "pixel.rubiconproject.com", "pixel.wp.com", "platform.instagram.com", "platform.linkedin.com", "platform.twitter.com", "plus.google.com", "popads.net", "popcash.net", "popmyads.com", "ppoi.org", "prd.jwpltx.com", "prebid.adnxs.com", "privacy-mgmt.com", "privacymanager.io", "prod.uidapi.com", "propellerads.com", "propellerclick.com", "prox.sendinblue.com", "pubads.g.doubleclick.net", "pubmatic.com", "px.ads.linkedin.com", "px.srvcs.tumblr.com", "qksrv.net", "quantcast.com", "quantcast.mgr.consensu.org", "quantserve.com", "r.logrocket.io", "r.skimresources.com", "raygun.io", "rcm-na.amazon-adsystem.com", "realtime.luckyorange.com", "redditmedia.com", "redirect.viglink.com", "redirector.googlevideo.com", "redirector.skimresources.com", "refersion.com", "region1.google-analytics.com", "reports.crashlytics.com", "results.optimizely.com", "rlcdn.com", "rollbar.com", "rs.fullstory.com", "rt.applovin.com", "rtb.openx.net", "rubiconproject.com", "s.adroll.com", "s.clarity.ms", "s.innovid.com", "s.skimresources.com", "s.youtube.com", "s0.2mdn.net", "s7.addthis.com", "samsung-com.112.2o7.net", "samsungads.com", "sb.scorecardresearch.com", "sc-analytics.appspot.com", "sc-static.net", "scorecardresearch.com", "script.hotjar.com", "sdk.appsflyer.com", "sdk.privacy-center.org", "sdk.singular.net", "sdk.split.io", "sdkconfig.ad.intl.xiaomi.com", "sdkconfig.ad.xiaomi.com", "search.spotxchange.com", "secure.adnxs.com", "securepubads.g.doubleclick.net", "sendgrid.net", "sentry.io", "servicer.mgid.com", "sessions.bugsnag.com", "settings-win.data.microsoft.com", "shareasale-analytics.com", "shareasale.com", "sharethis.com", "sharethrough.com", "shrsl.com", "sibautomation.com", "siftscience.com", "singular.net", "siteimproveanalytics.com", "skimresources.com", "smartclip.com", "smartclip.net", "smartyads.com", "smetrics.samsung.com", "snap.licdn.com", "sourcepoint.mgr.consensu.org", "spotxchange.com", "ssl.google-analytics.com", "ssl.p.jwpcdn.com", "sslwidget.criteo.com", "st-widget.s3.amazonaws.com", "statdynamic.com", "static.addtoany.com", "static.ads-twitter.com", "static.adsafeprotected.com", "static.chartbeat.com", "static.criteo.net", "static.doubleclick.net", "static.hotjar.com", "static.kameleoon.com", "static.media.net", "staticxx.facebook.com", "stats.g.doubleclick.net", "stats.wp.com", "statsigapi.net", "stream.launchdarkly.com", "streaming.split.io", "supportmetrics.apple.com", "survey.watson.microsoft.com", "surveys.hotjar.com", "sync.kargo.com", "sync.mathtag.com", "sync.outbrain.com", "sync.search.spotxchange.com", "sync.springserve.com", "sync.teads.tv", "syndication.twitter.com", "t.appsflyer.com", "t.co", "t.hubspotemail.net", "t.sendinblue.com", "t.skimresources.com", "t.teads.tv", "taboola.com", "tagmanager.google.com", "tags.tiqcdn.com", "tapad.com", "teads.tv", "telemetry.microsoft.com", "thetradedesk.com", "tkqlhce.com", "tlx.3lift.com", "tools.mouseflow.com", "top-fwz1.mail.ru", "tpc.googlesyndication.com", "tqlkg.com", "tr.facebook.com", "tr.outbrain.com", "tr.snapchat.com", "track.constantcontact.com", "track.customer.io", "track.hubspot.com", "track.linksynergy.com", "track.mailerlite.com", "track.mailgun.org", "trackcmp.net", "tracking.miui.com", "tracking.rus.miui.com", "trackjs.com", "trafficjunky.net", "trc.taboola.com", "tremorhub.com", "trends.revcontent.com", "trk.pinterest.com", "try.abtasty.com", "tvinteractive.tv", "tvpixel.com", "u.openx.net", "unagi-na.amazon.com", "unityads.unity3d.com", "upload.luckyorange.net", "us-ads.openx.net", "us.edge.rms.media.net", "us.ibs.lgappstv.com", "us.info.lgsmartad.com", "usage.trackjs.com", "usersync.krxd.net", "vars.hotjar.com", "vc.hotjar.io", "vid.connatix.com", "vid.springserve.com", "video-ads.twitter.com", "vk.com/rtrg", "vortex-win.data.microsoft.com", "vortex.data.microsoft.com", "vungle.com", "vwo.com", "w1.luckyorange.com", "watson.telemetry.microsoft.com", "wd.adcolony.com", "weather-analytics-events.apple.com", "webmine.cz", "webminepool.com", "webminerpool.com", "webview.unityads.unity3d.com", "webvisor.com", "widget.intercom.io", "widgets.outbrain.com", "widgets.pinterest.com", "wrapper-api.sp-prod.net", "ws-na.amazon-adsystem.com", "www.anrdoezrs.net", "www.awin1.com", "www.clarity.ms", "www.coinimp.com", "www.dpbolvw.net", "www.google-analytics.com", "www.googleadservices.com", "www.googletagmanager.com", "www.tkqlhce.com", "www3.smartadserver.com", "wzrkt.com", "xmr.omine.org", "xp.apple.com", "youtubei.googleapis.com", "yumenetworks.com", "z-na.amazon-adsystem.com", "zenaps.com"]);

        const isDomainBlocked = (url) => {
          try {
            if (!url) return false;
            const u = new URL(url, location.href);
            const h = u.hostname.toLowerCase();
            if (blocked.has(h)) return true;
            const parts = h.split('.');
            for (let i = 1; i < parts.length - 1; i++) {
              const sub = parts.slice(i).join('.');
              if (blocked.has(sub)) return true;
            }
            if (h.endsWith('vk.com') && u.pathname.startsWith('/rtrg')) return true;
            if (h.endsWith('doubleclick.net') || h.endsWith('googlesyndication.com') || h.endsWith('googleadservices.com')) return true;
          } catch (e) {}
          return false;
        };

        // 9. Fetch Interceptor
        try {
          const origFetch = window.fetch;
          if (typeof origFetch === 'function') {
            window.fetch = function(input, init) {
              let u = (typeof input === 'string') ? input : (input && input.url ? input.url : '');
              if (isDomainBlocked(u)) {
                try {
                  if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage({ kind: 'luma-shield-blocked' });
                  }
                } catch (e) {}
                return Promise.reject(new TypeError('Failed to fetch (Blocked by Luma Shield)'));
              }
              return origFetch.apply(this, arguments);
            };
          }
        } catch (e) {}

        // 10. XMLHttpRequest Interceptor
        try {
          const origOpen = XMLHttpRequest.prototype.open;
          XMLHttpRequest.prototype.open = function(method, url) {
            this.__lumaUrl = url;
            return origOpen.apply(this, arguments);
          };
          const origSend = XMLHttpRequest.prototype.send;
          XMLHttpRequest.prototype.send = function() {
            if (isDomainBlocked(this.__lumaUrl)) {
              try {
                if (window.chrome && window.chrome.webview) {
                  window.chrome.webview.postMessage({ kind: 'luma-shield-blocked' });
                }
              } catch (e) {}
              setTimeout(() => {
                try {
                  this.dispatchEvent(new ProgressEvent('error'));
                } catch (e) {}
              }, 0);
              return;
            }
            return origSend.apply(this, arguments);
          };
        } catch (e) {}

        // 11. HTMLImageElement / Image Interceptor
        try {
          const origImage = window.Image;
          window.Image = function(width, height) {
            const img = new origImage(width, height);
            let srcVal = '';
            Object.defineProperty(img, 'src', {
              get() { return srcVal; },
              set(val) {
                srcVal = val;
                if (isDomainBlocked(val)) {
                  try {
                    if (window.chrome && window.chrome.webview) {
                      window.chrome.webview.postMessage({ kind: 'luma-shield-blocked' });
                    }
                  } catch (e) {}
                  setTimeout(() => {
                    try {
                      img.dispatchEvent(new Event('error'));
                      if (typeof img.onerror === 'function') img.onerror(new Event('error'));
                    } catch (e) {}
                  }, 0);
                  return;
                }
                img.setAttribute('src', val);
              }
            });
            return img;
          };
          window.Image.prototype = origImage.prototype;
        } catch (e) {}

        window.__lumaShieldActive = true;
      } catch (e) {}
    })();
    """;

    // =========================================================================
    // POWER FEATURES: Media Sniffer, Tab Volume Booster, Form Recovery & Inline Translate
    // =========================================================================
    public const string MediaSnifferScript = """
    (() => {
      if (window.__lumaMediaSniffer) return;
      window.__lumaMediaSniffer = true;

      function reportMedia() {
        try {
          const media = [];
          document.querySelectorAll('video, audio, source').forEach(el => {
            const src = el.currentSrc || el.src;
            if (src && !src.startsWith('data:')) {
              media.push({
                url: src,
                type: el.tagName.toLowerCase() === 'audio' ? 'audio' : 'video',
                title: document.title || 'media'
              });
            }
          });

          const isYt = location.hostname.includes('youtube.com') || location.hostname.includes('youtu.be');
          if (isYt) {
            const v = new URLSearchParams(location.search).get('v') || (location.pathname.startsWith('/shorts/') ? location.pathname.split('/')[2] : null);
            if (v || location.pathname.startsWith('/watch') || location.pathname.startsWith('/shorts/')) {
              media.push({ url: location.href, type: 'video', title: document.title || 'YouTube Video', isYt: true });
            }
          }

          if (location.hostname.includes('filmix') || location.hostname.includes('rutube') || location.hostname.includes('vk.com')) {
            if (document.querySelector('video, iframe[src*="video"], iframe[src*="filmix"], iframe[src*="player"]')) {
              media.push({ url: location.href, type: 'video', title: document.title || 'Video Stream' });
            }
          }

          if (media.length > 0) {
            window.chrome.webview.postMessage({ kind: 'media-detected', media: media });
          }
        } catch(e) {}
      }

      function injectYtDownloadButton() {
        try {
          if (!location.hostname.includes('youtube.com')) return;
          const container = document.querySelector('#actions #top-level-buttons-computed, ytd-menu-renderer #top-level-buttons-computed, #above-the-fold #top-level-buttons-computed');
          if (!container) return;

          if (!document.getElementById('luma-yt-dl-btn')) {
            const btn = document.createElement('button');
            btn.id = 'luma-yt-dl-btn';
            btn.innerHTML = '<span style="font-size:15px;margin-right:4px;">📥</span><span>Скачать</span>';
            btn.style.cssText = 'background: #27272a; color: #f4f4f5; border: 1px solid rgba(255,255,255,0.18); border-radius: 18px; height: 36px; padding: 0 16px; font-weight: 600; font-size: 13.5px; cursor: pointer; display: inline-flex; align-items: center; margin-left: 8px; vertical-align: middle; z-index: 9999; box-shadow: 0 2px 8px rgba(0,0,0,0.3);';
            btn.onmouseenter = () => { btn.style.background = '#3f3f46'; };
            btn.onmouseleave = () => { btn.style.background = '#27272a'; };
            btn.onclick = (e) => {
              e.preventDefault();
              e.stopPropagation();
              window.chrome.webview.postMessage({ kind: 'open-media-sniffer-menu' });
            };
            container.appendChild(btn);
          }

          if (!document.getElementById('luma-yt-summary-btn')) {
            const sumBtn = document.createElement('button');
            sumBtn.id = 'luma-yt-summary-btn';
            sumBtn.innerHTML = '<span style="font-size:15px;margin-right:4px;">✨</span><span>Пересказ</span>';
            sumBtn.style.cssText = 'background: rgba(167, 139, 250, 0.18); color: #e9d5ff; border: 1px solid rgba(167, 139, 250, 0.4); border-radius: 18px; height: 36px; padding: 0 16px; font-weight: 600; font-size: 13.5px; cursor: pointer; display: inline-flex; align-items: center; margin-left: 8px; vertical-align: middle; z-index: 9999; box-shadow: 0 2px 8px rgba(0,0,0,0.3);';
            sumBtn.onmouseenter = () => { sumBtn.style.background = 'rgba(167, 139, 250, 0.3)'; };
            sumBtn.onmouseleave = () => { sumBtn.style.background = 'rgba(167, 139, 250, 0.18)'; };
            sumBtn.onclick = (e) => {
              e.preventDefault();
              e.stopPropagation();
              window.chrome.webview.postMessage({ kind: 'open-summarize-menu' });
            };
            container.appendChild(sumBtn);
          }
        } catch(e) {}
      }

      setInterval(() => {
        reportMedia();
        injectYtDownloadButton();
      }, 2500);

      window.addEventListener('yt-navigate-finish', () => {
        setTimeout(() => { reportMedia(); injectYtDownloadButton(); }, 600);
      });

      reportMedia();
      injectYtDownloadButton();
    })();
    """;

    public static string TabVolumeBoosterScript(double factor) => $$"""
    (() => {
      try {
        const factor = {{factor.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}};
        if (!window.__lumaAudioCtx) {
          const AudioCtx = window.AudioContext || window.webkitAudioContext;
          if (!AudioCtx) return;
          window.__lumaAudioCtx = new AudioCtx();
        }
        if (window.__lumaAudioCtx.state === 'suspended') {
          window.__lumaAudioCtx.resume();
        }
        if (!window.__lumaGainNodes) window.__lumaGainNodes = new Map();
        const mediaElements = Array.from(document.querySelectorAll('audio, video'));
        mediaElements.forEach(el => {
          try {
            if (!window.__lumaGainNodes.has(el)) {
              const srcNode = window.__lumaAudioCtx.createMediaElementSource(el);
              const gainNode = window.__lumaAudioCtx.createGain();
              srcNode.connect(gainNode);
              gainNode.connect(window.__lumaAudioCtx.destination);
              window.__lumaGainNodes.set(el, gainNode);
            }
            const g = window.__lumaGainNodes.get(el);
            if (g) g.gain.value = factor;
          } catch(err) {}
        });
      } catch(e) {}
    })();
    """;

    public const string FormRecoveryScript = """
    (() => {
      if (window.__lumaFormRecovery) return;
      window.__lumaFormRecovery = true;
      const KEY = '__luma_form_' + location.host + location.pathname;
      function save() {
        try {
          const data = {};
          let count = 0;
          document.querySelectorAll('textarea, input[type=text], input[type=search], [contenteditable=true]').forEach((el, idx) => {
            const val = el.isContentEditable ? el.innerText : el.value;
            if (val && val.trim().length > 3) {
              const id = el.id || el.name || ('idx_' + idx);
              data[id] = val;
              count++;
            }
          });
          if (count > 0) sessionStorage.setItem(KEY, JSON.stringify(data));
        } catch(e) {}
      }
      function restore() {
        try {
          const raw = sessionStorage.getItem(KEY);
          if (!raw) return;
          const data = JSON.parse(raw);
          document.querySelectorAll('textarea, input[type=text], input[type=search], [contenteditable=true]').forEach((el, idx) => {
            const id = el.id || el.name || ('idx_' + idx);
            if (data[id]) {
              const cur = el.isContentEditable ? el.innerText : el.value;
              if (!cur || cur.trim().length === 0) {
                if (el.isContentEditable) el.innerText = data[id];
                else el.value = data[id];
                el.dispatchEvent(new Event('input', { bubbles: true }));
              }
            }
          });
        } catch(e) {}
      }
      document.addEventListener('input', save, { passive: true });
      setTimeout(restore, 1000);
    })();
    """;

}
