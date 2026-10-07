// Luma AutoLesson (Автоурок) for Google Classroom & Google Meet
(() => {
  try {
    const host = (location.hostname || '').toLowerCase();
    const isClassroom = host.includes('classroom.google.com');
    const isMeet = host.includes('meet.google.com');
    if (!isClassroom && !isMeet) return;

    // ────────────────────── LUCIDE ICONS (LUMA STYLE) ──────────────────────
    function lucideIcon(name, size = 18, color = 'currentColor') {
      const icons = {
        cap: '<path d="M21.42 10.922a1 1 0 0 0-.019-1.838L12.83 5.18a2 2 0 0 0-1.66 0L2.6 9.08a1 1 0 0 0 0 1.832l8.57 3.908a2 2 0 0 0 1.66 0z"/><path d="M22 10v6"/><path d="M6 12.5V16a6 3 0 0 0 12 0v-3.5"/>',
        calendar: '<rect width="18" height="18" x="3" y="4" rx="2"/><path d="M16 2v4"/><path d="M8 2v4"/><path d="M3 10h18"/>',
        sliders: '<line x1="4" x2="4" y1="21" y2="14"/><line x1="4" x2="4" y1="10" y2="3"/><line x1="12" x2="12" y1="21" y2="12"/><line x1="12" x2="12" y1="8" y2="3"/><line x1="20" x2="20" y1="21" y2="16"/><line x1="20" x2="20" y1="12" y2="3"/><line x1="1" x2="7" y1="14" y2="14"/><line x1="9" x2="15" y1="8" y2="8"/><line x1="17" x2="23" y1="16" y2="16"/>',
        fileText: '<path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"/><path d="M14 2v4a2 2 0 0 0 2 2h4"/><path d="M10 9H8"/><path d="M16 13H8"/><path d="M16 17H8"/>',
        flask: '<path d="M10 2v7.31L4.36 20.3a1 1 0 0 0 .86 1.45h13.56a1 1 0 0 0 .86-1.45L14 9.31V2"/><path d="M8.5 2h7"/><path d="M7 16h10"/>',
        video: '<path d="m16 13 5.223 3.482a.5.5 0 0 0 .777-.416V7.87a.5.5 0 0 0-.752-.432L16 10.5"/><rect x="2" y="6" width="14" height="12" rx="2"/>',
        clock: '<circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>',
        plus: '<path d="M5 12h14"/><path d="M12 5v14"/>',
        trash: '<path d="M3 6h18"/><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6"/><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2"/><line x1="10" x2="10" y1="11" y2="17"/><line x1="14" x2="14" y1="11" y2="17"/>',
        save: '<path d="M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z"/><path d="M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7"/><path d="M7 3v4a1 1 0 0 0 1 1h7"/>',
        x: '<path d="M18 6 6 18"/><path d="m6 6 12 12"/>',
        search: '<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>',
        check: '<path d="M20 6 9 17l-5-5"/>',
        sparkles: '<path d="m12 3-1.9 5.8a2 2 0 0 1-1.3 1.3L3 12l5.8 1.9a2 2 0 0 1 1.3 1.3L12 21l1.9-5.8a2 2 0 0 1 1.3-1.3L21 12l-5.8-1.9a2 2 0 0 1-1.3-1.3Z"/>',
        radio: '<circle cx="12" cy="12" r="2"/><path d="M16.24 7.76a6 6 0 0 1 0 8.49m-8.48-.01a6 6 0 0 1 0-8.49m11.31-2.82a10 10 0 0 1 0 14.14m-14.14 0a10 10 0 0 1 0-14.14"/>'
      };
      const body = icons[name] || icons.cap;
      return `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="${color}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="flex-shrink:0;vertical-align:middle;">${body}</svg>`;
    }

    function setSafeHTML(el, html) {
      if (window.trustedTypes && window.trustedTypes.createPolicy) {
        try {
          window.__lumaPolicy ??= window.trustedTypes.createPolicy('lumaPolicy', {
            createHTML: s => s
          });
          el.innerHTML = window.__lumaPolicy.createHTML(html);
          return;
        } catch (e) {}
      }
      try {
        el.innerHTML = html;
      } catch (e) {
        try {
          const parser = new DOMParser();
          const doc = parser.parseFromString(html, 'text/html');
          while (el.firstChild) el.removeChild(el.firstChild);
          while (doc.body && doc.body.firstChild) el.appendChild(doc.body.firstChild);
        } catch (_) {}
      }
    }

    // ────────────────────── SURFACE BUTTON (GUARANTEED TOP-LEVEL MOUNT) ──────────────────────
    function ensureSurfaceButton() {
      if (!isClassroom) return;
      try {
        const root = document.body || document.documentElement;
        if (!root) return;

        // Clean up any old buggy sidebar injected items to prevent duplication
        try {
          const oldSidebar = document.querySelectorAll('[data-luma-autourok]');
          oldSidebar.forEach(el => el.remove());
        } catch (_) {}

        let btn = document.getElementById('luma-autourok-surface-btn');
        if (!btn) {
          btn = document.createElement('div');
          btn.id = 'luma-autourok-surface-btn';
          btn.className = 'luma-autourok-surface-btn';
          btn.setAttribute('role', 'button');
          btn.setAttribute('tabindex', '0');
          btn.title = 'Luma Автоурок — расписание и авто-вход на уроки';

          const isDark = Boolean(
            (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) ||
            document.documentElement?.classList?.contains?.('dark') ||
            document.body?.classList?.contains?.('dark')
          );

          btn.style.cssText = [
            'position: fixed !important',
            'top: 72px !important',
            'right: 20px !important',
            'z-index: 2147483647 !important',
            'display: inline-flex !important',
            'align-items: center !important',
            'gap: 8px !important',
            'padding: 6px 14px 6px 10px !important',
            'height: 36px !important',
            `background: ${isDark ? '#16171b' : '#ffffff'} !important`,
            `color: ${isDark ? '#e2e2ea' : '#1e1f24'} !important`,
            `border: 1px solid ${isDark ? '#2d2e37' : '#e1e3ea'} !important`,
            'border-radius: 18px !important',
            'box-shadow: 0 2px 10px rgba(0,0,0,0.2) !important',
            'cursor: pointer !important',
            "font-family: 'Segoe UI Variable Display', 'Segoe UI', Inter, Roboto, sans-serif !important",
            'font-size: 13px !important',
            'font-weight: 550 !important',
            'line-height: 1 !important',
            'user-select: none !important',
            'transition: all 0.16s ease !important',
            'backdrop-filter: blur(8px) !important',
            'text-decoration: none !important',
            'box-sizing: border-box !important',
            'visibility: visible !important',
            'opacity: 1 !important',
            'pointer-events: auto !important',
            'margin: 0 !important'
          ].join('; ') + ';';

          const iconSpan = document.createElement('span');
          iconSpan.style.cssText = 'display:inline-flex;align-items:center;color:#7C5CE4;flex-shrink:0;';
          setSafeHTML(iconSpan, lucideIcon('cap', 18, '#7C5CE4'));
          btn.appendChild(iconSpan);

          const label = document.createElement('span');
          label.textContent = 'Автоурок';
          label.style.cssText = 'font-weight:600 !important;font-size:13px !important;color:inherit !important;display:inline-block !important;letter-spacing:0.1px;';
          btn.appendChild(label);

          const dot = document.createElement('span');
          dot.title = 'Активен';
          dot.style.cssText = 'width:6px !important;height:6px !important;border-radius:50% !important;background:#22c55e !important;box-shadow:0 0 5px #22c55e !important;flex-shrink:0 !important;display:inline-block !important;';
          btn.appendChild(dot);

          btn.onclick = (e) => {
            e.preventDefault();
            e.stopPropagation();
            toggleModal(true);
          };
          btn.onkeydown = (e) => {
            if (e.key === 'Enter' || e.key === ' ') {
              e.preventDefault();
              toggleModal(true);
            }
          };

          root.appendChild(btn);
        } else {
          if (document.body && btn.parentElement !== document.body) {
            document.body.appendChild(btn);
          }
        }
      } catch (e) {
        console.warn('[Luma AutoLesson] ensureSurfaceButton error:', e);
      }
    }

    // Continuously enforce surface button on classroom
    if (isClassroom) {
      ensureSurfaceButton();
      if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', ensureSurfaceButton);
      }
      window.addEventListener('load', ensureSurfaceButton);
      setInterval(ensureSurfaceButton, 500);
      try {
        const obs = new MutationObserver(() => ensureSurfaceButton());
        const t = document.documentElement || document.body;
        if (t) obs.observe(t, { childList: true, subtree: true });
      } catch (_) {}
    }

    if (window.__lumaAutourokLoaded) {
      if (isClassroom) ensureSurfaceButton();
      return;
    }
    window.__lumaAutourokLoaded = true;

    // ────────────────────── CONFIG & SCHEDULE ──────────────────────
    const STORAGE_KEY = 'luma_autourok_config_v2';
    const JOINED_KEY = 'luma_autourok_joined_cache';

    const defaultSchedule = {
      1: [
        { subject: 'Алгебра', start: '08:30', end: '09:15' },
        { subject: 'Геометрия', start: '09:25', end: '10:10' },
        { subject: 'Физика', start: '10:20', end: '11:05' },
        { subject: 'Информатика', start: '11:20', end: '12:05' },
        { subject: 'История', start: '12:15', end: '13:00' },
      ],
      2: [
        { subject: 'Украинский язык', start: '08:30', end: '09:15' },
        { subject: 'Зарубежная литература', start: '09:25', end: '10:10' },
        { subject: 'Английский', start: '10:20', end: '11:05' },
        { subject: 'Биология', start: '11:20', end: '12:05' },
        { subject: 'Химия', start: '12:15', end: '13:00' },
      ],
      3: [
        { subject: 'Алгебра', start: '08:30', end: '09:15' },
        { subject: 'Физика', start: '09:25', end: '10:10' },
        { subject: 'География', start: '10:20', end: '11:05' },
        { subject: 'Английский', start: '11:20', end: '12:05' },
        { subject: 'Физкультура', start: '12:15', end: '13:00' },
      ],
      4: [
        { subject: 'Геометрия', start: '08:30', end: '09:15' },
        { subject: 'История', start: '09:25', end: '10:10' },
        { subject: 'Химия', start: '10:20', end: '11:05' },
        { subject: 'Информатика', start: '11:20', end: '12:05' },
        { subject: 'Зарубежная литература', start: '12:15', end: '13:00' },
      ],
      5: [
        { subject: 'Украинский язык', start: '08:30', end: '09:15' },
        { subject: 'Английский', start: '09:25', end: '10:10' },
        { subject: 'Биология', start: '10:20', end: '11:05' },
        { subject: 'Правоведение', start: '11:20', end: '12:05' },
        { subject: 'Искусство', start: '12:15', end: '13:00' },
      ],
      6: [],
      0: []
    };

    function loadConfig() {
      try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (raw) {
          const cfg = JSON.parse(raw);
          if (!cfg.schedule) cfg.schedule = defaultSchedule;
          if (cfg.muteMicAndCam === undefined) cfg.muteMicAndCam = true;
          if (cfg.autoJoin === undefined) cfg.autoJoin = true;
          if (cfg.enabled === undefined) cfg.enabled = true;
          return cfg;
        }
      } catch (_) {}
      return {
        enabled: true,
        autoJoin: true,
        leadMinutes: 2,
        muteMicAndCam: true,
        notify: true,
        schedule: defaultSchedule
      };
    }

    function saveConfig(cfg) {
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(cfg));
      } catch (_) {}
    }

    let config = loadConfig();

    // ────────────────────── CLEAN LUMA STYLES (NO NEON) ──────────────────────
    function injectStyles() {
      if (document.getElementById('luma-autourok-styles')) return;
      const st = document.createElement('style');
      st.id = 'luma-autourok-styles';
      const cssContent = `
        @keyframes lumaOverlayIn {
          from { opacity: 0; backdrop-filter: blur(0px); }
          to { opacity: 1; backdrop-filter: blur(10px); }
        }
        @keyframes lumaModalIn {
          from { opacity: 0; transform: scale(0.95) translateY(12px); }
          to { opacity: 1; transform: scale(1) translateY(0); }
        }

        .luma-autourok-overlay {
          position: fixed;
          inset: 0;
          background: rgba(8, 9, 12, 0.72);
          backdrop-filter: blur(10px);
          -webkit-backdrop-filter: blur(10px);
          z-index: 2147483647;
          display: flex;
          align-items: center;
          justify-content: center;
          padding: 20px;
          animation: lumaOverlayIn 0.2s cubic-bezier(0.16, 1, 0.3, 1) forwards;
          font-family: 'Segoe UI Variable Text', 'Segoe UI', Inter, -apple-system, BlinkMacSystemFont, Roboto, sans-serif;
          box-sizing: border-box;
        }

        .luma-autourok-modal {
          background: #111216;
          color: #e5e5ed;
          width: 560px;
          max-width: 96vw;
          max-height: 88vh;
          border-radius: 20px;
          border: 1px solid #252630;
          box-shadow: 0 24px 60px rgba(0, 0, 0, 0.65), 0 0 1px rgba(255, 255, 255, 0.1);
          overflow: hidden;
          display: flex;
          flex-direction: column;
          animation: lumaModalIn 0.22s cubic-bezier(0.16, 1, 0.3, 1) forwards;
          box-sizing: border-box;
        }

        .luma-autourok-header {
          padding: 16px 20px;
          display: flex;
          align-items: center;
          justify-content: space-between;
          border-bottom: 1px solid #1f2028;
          background: #14151a;
          box-sizing: border-box;
        }

        .luma-autourok-title-group {
          display: flex;
          align-items: center;
          gap: 10px;
        }

        .luma-autourok-title {
          font-size: 16px;
          font-weight: 650;
          color: #ffffff;
          margin: 0;
          letter-spacing: -0.2px;
        }

        .luma-autourok-header-actions {
          display: flex;
          align-items: center;
          gap: 10px;
        }

        .luma-autourok-status-badge {
          display: inline-flex;
          align-items: center;
          gap: 6px;
          font-size: 12px;
          font-weight: 550;
          padding: 5px 10px;
          border-radius: 20px;
          background: rgba(34, 197, 94, 0.12);
          color: #4ade80;
          border: 1px solid rgba(34, 197, 94, 0.25);
          cursor: pointer;
          user-select: none;
          transition: all 0.15s;
        }
        .luma-autourok-status-badge.paused {
          background: rgba(239, 68, 68, 0.12);
          color: #f87171;
          border-color: rgba(239, 68, 68, 0.25);
        }

        .luma-autourok-close {
          background: transparent;
          border: 1px solid transparent;
          cursor: pointer;
          color: #8a8c9e;
          width: 32px;
          height: 32px;
          border-radius: 8px;
          display: flex;
          align-items: center;
          justify-content: center;
          transition: all 0.15s;
        }
        .luma-autourok-close:hover {
          background: #20212a;
          border-color: #2b2c38;
          color: #ffffff;
        }

        .luma-autourok-content {
          padding: 18px 20px;
          overflow-y: auto;
          flex: 1;
        }

        /* Hero Card (Luma Graphite Style) */
        .luma-autourok-hero {
          background: #17181f;
          border: 1px solid #282935;
          border-radius: 14px;
          padding: 16px 18px;
          margin-bottom: 16px;
        }

        .luma-autourok-hero-top {
          display: flex;
          justify-content: space-between;
          align-items: center;
          font-size: 11.5px;
          font-weight: 600;
          text-transform: uppercase;
          letter-spacing: 0.5px;
          color: #7C5CE4;
          margin-bottom: 6px;
        }

        .luma-autourok-hero-subject {
          font-size: 20px;
          font-weight: 700;
          color: #ffffff;
          margin: 0 0 6px 0;
          letter-spacing: -0.3px;
        }

        .luma-autourok-hero-time {
          font-size: 13px;
          color: #8c8ea0;
          margin-bottom: 14px;
        }

        .luma-autourok-btn-join {
          width: 100%;
          background: #7C5CE4;
          color: #ffffff;
          border: none;
          padding: 10px 16px;
          border-radius: 10px;
          font-size: 13.5px;
          font-weight: 600;
          cursor: pointer;
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 8px;
          transition: background 0.16s, transform 0.12s;
        }
        .luma-autourok-btn-join:hover {
          background: #6D4BD6;
        }
        .luma-autourok-btn-join:active {
          transform: scale(0.98);
        }

        /* Tabs Navigation */
        .luma-autourok-tabs {
          display: flex;
          gap: 6px;
          background: #15161c;
          padding: 4px;
          border-radius: 12px;
          border: 1px solid #23242e;
          margin-bottom: 16px;
        }

        .luma-autourok-tab {
          flex: 1;
          padding: 7px 10px;
          text-align: center;
          background: transparent;
          border: 1px solid transparent;
          border-radius: 8px;
          cursor: pointer;
          font-size: 12.5px;
          font-weight: 500;
          color: #828496;
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 6px;
          transition: all 0.15s;
        }
        .luma-autourok-tab:hover {
          color: #d1d2df;
        }
        .luma-autourok-tab.active {
          background: #20212b;
          border-color: #2e303d;
          color: #ffffff;
          font-weight: 600;
        }

        /* Day Pills */
        .luma-autourok-days {
          display: flex;
          gap: 6px;
          margin-bottom: 14px;
        }

        .luma-autourok-day-pill {
          flex: 1;
          padding: 6px 0;
          text-align: center;
          background: #17181f;
          border: 1px solid #252632;
          border-radius: 8px;
          font-size: 12px;
          font-weight: 550;
          color: #8c8ea0;
          cursor: pointer;
          transition: all 0.14s;
        }
        .luma-autourok-day-pill:hover {
          border-color: #3b3c4d;
          color: #ffffff;
        }
        .luma-autourok-day-pill.active {
          background: #7C5CE4;
          border-color: #7C5CE4;
          color: #ffffff;
          font-weight: 650;
        }

        /* Lesson Row */
        .luma-autourok-lesson-row {
          display: flex;
          align-items: center;
          gap: 8px;
          background: #16171d;
          border: 1px solid #23242d;
          border-radius: 10px;
          padding: 8px 10px;
          margin-bottom: 8px;
        }

        .luma-autourok-lesson-num {
          font-size: 12.5px;
          font-weight: 700;
          color: #7C5CE4;
          min-width: 18px;
          text-align: center;
        }

        .luma-autourok-input {
          border: 1px solid #272833;
          background: #1b1c23;
          color: #ffffff;
          padding: 6px 10px;
          border-radius: 7px;
          font-size: 13px;
          outline: none;
          transition: border-color 0.15s;
        }
        .luma-autourok-input:focus {
          border-color: #7C5CE4;
        }
        .luma-autourok-input-name {
          flex: 1;
        }
        .luma-autourok-input-time {
          width: 60px;
          text-align: center;
        }

        .luma-autourok-del-btn {
          background: transparent;
          border: none;
          cursor: pointer;
          color: #6e7080;
          padding: 4px;
          border-radius: 6px;
          display: flex;
          align-items: center;
          justify-content: center;
          transition: color 0.15s;
        }
        .luma-autourok-del-btn:hover {
          color: #ef4444;
        }

        .luma-autourok-actions-bar {
          display: flex;
          gap: 8px;
          margin-top: 14px;
        }

        .luma-autourok-btn-secondary {
          flex: 1;
          background: #1a1b22;
          border: 1px solid #2a2b37;
          color: #c2c3d2;
          padding: 9px 14px;
          border-radius: 9px;
          font-size: 13px;
          font-weight: 550;
          cursor: pointer;
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 6px;
          transition: all 0.15s;
        }
        .luma-autourok-btn-secondary:hover {
          background: #22232c;
          border-color: #383948;
          color: #ffffff;
        }

        .luma-autourok-btn-primary {
          flex: 1;
          background: #7C5CE4;
          border: 1px solid #7C5CE4;
          color: #ffffff;
          padding: 9px 14px;
          border-radius: 9px;
          font-size: 13px;
          font-weight: 600;
          cursor: pointer;
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 6px;
          transition: background 0.15s;
        }
        .luma-autourok-btn-primary:hover {
          background: #6D4BD6;
        }

        /* Dev / Test Panel */
        .luma-test-card {
          background: #16171d;
          border: 1px solid #282935;
          border-radius: 12px;
          padding: 14px;
          margin-bottom: 12px;
        }
        .luma-test-badge {
          display: inline-block;
          font-size: 10px;
          font-weight: 700;
          letter-spacing: 0.6px;
          color: #a78bfa;
          background: rgba(124, 92, 228, 0.15);
          border: 1px solid rgba(124, 92, 228, 0.3);
          border-radius: 6px;
          padding: 2px 7px;
          margin-bottom: 8px;
        }
        .luma-test-chips {
          display: flex;
          flex-wrap: wrap;
          gap: 6px;
          margin: 10px 0;
        }
        .luma-test-chip {
          background: #1f2029;
          border: 1px solid #2d2e3b;
          color: #b5b7c8;
          font-size: 11.5px;
          padding: 4px 9px;
          border-radius: 6px;
          cursor: pointer;
          transition: all 0.14s;
        }
        .luma-test-chip:hover {
          background: #2b2c3a;
          color: #ffffff;
          border-color: #7C5CE4;
        }
        .luma-test-console {
          background: #0d0e12;
          border: 1px solid #1f2028;
          border-radius: 8px;
          padding: 10px 12px;
          font-family: 'Consolas', 'Courier New', monospace;
          font-size: 11.5px;
          line-height: 1.5;
          color: #a0a2b4;
          max-height: 160px;
          overflow-y: auto;
          white-space: pre-wrap;
          word-break: break-all;
        }

        /* Schedule Photo Dropzone & OCR Loader */
        .luma-dropzone {
          border: 2px dashed #3a3b4c;
          background: #14151b;
          border-radius: 12px;
          padding: 22px 16px;
          text-align: center;
          cursor: pointer;
          transition: all 0.18s ease;
          margin-bottom: 12px;
          display: flex;
          flex-direction: column;
          align-items: center;
          gap: 6px;
        }
        .luma-dropzone:hover, .luma-dropzone.dragover {
          border-color: #7C5CE4;
          background: rgba(124, 92, 228, 0.08);
        }
        .luma-dropzone-icon {
          margin-bottom: 2px;
        }
        .luma-dropzone-title {
          font-size: 13.5px;
          font-weight: 600;
          color: #e2e3ee;
        }
        .luma-dropzone-desc {
          font-size: 11.5px;
          color: #7c7e90;
        }
        .luma-ocr-loading {
          display: flex;
          align-items: center;
          justify-content: center;
          gap: 12px;
          padding: 14px 16px;
          background: #181922;
          border: 1px solid #323348;
          border-radius: 10px;
          font-size: 12.5px;
          color: #a78bfa;
          margin-bottom: 12px;
        }
        .luma-spinner {
          width: 20px;
          height: 20px;
          border: 2.5px solid rgba(124, 92, 228, 0.25);
          border-top-color: #7C5CE4;
          border-radius: 50%;
          animation: luma-spin 0.7s linear infinite;
        }
        @keyframes luma-spin {
          to { transform: rotate(360deg); }
        }

        /* Settings card */
        .luma-setting-row {
          display: flex;
          align-items: center;
          justify-content: space-between;
          padding: 10px 0;
          border-bottom: 1px solid #1f2028;
        }
        .luma-setting-row:last-child {
          border-bottom: none;
        }
        .luma-setting-label {
          font-size: 13px;
          color: #dedee8;
          font-weight: 500;
        }
        .luma-setting-desc {
          font-size: 11.5px;
          color: #7c7e90;
          margin-top: 2px;
        }

        /* Luma Switch (native browser toggle switch style) */
        .luma-switch {
          appearance: none;
          -webkit-appearance: none;
          width: 44px;
          height: 24px;
          border-radius: 999px;
          background: #353642;
          position: relative;
          cursor: pointer;
          outline: none;
          border: 1px solid #424452;
          transition: background 0.18s, border-color 0.18s;
          margin: 0;
          flex-shrink: 0;
        }
        .luma-switch:hover {
          border-color: #555768;
        }
        .luma-switch:after {
          content: "";
          position: absolute;
          width: 18px;
          height: 18px;
          left: 2px;
          top: 2px;
          border-radius: 50%;
          background: #ffffff;
          box-shadow: 0 1px 3px rgba(0,0,0,0.35);
          transition: transform 0.18s cubic-bezier(0.16, 1, 0.3, 1);
        }
        .luma-switch:checked {
          background: #7C5CE4;
          border-color: #7C5CE4;
        }
        .luma-switch:checked:after {
          transform: translateX(20px);
        }

        .luma-autourok-toast {
          position: fixed;
          top: 24px;
          right: 24px;
          background: #181920;
          border: 1px solid #2c2d3a;
          color: #ffffff;
          padding: 12px 18px;
          border-radius: 12px;
          box-shadow: 0 12px 36px rgba(0, 0, 0, 0.5);
          display: flex;
          align-items: center;
          gap: 10px;
          z-index: 2147483647;
          font-size: 13.5px;
          font-weight: 500;
          animation: lumaModalIn 0.2s forwards;
        }
      `;
      try {
        st.appendChild(document.createTextNode(cssContent));
      } catch (_) {
        st.textContent = cssContent;
      }
      const target = document.head || document.documentElement || document.body;
      if (target) {
        try { target.appendChild(st); } catch (_) {}
      }
    }

    function showToast(msg) {
      injectStyles();
      const t = document.createElement('div');
      t.className = 'luma-autourok-toast';
      const iconSpan = document.createElement('span');
      iconSpan.style.color = '#7C5CE4';
      setSafeHTML(iconSpan, lucideIcon('sparkles', 18, '#7C5CE4'));
      const span = document.createElement('span');
      span.textContent = msg;
      t.appendChild(iconSpan);
      t.appendChild(span);
      (document.body || document.documentElement).appendChild(t);
      setTimeout(() => {
        t.style.opacity = '0';
        t.style.transition = 'opacity 0.25s';
        setTimeout(() => t.remove(), 250);
      }, 3200);
    }

    // ────────────────────── SCHEDULE LOGIC ──────────────────────
    function getTodaySchedule() {
      const day = new Date().getDay();
      return config.schedule[day] || [];
    }

    function timeToMinutes(t) {
      if (!t) return 0;
      const [h, m] = t.split(':').map(Number);
      return (h || 0) * 60 + (m || 0);
    }

    function getCurrentLessonInfo() {
      const now = new Date();
      const currentMin = now.getHours() * 60 + now.getMinutes();
      const currentSec = currentMin * 60 + now.getSeconds();
      const lessons = getTodaySchedule().map(l => ({
        ...l,
        startMin: timeToMinutes(l.start),
        endMin: timeToMinutes(l.end),
      })).sort((a, b) => a.startMin - b.startMin);

      let ongoing = null;
      let next = null;

      for (const l of lessons) {
        if (currentMin >= l.startMin && currentMin < l.endMin) {
          ongoing = l;
          break;
        }
      }

      if (!ongoing) {
        for (const l of lessons) {
          if (l.startMin > currentMin) {
            next = l;
            break;
          }
        }
      }

      return { ongoing, next, currentMin, currentSec, lessons };
    }

    // ────────────────────── INTELLIGENT AI LESSON MATCHER ──────────────────────
    function cleanString(str) {
      if (!str) return '';
      return str
        .toLowerCase()
        .replace(/(?:^|\s+)(?:11\s*[-–—]?\s*[а-яa-z0-9]?|11[а-яa-z0-9]?|клас[с]?|н\.?р\.?)(?=\s+|$|[.,/#!$%^&*;:{}=\-_`~()«»"])/gi, ' ')
        .replace(/\b20\d\d\s*[-–—]?\s*20\d\d\b/g, ' ')
        .replace(/[.,/#!$%^&*;:{}=\-_`~()«»"]/g, ' ')
        .replace(/\s+/g, ' ')
        .trim();
    }

    function isMatch(target, candidate) {
      if (!target || !candidate) return 0;
      if (target.length < 2 || candidate.length < 2) return 0;
      if (target === candidate) return 100;

      if (target.includes(candidate)) {
        return Math.max(60, Math.round((candidate.length / target.length) * 100));
      }
      if (candidate.includes(target)) {
        return Math.max(60, Math.round((target.length / candidate.length) * 100));
      }

      const targetWords = target.split(' ').filter(w => w.length >= 3);
      const candWords = candidate.split(' ').filter(w => w.length >= 3);
      let matchedWords = 0;
      for (const tw of targetWords) {
        if (candWords.some(cw => cw === tw || (cw.length >= 4 && tw.length >= 4 && (cw.startsWith(tw) || tw.startsWith(cw))))) {
          matchedWords++;
        }
      }
      if (targetWords.length > 0 && matchedWords > 0) {
        return Math.round((matchedWords / targetWords.length) * 90);
      }
      return 0;
    }

    const SUBJECT_SYNONYMS = {
      'алгебра': ['математика', 'алгебра', 'algebra', 'мат', 'матем'],
      'геометрия': ['математика', 'геометрія', 'геометрия', 'geometry', 'мат', 'матем'],
      'геометрія': ['математика', 'геометрія', 'геометрия', 'geometry', 'мат', 'матем'],
      'математика': ['математика', 'алгебра', 'геометрія', 'геометрия', 'math', 'мат', 'матем'],

      'зарубежная литература': ['зарубіжна література', 'зарубежная литература', 'зарубіжна', 'зарубежная', 'зар літ', 'зар лит', 'зарубежка'],
      'зарубежная лит': ['зарубіжна література', 'зарубежная литература', 'зарубіжна', 'зарубежная', 'зар літ', 'зар лит', 'зарубежка'],
      'зарубежная лит.': ['зарубіжна література', 'зарубежная литература', 'зарубіжна', 'зарубежная', 'зар літ', 'зар лит', 'зарубежка'],
      'зарубежка': ['зарубіжна література', 'зарубежная литература', 'зарубіжна', 'зарубежная', 'зар літ', 'зар лит', 'зарубежка'],
      'зарубіжна література': ['зарубіжна література', 'зарубежная литература', 'зарубіжна', 'зарубежная', 'зар літ', 'зар лит'],

      'украинская литература': ['українська література', 'украинская литература', 'укр літ', 'укр лит'],
      'українська література': ['українська література', 'украинская литература', 'укр літ', 'укр лит'],
      'укр літ': ['українська література', 'украинская литература', 'укр літ', 'укр лит'],
      'укр лит': ['українська література', 'украинская литература', 'укр літ', 'укр лит'],

      'украинский язык': ['українська мова', 'украинский язык', 'укр мова', 'мова'],
      'українська мова': ['українська мова', 'украинский язык', 'укр мова', 'мова'],
      'укр мова': ['українська мова', 'украинский язык', 'укр мова', 'мова'],

      'биология': ['біологія', 'биология', 'biology'],
      'біологія': ['біологія', 'биология', 'biology'],

      'химия': ['хімія', 'химия', 'chemistry'],
      'хімія': ['хімія', 'химия', 'chemistry'],

      'английский': ['english', 'англійська', 'английский', 'англ'],
      'english': ['english', 'англійська', 'английский', 'англ'],

      'физика': ['фізика', 'физика', 'physics'],
      'фізика': ['фізика', 'физика', 'physics'],

      'физкультура': ['фізична культура', 'физкультура', 'фізкультура', 'спорт'],
      'фізична культура': ['фізична культура', 'физкультура', 'фізкультура', 'спорт'],

      'астрономия': ['астрономія', 'астрономия'],
      'астрономія': ['астрономія', 'астрономия'],

      'всемирная история': ['всесвітня історія', 'всемирная история', 'всесвітня', 'всемирная'],
      'всесвітня історія': ['всесвітня історія', 'всемирная история', 'всесвітня', 'всемирная'],

      'история украины': ['історія україни', 'история украины'],
      'історія україни': ['історія україни', 'история украины'],
      'история': ['історія україни', 'всесвітня історія', 'історія', 'история'],

      'география': ['географія', 'география', 'geography'],
      'географія': ['географія', 'география'],

      'технологии': ['технології', 'технологии', 'труды'],
      'технології': ['технології', 'технологии'],

      'искусство': ['мистецтво', 'искусство'],
      'мистецтво': ['мистецтво', 'искусство']
    };

    // 15 actual courses from Screen 3 (used for dev testing fallback)
    const MOCK_COURSES = [
      { title: '11 - В', subtitle: 'Зарубіжна література', allText: '11 - В\nЗарубіжна література', url: 'https://classroom.google.com/c/zarubizhna' },
      { title: 'Фізична культура 11-В', subtitle: '', allText: 'Фізична культура 11-В', url: 'https://classroom.google.com/c/fizra' },
      { title: '11-В Біологія', subtitle: '2026-2027 н.р.', allText: '11-В Біологія\n2026-2027 н.р.', url: 'https://classroom.google.com/c/bio' },
      { title: '11-B English', subtitle: '', allText: '11-B English', url: 'https://classroom.google.com/c/english' },
      { title: '11-В ХІМІЯ', subtitle: '', allText: '11-В ХІМІЯ', url: 'https://classroom.google.com/c/chem' },
      { title: 'Астрономія', subtitle: '11-В', allText: 'Астрономія\n11-В', url: 'https://classroom.google.com/c/astro' },
      { title: 'Фізика', subtitle: '11-В', allText: 'Фізика\n11-В', url: 'https://classroom.google.com/c/physics' },
      { title: '11-В клас. Всесвітня історія.', subtitle: '', allText: '11-В клас. Всесвітня історія.', url: 'https://classroom.google.com/c/worldhistory' },
      { title: 'УКРАЇНСЬКА МОВА', subtitle: '11-В', allText: 'УКРАЇНСЬКА МОВА\n11-В', url: 'https://classroom.google.com/c/ukrmova' },
      { title: 'Математика 11-В', subtitle: '', allText: 'Математика 11-В', url: 'https://classroom.google.com/c/math' },
      { title: '11-В', subtitle: 'Географія', allText: '11-В\nГеографія', url: 'https://classroom.google.com/c/geo' },
      { title: '11-В клас. Історія України.', subtitle: '', allText: '11-В клас. Історія України.', url: 'https://classroom.google.com/c/ukrhistory' },
      { title: '11-В ТЕХНОЛОГІЇ', subtitle: '2026-2027', allText: '11-В ТЕХНОЛОГІЇ\n2026-2027', url: 'https://classroom.google.com/c/tech' },
      { title: '11В Мистецтво', subtitle: '', allText: '11В Мистецтво', url: 'https://classroom.google.com/c/art' },
      { title: 'УКРАЇНСЬКА ЛІТЕРАТУРА', subtitle: '11-В', allText: 'УКРАЇНСЬКА ЛІТЕРАТУРА\n11-В', url: 'https://classroom.google.com/c/ukrlit' }
    ];

    function getSynonymsList(query) {
      const q = cleanString(query);
      const list = new Set([q, query.toLowerCase().trim()]);

      // Exact match
      if (SUBJECT_SYNONYMS[q]) {
        SUBJECT_SYNONYMS[q].forEach(s => list.add(s));
        return Array.from(list).filter(s => s && s.length >= 2);
      }

      // Multi-word phrase matches sorted by longest key first
      const keys = Object.keys(SUBJECT_SYNONYMS).sort((a, b) => b.length - a.length);
      for (const k of keys) {
        if (k === q || (k.includes(' ') && q.includes(k)) || (q.includes(' ') && k.includes(q))) {
          SUBJECT_SYNONYMS[k].forEach(s => list.add(s));
          return Array.from(list).filter(s => s && s.length >= 2);
        }
      }

      // Single word substring matches
      for (const k of keys) {
        if (q.includes(k) || k.includes(q)) {
          SUBJECT_SYNONYMS[k].forEach(s => list.add(s));
        }
      }

      return Array.from(list).filter(s => s && s.length >= 2);
    }

    function scanPageCourses() {
      const candidates = [];
      const links = Array.from(document.querySelectorAll('a[href*="/c/"], [data-course-id]'));

      for (const a of links) {
        const href = a.href || a.getAttribute('href') || '';
        if (!href.includes('/c/')) continue;

        // Container card (e.g. course card or sidebar item)
        const container = a.closest('[role="listitem"], li, .gHz6xd, div[data-id]') || a;
        
        // In Google Classroom:
        // Title is often in first heading/div (e.g. "11 - В" or "11-В Біологія")
        // Subtitle is in second line/div (e.g. "Зарубіжна література" or "2026-2027 н.р.")
        let title = '';
        let subtitle = '';

        const headings = container.querySelectorAll('h2, h3, [role="heading"], [class*="title"], [data-sort-key]');
        if (headings.length > 0) {
          title = (headings[0].innerText || '').trim();
        }

        const subElements = container.querySelectorAll('[class*="subtitle"], [class*="section"], [class*="description"]');
        if (subElements.length > 0) {
          subtitle = (subElements[0].innerText || '').trim();
        }

        // Fallback: examine text children
        if (!title && !subtitle) {
          const directDivs = Array.from(a.querySelectorAll('div')).filter(d => d.children.length === 0 && d.innerText.trim());
          if (directDivs.length >= 2) {
            title = directDivs[0].innerText.trim();
            subtitle = directDivs[1].innerText.trim();
          } else if (directDivs.length === 1) {
            title = directDivs[0].innerText.trim();
          }
        }

        const allText = (container.innerText || a.innerText || '').trim();
        if (!title && allText) title = allText.split('\n')[0].trim();
        if (!subtitle && allText.includes('\n')) subtitle = allText.split('\n').slice(1).join(' ').trim();

        // Check if there is an existing Meet link/button directly on the card
        const meetEl = container.querySelector('a[href*="meet.google.com"], [data-meet-url], [aria-label*="Meet" i], [aria-label*="відеозустріч" i]');
        const directMeet = meetEl ? (meetEl.href || meetEl.getAttribute('data-meet-url') || '') : '';

        candidates.push({
          element: a,
          container: container,
          url: href,
          title: title,
          subtitle: subtitle,
          allText: allText,
          directMeet: directMeet
        });
      }

      // De-duplicate by course URL
      const unique = [];
      const seen = new Set();
      for (const c of candidates) {
        if (!seen.has(c.url)) {
          seen.add(c.url);
          unique.push(c);
        }
      }
      return unique;
    }

    // ────────────────────── 100% EXACT COURSE DATABASE (SCREEN 3 & 11-В) ──────────────────────
    const EXACT_COURSE_RULES = [
      {
        id: 'zarubizhna',
        canonicalName: '11 - В | Зарубіжна література',
        queryKeywords: ['зарубежная литература', 'зарубіжна література', 'зарубежная', 'зарубіжна', 'зарубежка', 'зар літ', 'зар лит', 'зар. літ', 'зар. лит', 'зарубежная лит'],
        matchCourse: (c) => {
          const sub = (c.subtitle || '').toLowerCase();
          const tit = (c.title || '').toLowerCase();
          const all = (c.allText || '').toLowerCase();
          return sub.includes('зарубіжн') || sub.includes('зарубежн') ||
                 tit.includes('зарубіжн') || tit.includes('зарубежн') ||
                 all.includes('зарубіжн') || all.includes('зарубежн');
        }
      },
      {
        id: 'math',
        canonicalName: 'Математика 11-В',
        queryKeywords: ['алгебра', 'геометрия', 'геометрія', 'математика', 'матем', 'мат', 'algebra', 'geometry', 'math'],
        matchCourse: (c) => {
          const tit = (c.title || '').toLowerCase();
          const all = (c.allText || '').toLowerCase();
          return tit.includes('математик') || all.includes('математик') || tit.includes('матем') || all.includes('матем');
        }
      },
      {
        id: 'ukrlit',
        canonicalName: 'УКРАЇНСЬКА ЛІТЕРАТУРА | 11-В',
        queryKeywords: ['украинская литература', 'українська література', 'укр литература', 'укр література', 'укр літ', 'укр лит', 'укр. літ', 'укр. лит'],
        matchCourse: (c) => {
          const all = ((c.title || '') + ' ' + (c.subtitle || '') + ' ' + (c.allText || '')).toLowerCase();
          return (all.includes('українськ') || all.includes('украинск') || all.includes('укр')) &&
                 (all.includes('літератур') || all.includes('литератур') || all.includes('літ'));
        }
      },
      {
        id: 'ukrmova',
        canonicalName: 'УКРАЇНСЬКА МОВА | 11-В',
        queryKeywords: ['украинский язык', 'українська мова', 'укр язык', 'укр мова', 'мова', 'укр. мова', 'укр. язык'],
        matchCourse: (c) => {
          const all = ((c.title || '') + ' ' + (c.subtitle || '') + ' ' + (c.allText || '')).toLowerCase();
          return (all.includes('українськ') || all.includes('украинск') || all.includes('укр')) &&
                 (all.includes('мов') || all.includes('язык'));
        }
      },
      {
        id: 'chem',
        canonicalName: '11-В ХІМІЯ',
        queryKeywords: ['химия', 'хімія', 'chemistry', 'хим', 'хім'],
        matchCourse: (c) => {
          const all = ((c.title || '') + ' ' + (c.subtitle || '') + ' ' + (c.allText || '')).toLowerCase();
          return all.includes('хімі') || all.includes('хими') || all.includes('chem');
        }
      },
      {
        id: 'bio',
        canonicalName: '11-В Біологія (2026-2027)',
        queryKeywords: ['биология', 'біологія', 'biology', 'био', 'біо'],
        matchCourse: (c) => {
          const all = ((c.title || '') + ' ' + (c.subtitle || '') + ' ' + (c.allText || '')).toLowerCase();
          return all.includes('біолог') || all.includes('биолог') || all.includes('bio');
        }
      },
      {
        id: 'english',
        canonicalName: '11-B English',
        queryKeywords: ['english', 'английский', 'англійська', 'англ', 'английский язык', 'англійська мова', 'инглиш'],
        matchCourse: (c) => {
          const all = ((c.title || '') + ' ' + (c.subtitle || '') + ' ' + (c.allText || '')).toLowerCase();
          return all.includes('english') || all.includes('англійськ') || all.includes('английск') || all.includes('англ');
        }
      },
      {
        id: 'physics',
        canonicalName: 'Фізика | 11-В',
        queryKeywords: ['физика', 'фізика', 'physics', 'физ', 'фіз'],
        matchCourse: (c) => {
          const tit = (c.title || '').toLowerCase();
          const sub = (c.subtitle || '').toLowerCase();
          const all = (c.allText || '').toLowerCase();
          if (all.includes('культур') || all.includes('sport')) return false;
          return tit.includes('фізик') || tit.includes('физик') || sub.includes('фізик') || sub.includes('физик') || (all.includes('фізик') && !all.includes('культура'));
        }
      },
      {
        id: 'astro',
        canonicalName: 'Астрономія | 11-В',
        queryKeywords: ['астрономия', 'астрономія', 'astronomy', 'астро'],
        matchCourse: (c) => {
          const all = ((c.title || '') + ' ' + (c.subtitle || '') + ' ' + (c.allText || '')).toLowerCase();
          return all.includes('астроном') || all.includes('astron');
        }
      },
      {
        id: 'sport',
        canonicalName: 'Фізична культура 11-В',
        queryKeywords: ['физкультура', 'фізкультура', 'фізична культура', 'физическая культура', 'спорт', 'физра', 'фізра'],
        matchCourse: (c) => {
          const all = ((c.title || '') + ' ' + (c.subtitle || '') + ' ' + (c.allText || '')).toLowerCase();
          return all.includes('фізична культура') || all.includes('физическая культура') || all.includes('культур') || all.includes('фізкультур') || all.includes('физкультур');
        }
      },
      {
        id: 'worldhistory',
        canonicalName: '11-В клас. Всесвітня історія.',
        queryKeywords: ['всемирная история', 'всесвітня історія', 'всесвітня', 'всемирная', 'всемирка'],
        matchCourse: (c) => {
          const all = ((c.title || '') + ' ' + (c.subtitle || '') + ' ' + (c.allText || '')).toLowerCase();
          return all.includes('всесвітн') || all.includes('всемирн');
        }
      },
      {
        id: 'ukrhistory',
        canonicalName: '11-В клас. Історія України.',
        queryKeywords: ['история украины', 'історія україни', 'история', 'історія'],
        matchCourse: (c) => {
          const all = ((c.title || '') + ' ' + (c.subtitle || '') + ' ' + (c.allText || '')).toLowerCase();
          return (all.includes('історі') || all.includes('истори')) && (all.includes('україн') || all.includes('украин') || !all.includes('всесвіт'));
        }
      },
      {
        id: 'geo',
        canonicalName: '11-В | Географія',
        queryKeywords: ['география', 'географія', 'geography', 'гео'],
        matchCourse: (c) => {
          const sub = (c.subtitle || '').toLowerCase();
          const tit = (c.title || '').toLowerCase();
          const all = (c.allText || '').toLowerCase();
          return sub.includes('географ') || tit.includes('географ') || all.includes('географ');
        }
      },
      {
        id: 'tech',
        canonicalName: '11-В ТЕХНОЛОГІЇ',
        queryKeywords: ['технологии', 'технології', 'труды', 'труди', 'tech'],
        matchCourse: (c) => {
          const all = ((c.title || '') + ' ' + (c.subtitle || '') + ' ' + (c.allText || '')).toLowerCase();
          return all.includes('технолог') || all.includes('tech');
        }
      },
      {
        id: 'art',
        canonicalName: '11В Мистецтво',
        queryKeywords: ['искусство', 'мистецтво', 'art', 'изо'],
        matchCourse: (c) => {
          const all = ((c.title || '') + ' ' + (c.subtitle || '') + ' ' + (c.allText || '')).toLowerCase();
          return all.includes('мистецтв') || all.includes('искусств') || all.includes('art');
        }
      }
    ];

    function matchCourse(query, courses) {
      if (!query) return { best: null, highestScore: 0, results: [] };
      const qClean = cleanString(query);
      const qLower = query.toLowerCase().trim();

      // 1. FIRST PRIORITY: Check 100% exact database rules (Screen 3 verified classes)
      for (const rule of EXACT_COURSE_RULES) {
        const matchesQuery = rule.queryKeywords.some(kw => {
          const kwClean = cleanString(kw);
          return qClean === kwClean || qLower === kw.toLowerCase() ||
                 (kw.length >= 4 && (qClean.includes(kwClean) || kwClean.includes(qClean)));
        });

        if (matchesQuery) {
          for (const c of courses) {
            if (rule.matchCourse(c)) {
              const matched = { ...c, score: 100, matchedRule: rule.canonicalName };
              return {
                best: matched,
                highestScore: 100,
                results: [matched]
              };
            }
          }
        }
      }

      // 2. SECOND PRIORITY: Standard synonym and fuzzy matching (for any custom or unmapped courses)
      const syns = getSynonymsList(query);
      let best = null;
      let highestScore = 0;

      const results = courses.map(c => {
        let score = 0;
        const cleanSub = cleanString(c.subtitle);
        const cleanTit = cleanString(c.title);
        const cleanAll = cleanString(c.allText);

        for (const s of syns) {
          const sSub = isMatch(s, cleanSub);
          const sTit = isMatch(s, cleanTit);
          const sAll = isMatch(s, cleanAll);
          score = Math.max(score, sSub, sTit, Math.round(sAll * 0.9));
        }

        return { ...c, score };
      });

      results.sort((a, b) => b.score - a.score);
      if (results.length > 0 && results[0].score >= 60) {
        best = results[0];
        highestScore = best.score;
      }

      return { best, highestScore, results };
    }

    // Export for dev test and inspector
    window.__lumaMatchCourse = matchCourse;
    window.__lumaScanPageCourses = scanPageCourses;
    window.__lumaTriggerJoinLesson = triggerJoinLesson;

    function extractMeetFromCurrentPage() {
      // Look for Google Meet links in the course stream header or banners
      const meetLinks = Array.from(document.querySelectorAll('a[href*="meet.google.com"], [data-meet-url]'));
      for (const a of meetLinks) {
        const u = a.href || a.getAttribute('data-meet-url');
        if (u && u.includes('meet.google.com/')) return u;
      }

      // Look for buttons that trigger Meet
      const buttons = Array.from(document.querySelectorAll('button, [role="button"]'));
      for (const b of buttons) {
        const txt = (b.innerText || b.getAttribute('aria-label') || '').toLowerCase();
        if (txt.includes('присоединиться') || txt.includes('приєднатися') || txt.includes('join now') || txt.includes('відеозустріч') || txt.includes('видеовстреча')) {
          const parentA = b.closest('a[href*="meet.google.com"]');
          if (parentA) return parentA.href;
          b.click();
          return 'CLICKED';
        }
      }
      return null;
    }

    function triggerJoinLesson(subjectName, logFn = null) {
      const log = (msg) => {
        console.log('[Luma AutoLesson]', msg);
        if (logFn) logFn(msg);
      };

      if (!subjectName) return;
      log(`Запуск поиска урока: «${subjectName}»...`);
      showToast(`Ищу курс «${subjectName}»...`);

      const courses = scanPageCourses();
      log(`Найдено активных курсов на странице: ${courses.length}`);

      if (courses.length === 0) {
        log('Курсы в DOM не найдены. Возможно, страница ещё загружается.');
        // If already inside course, check for Meet link
        const direct = extractMeetFromCurrentPage();
        if (direct && direct !== 'CLICKED') {
          log(`Найдена прямая ссылка Meet на текущей странице: ${direct}`);
          location.href = direct;
        }
        return;
      }

      const match = matchCourse(subjectName, courses);
      if (!match.best) {
        log(`Не удалось с уверенностью сопоставить «${subjectName}». Лучший балл: ${match.highestScore}%`);
        return;
      }

      const best = match.best;
      log(`Точное совпадение (${best.score}%): «${best.title}» | «${best.subtitle}»`);

      if (best.directMeet) {
        log(`Переход по прямой ссылке Meet карточки: ${best.directMeet}`);
        location.href = best.directMeet;
        return;
      }

      // Navigate to course
      log(`Переход на страницу курса: ${best.url}`);
      showToast(`Курс найден: ${best.subtitle || best.title}. Вхожу в Google Meet...`);

      if (best.element && typeof best.element.click === 'function') {
        best.element.click();
      } else {
        location.href = best.url;
      }

      // Poll for Meet link after course page opens
      let polls = 0;
      const t = setInterval(() => {
        polls++;
        log(`Ожидание ссылки Google Meet в шапке курса (попытка ${polls}/15)...`);
        const found = extractMeetFromCurrentPage();
        if (found) {
          clearInterval(t);
          if (found !== 'CLICKED') {
            log(`Google Meet найден: ${found}. Перехожу!`);
            location.href = found;
          } else {
            log(`Кнопка Google Meet успешно нажата!`);
          }
        } else if (polls >= 15) {
          clearInterval(t);
          log('Ссылка на Google Meet не обнаружена в шапке курса.');
        }
      }, 400);
    }

    // ────────────────────── BACKGROUND MONITORING ──────────────────────
    function runBackgroundCheck() {
      if (!isClassroom || !config.enabled || !config.autoJoin) return;

      const { ongoing, next, currentMin } = getCurrentLessonInfo();
      const target = ongoing || next;
      if (!target) return;

      const lead = config.leadMinutes || 0;
      const shouldJoin = currentMin >= (target.startMin - lead) && currentMin <= target.endMin;

      if (shouldJoin) {
        const todayStr = new Date().toISOString().slice(0, 10);
        const joinKey = `${todayStr}_${target.subject}_${target.start}`;
        const lastJoined = localStorage.getItem(JOINED_KEY);

        if (lastJoined !== joinKey) {
          localStorage.setItem(JOINED_KEY, joinKey);
          if (config.notify) showToast(`Урок «${target.subject}». Запуск авто-входа...`);
          setTimeout(() => triggerJoinLesson(target.subject), 1000);
        }
      }
    }

    // ────────────────────── GOOGLE MEET AUTO-MUTE & JOIN ──────────────────────
    function handleGoogleMeet() {
      if (!isMeet) return;
      if (!config.enabled) return;
      if (!config.autoJoin && !config.muteMicAndCam) return;

      console.log('[Luma AutoLesson] Google Meet: Запуск контроля микрофона и камеры (цель: ОБА КРАСНЫЕ)...');

      function isElementRed(el) {
        if (!el) return false;

        // 1. Check data-is-muted attribute on element, ancestors, or descendants
        const mutedEl = (el.getAttribute && el.hasAttribute('data-is-muted')) ? el :
          (el.closest && el.closest('[data-is-muted]')) ||
          (el.querySelector && el.querySelector('[data-is-muted]'));
        if (mutedEl) {
          const mVal = mutedEl.getAttribute('data-is-muted');
          if (mVal === 'true') return true;
          if (mVal === 'false') return false;
        }

        // 2. Check aria-label / title / tooltip text
        const label = (
          el.getAttribute('aria-label') ||
          el.getAttribute('title') ||
          el.getAttribute('data-tooltip') ||
          el.innerText ||
          (el.querySelector && el.querySelector('[aria-label]') ? el.querySelector('[aria-label]').getAttribute('aria-label') : '') ||
          (el.closest && el.closest('[aria-label]') ? el.closest('[aria-label]').getAttribute('aria-label') : '') ||
          ''
        ).toLowerCase();

        // If label contains turn-on verbs -> ALREADY OFF (RED)
        if (
          label.includes('turn on') ||
          label.includes('включить') ||
          label.includes('включи') ||
          label.includes('увімкнути') ||
          label.includes('увімк') ||
          label.includes('ввімкнути') ||
          label.includes('ввімк')
        ) {
          return true;
        }

        // If label contains turn-off verbs -> ACTIVE (WHITE), NEEDS TO BE CLICKED
        if (
          label.includes('turn off') ||
          label.includes('выключить') ||
          label.includes('отключить') ||
          label.includes('вимкнути') ||
          label.includes('вимк')
        ) {
          return false;
        }

        // 3. Visual computed style check (Red vs White/Transparent)
        const nodes = [el, el.firstElementChild, el.parentElement, el.querySelector ? el.querySelector('div, span, button') : null].filter(Boolean);
        for (const n of nodes) {
          try {
            const bg = window.getComputedStyle(n).backgroundColor || '';
            const m = bg.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/);
            if (m) {
              const r = parseInt(m[1], 10);
              const g = parseInt(m[2], 10);
              const b = parseInt(m[3], 10);
              // Red: r > 160, g < 110, b < 110
              if (r > 160 && g < 110 && b < 110) return true;
              // White / Light: r > 180, g > 180, b > 180
              if (r > 180 && g > 180 && b > 180) return false;
            }
          } catch (_) {}
        }

        // 4. Check for icon names indicating OFF state (Material Symbols: mic_off, videocam_off)
        const inner = (el.innerHTML || '').toLowerCase();
        if (inner.includes('mic_off') || inner.includes('videocam_off')) {
          return true;
        }

        return false;
      }

      function getMeetButtons() {
        let mic = null;
        let cam = null;

        // Method 1: By Ctrl + D / Ctrl + E and language keywords in aria-label, title, or tooltip
        const candidates = Array.from(document.querySelectorAll('button, div[role="button"], span[role="button"], [data-is-muted]'));
        for (const el of candidates) {
          const txt = (
            el.getAttribute('aria-label') ||
            el.getAttribute('data-tooltip') ||
            el.getAttribute('title') ||
            el.innerText ||
            el.innerHTML ||
            ''
          ).toLowerCase();

          const isMicCandidate = txt.includes('ctrl + d') || txt.includes('ctrl+d') || txt.includes('мікрофон') || txt.includes('микрофон') || txt.includes('microphone') || txt.includes('mic_off') || (txt.includes('mic') && !txt.includes('cam'));
          const isCamCandidate = txt.includes('ctrl + e') || txt.includes('ctrl+e') || txt.includes('камер') || txt.includes('camera') || txt.includes('videocam') || txt.includes('videocam_off');

          if (!mic && isMicCandidate && !isCamCandidate) {
            mic = el.closest('button, [role="button"]') || el;
          }
          if (!cam && isCamCandidate && !isMicCandidate) {
            cam = el.closest('button, [role="button"]') || el;
          }
        }

        // Method 2: By data-is-muted if not yet assigned
        if (!mic || !cam) {
          const mutedElements = Array.from(document.querySelectorAll('[data-is-muted]'));
          if (!mic && mutedElements[0]) mic = mutedElements[0].closest('button, [role="button"]') || mutedElements[0];
          if (!cam && mutedElements[1]) cam = mutedElements[1].closest('button, [role="button"]') || mutedElements[1];
        }

        // Method 3: By circular buttons in preview area
        if (!mic || !cam) {
          const previewButtons = Array.from(document.querySelectorAll('button, div[role="button"]')).filter(b => {
            const rect = b.getBoundingClientRect();
            return rect.width >= 36 && rect.width <= 76 && Math.abs(rect.width - rect.height) <= 12;
          });
          if (previewButtons.length >= 2) {
            if (!mic) mic = previewButtons[0];
            if (!cam) cam = previewButtons[1];
          }
        }

        return { mic, cam };
      }

      function safeClick(el) {
        if (!el) return;
        try {
          const rect = el.getBoundingClientRect();
          const clientX = rect.left + rect.width / 2;
          const clientY = rect.top + rect.height / 2;
          const opts = { bubbles: true, cancelable: true, view: window, clientX, clientY };
          el.focus();
          el.dispatchEvent(new PointerEvent('pointerdown', opts));
          el.dispatchEvent(new MouseEvent('mousedown', opts));
          el.dispatchEvent(new PointerEvent('pointerup', opts));
          el.dispatchEvent(new MouseEvent('mouseup', opts));
          el.click();
        } catch (_) {
          try { el.click(); } catch (_) {}
        }
      }

      function sendMeetHotkey(key, code) {
        const keyCode = code === 'KeyD' ? 68 : 69;
        const opts = {
          key: key,
          code: code,
          keyCode: keyCode,
          which: keyCode,
          ctrlKey: true,
          bubbles: true,
          cancelable: true,
          composed: true
        };
        try {
          document.dispatchEvent(new KeyboardEvent('keydown', opts));
          window.dispatchEvent(new KeyboardEvent('keydown', opts));
          document.dispatchEvent(new KeyboardEvent('keyup', opts));
          window.dispatchEvent(new KeyboardEvent('keyup', opts));
        } catch (_) {}
      }

      function makeItRed(btn, hotkeyLetter, hotkeyCode) {
        if (btn) {
          safeClick(btn);
          const children = btn.querySelectorAll('svg, span, i, div');
          for (const ch of children) {
            try {
              ch.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window }));
            } catch (_) {}
          }
        }
        sendMeetHotkey(hotkeyLetter, hotkeyCode);
      }

      function findMeetJoinButton() {
        const allBtns = Array.from(document.querySelectorAll('button, div[role="button"], span[role="button"]'));
        for (const b of allBtns) {
          const txt = (b.innerText || b.getAttribute('aria-label') || '').toLowerCase().trim();
          if (
            txt.includes('приєднатися') ||
            txt.includes('присоединиться') ||
            txt.includes('join now') ||
            txt.includes('ask to join') ||
            txt === 'войти' ||
            txt === 'увійти' ||
            txt === 'join' ||
            txt.includes('перейти')
          ) {
            const aria = (b.getAttribute('aria-label') || '').toLowerCase();
            if (!aria.includes('мікрофон') && !aria.includes('микрофон') && !aria.includes('камер') && !aria.includes('microphone')) {
              return b.closest('button, [role="button"]') || b;
            }
          }
        }
        return null;
      }

      let attempts = 0;
      let joinedDone = false;
      let lastMicAction = 0;
      let lastCamAction = 0;

      function tick() {
        attempts++;
        const now = Date.now();
        const { mic, cam } = getMeetButtons();

        // 1. Microphone: Check if RED. If NOT red, click it!
        let micIsRed = false;
        if (mic) {
          micIsRed = isElementRed(mic);
          if (!micIsRed && (now - lastMicAction > 400)) {
            console.log('[Luma AutoLesson] Микрофон БЕЛЫЙ -> нажимаем, чтобы стал КРАСНЫМ!');
            makeItRed(mic, 'd', 'KeyD');
            lastMicAction = now;
          }
        } else {
          if (attempts > 30) {
            micIsRed = true; // Fallback if no microphone device
          } else if (attempts <= 20 && attempts % 3 === 0) {
            sendMeetHotkey('d', 'KeyD');
          }
        }

        // 2. Camera: Check if RED. If NOT red, click it!
        let camIsRed = false;
        if (cam) {
          camIsRed = isElementRed(cam);
          if (!camIsRed && (now - lastCamAction > 400)) {
            console.log('[Luma AutoLesson] Камера БЕЛАЯ -> нажимаем, чтобы стала КРАСНОЙ!');
            makeItRed(cam, 'e', 'KeyE');
            lastCamAction = now;
          }
        } else {
          if (attempts > 30) {
            camIsRed = true; // Fallback if no camera device
          } else if (attempts <= 20 && attempts % 3 === 0) {
            sendMeetHotkey('e', 'KeyE');
          }
        }

        // 3. Confirmations/popups (e.g. "Присоединиться без микрофона")
        const dialogBtns = Array.from(document.querySelectorAll('[role="dialog"] button, .mGcWTe button, [data-mdc-dialog-action]'));
        for (const db of dialogBtns) {
          const dtxt = (db.innerText || db.getAttribute('aria-label') || '').toLowerCase();
          if (dtxt.includes('без микрофон') || dtxt.includes('без мікрофон') || dtxt.includes('без звука') || dtxt.includes('без звуку') || dtxt.includes('присоединиться') || dtxt.includes('приєднатися') || dtxt.includes('продолжить') || dtxt.includes('продовжити')) {
            safeClick(db);
          }
        }

        // 4. Auto-Join: ONLY when BOTH are verified RED!
        if (config.autoJoin) {
          const readyToJoin = config.muteMicAndCam ? (micIsRed && camIsRed) : true;
          if (readyToJoin && !joinedDone) {
            const joinBtn = findMeetJoinButton();
            if (joinBtn) {
              console.log('[Luma AutoLesson] Обе кнопки КРАСНЫЕ! Нажимаем Вход...');
              showToast('Автоурок: Микрофон и камера выключены (красные). Входим в урок!');
              safeClick(joinBtn);
              joinedDone = true;
            }
          }
        }

        // Stop polling after 120 attempts (30s) or 50 attempts after join
        if (attempts >= 120 || (joinedDone && attempts >= 60)) {
          clearInterval(timer);
          try { obs.disconnect(); } catch (_) {}
        }
      }

      const timer = setInterval(tick, 250);
      tick();

      let obs = null;
      try {
        obs = new MutationObserver(() => tick());
        const root = document.documentElement || document.body;
        if (root) {
          obs.observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['aria-label', 'data-is-muted', 'style', 'class'] });
        }
      } catch (_) {}
    }

    // ────────────────────── MODAL UI (LUMA GRAPHITE STYLE) ──────────────────────
    function escapeHtml(str) {
      if (!str) return '';
      return String(str).replace(/[&<>"']/g, m => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[m]);
    }

    function formatFileSize(bytes) {
      if (!bytes || bytes <= 0) return '';
      if (bytes < 1024) return bytes + ' B';
      if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
      return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
    }

    let currentUploadedImage = null; // { name, sizeStr, dataUrl, base64, mime }
    let isOcrRunning = false;
    let lastOcrError = null;
    let renderCurrentModal = null;

    let activeDay = new Date().getDay() || 1;
    if (activeDay === 0 || activeDay === 6) activeDay = 1;
    let activeMainTab = 'schedule'; // 'schedule' | 'settings' | 'import' | 'test'

    function toggleModal(open) {
      injectStyles();
      const existing = document.querySelector('.luma-autourok-overlay');
      if (existing) {
        existing.remove();
        if (!open) {
          renderCurrentModal = null;
          return;
        }
      }
      if (!open) {
        renderCurrentModal = null;
        return;
      }

      const overlay = document.createElement('div');
      overlay.id = 'luma-autourok-overlay';
      overlay.className = 'luma-autourok-overlay';

      const modal = document.createElement('div');
      modal.id = 'luma-autourok-modal';
      modal.className = 'luma-autourok-modal';

      function renderContent() {
        const { ongoing, next, currentSec } = getCurrentLessonInfo();
        let heroHtml = '';

        if (ongoing) {
          heroHtml = `
            <div class="luma-autourok-hero">
              <div class="luma-autourok-hero-top">
                <span>⚡ СЕЙЧАС ИДЁТ УРОК</span>
                <span>до ${ongoing.end}</span>
              </div>
              <h2 class="luma-autourok-hero-subject">${ongoing.subject}</h2>
              <div class="luma-autourok-hero-time">Время урока: ${ongoing.start} – ${ongoing.end}</div>
              <button class="luma-autourok-btn-join" id="luma-btn-join-now">
                ${lucideIcon('video', 18, '#ffffff')}
                Присоединиться к уроку в Google Meet
              </button>
            </div>
          `;
        } else if (next) {
          const nextStartSec = next.startMin * 60;
          const diffSec = Math.max(0, nextStartSec - currentSec);
          const mins = Math.floor(diffSec / 60);
          const secs = diffSec % 60;
          const timeStr = `${mins} мин ${secs < 10 ? '0' : ''}${secs} сек`;

          heroHtml = `
            <div class="luma-autourok-hero">
              <div class="luma-autourok-hero-top">
                <span>СЛЕДУЮЩИЙ УРОК</span>
                <span>через ${timeStr}</span>
              </div>
              <h2 class="luma-autourok-hero-subject">${next.subject}</h2>
              <div class="luma-autourok-hero-time">Начало в ${next.start} (до ${next.end})</div>
              <button class="luma-autourok-btn-join" id="luma-btn-join-now">
                ${lucideIcon('video', 18, '#ffffff')}
                Найти курс и войти в Meet
              </button>
            </div>
          `;
        } else {
          heroHtml = `
            <div class="luma-autourok-hero">
              <div class="luma-autourok-hero-top">
                <span>УРОКОВ НЕТ</span>
              </div>
              <h2 class="luma-autourok-hero-subject">Отдых</h2>
              <div class="luma-autourok-hero-time">На сегодня уроки не запланированы</div>
            </div>
          `;
        }

        const days = [
          { id: 1, name: 'Пн' },
          { id: 2, name: 'Вт' },
          { id: 3, name: 'Ср' },
          { id: 4, name: 'Чт' },
          { id: 5, name: 'Пт' },
          { id: 6, name: 'Сб' }
        ];

        let tabBody = '';
        if (activeMainTab === 'schedule') {
          const currentDayLessons = config.schedule[activeDay] || [];
          const rowsHtml = currentDayLessons.map((l, i) => `
            <div class="luma-autourok-lesson-row" data-idx="${i}">
              <div class="luma-autourok-lesson-num">${i + 1}</div>
              <input class="luma-autourok-input luma-autourok-input-name" type="text" value="${l.subject || ''}" placeholder="Предмет">
              <input class="luma-autourok-input luma-autourok-input-time" type="text" value="${l.start || ''}" placeholder="08:30">
              <span style="color:#5e6070;">–</span>
              <input class="luma-autourok-input luma-autourok-input-time" type="text" value="${l.end || ''}" placeholder="09:15">
              <button class="luma-autourok-del-btn" title="Удалить">${lucideIcon('trash', 16, 'currentColor')}</button>
            </div>
          `).join('');

          tabBody = `
            <div class="luma-autourok-days">
              ${days.map(d => `<div class="luma-autourok-day-pill ${d.id === activeDay ? 'active' : ''}" data-day="${d.id}">${d.name}</div>`).join('')}
            </div>
            <div id="luma-schedule-list">${rowsHtml}</div>
            <div class="luma-autourok-actions-bar">
              <button class="luma-autourok-btn-secondary" id="luma-btn-add-lesson">
                ${lucideIcon('plus', 16, 'currentColor')} Добавить урок
              </button>
              <button class="luma-autourok-btn-primary" id="luma-btn-save-schedule">
                ${lucideIcon('save', 16, '#ffffff')} Сохранить
              </button>
            </div>
          `;
        } else if (activeMainTab === 'settings') {
          tabBody = `
            <div class="luma-test-card">
              <div class="luma-setting-row">
                <div>
                  <div class="luma-setting-label">Автоматический вход в Google Meet</div>
                  <div class="luma-setting-desc">ИИ находит нужный курс и открывает видеовстречу</div>
                </div>
                <input class="luma-switch" type="checkbox" id="luma-cfg-autojoin" ${config.autoJoin ? 'checked' : ''}>
              </div>
              <div class="luma-setting-row">
                <div>
                  <div class="luma-setting-label">Отключать микрофон и камеру</div>
                  <div class="luma-setting-desc">Автоматически глушит микрофон и выключает видео при входе</div>
                </div>
                <input class="luma-switch" type="checkbox" id="luma-cfg-mute" ${config.muteMicAndCam ? 'checked' : ''}>
              </div>
              <div class="luma-setting-row">
                <div>
                  <div class="luma-setting-label">Уведомления в браузере</div>
                  <div class="luma-setting-desc">Показывать уведомление о начале урока</div>
                </div>
                <input class="luma-switch" type="checkbox" id="luma-cfg-notify" ${config.notify ? 'checked' : ''}>
              </div>
              <div class="luma-setting-row">
                <div>
                  <div class="luma-setting-label">Входить за N минут до звонка</div>
                  <div class="luma-setting-desc">Запас времени до начала урока (в минутах)</div>
                </div>
                <input class="luma-autourok-input" style="width:50px;text-align:center;" type="number" id="luma-cfg-lead" value="${config.leadMinutes || 2}" min="0" max="15">
              </div>
            </div>
            <button class="luma-autourok-btn-primary" style="width:100%;margin-top:10px;" id="luma-btn-save-settings">
              ${lucideIcon('save', 16, '#ffffff')} Сохранить настройки
            </button>
          `;
        } else if (activeMainTab === 'import') {
          let dropzoneHtml = '';
          if (currentUploadedImage && currentUploadedImage.dataUrl) {
            dropzoneHtml = `
              <div class="luma-dropzone" id="luma-schedule-dropzone" style="cursor:default;padding:16px 12px;display:flex;flex-direction:column;align-items:center;text-align:center;">
                <div style="position:relative;max-width:100%;margin-bottom:8px;">
                  <img src="${currentUploadedImage.dataUrl}" style="max-height:170px;max-width:100%;border-radius:8px;border:1px solid #3c3e4f;box-shadow:0 6px 20px rgba(0,0,0,0.5);object-fit:contain;" alt="Фото расписания">
                  <div style="position:absolute;top:6px;right:6px;background:rgba(24,25,32,0.88);backdrop-filter:blur(8px);border:1px solid #3d3e52;padding:3px 8px;border-radius:6px;font-size:10px;color:#4ade80;font-weight:600;display:flex;align-items:center;gap:4px;">
                    ${lucideIcon('check', 11, '#4ade80')} Фото выбрано
                  </div>
                </div>
                <div style="display:flex;align-items:center;gap:8px;margin-bottom:10px;">
                  <span style="font-size:12.5px;font-weight:600;color:#ffffff;max-width:240px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;">${escapeHtml(currentUploadedImage.name || 'Фото расписания')}</span>
                  ${currentUploadedImage.sizeStr ? `<span style="font-size:11px;color:#a1a1aa;background:#242531;padding:2px 8px;border-radius:10px;">${escapeHtml(currentUploadedImage.sizeStr)}</span>` : ''}
                </div>
                <div style="display:flex;gap:8px;">
                  <button type="button" class="luma-autourok-btn-secondary" style="padding:6px 14px;font-size:11.5px;" id="luma-btn-change-photo">
                    ${lucideIcon('refreshCw', 13, '#a78bfa')} Выбрать другое фото
                  </button>
                  <button type="button" class="luma-autourok-btn-primary" style="padding:6px 14px;font-size:11.5px;" id="luma-btn-retrigger-ocr">
                    ${lucideIcon('sparkles', 13, '#ffffff')} Распознать повторно
                  </button>
                </div>
              </div>
            `;
          } else {
            dropzoneHtml = `
              <label for="luma-schedule-file-input" class="luma-dropzone" id="luma-schedule-dropzone" style="cursor:pointer;display:flex;flex-direction:column;align-items:center;text-align:center;">
                <div class="luma-dropzone-icon">${lucideIcon('camera', 32, '#7C5CE4')}</div>
                <div class="luma-dropzone-title">Перетащите сюда фото или нажмите для выбора</div>
                <div class="luma-dropzone-desc">Поддерживаются любые фото и скриншоты (PNG, JPG, WEBP). Также работает: <b>Ctrl + V</b></div>
                <div style="display:flex;gap:8px;margin-top:6px;" onclick="event.stopPropagation();">
                  <button type="button" class="luma-autourok-btn-secondary" style="padding:6px 16px;font-size:12px;" id="luma-btn-open-explorer">
                    ${lucideIcon('folder', 14, '#a78bfa')} Выбрать через Проводник
                  </button>
                </div>
              </label>
            `;
          }

          let ocrStatusHtml = '';
          if (isOcrRunning) {
            ocrStatusHtml = `
              <div class="luma-ocr-loading" style="display:flex;align-items:center;gap:12px;padding:12px 14px;background:rgba(124,92,228,0.12);border:1px solid rgba(124,92,228,0.4);border-radius:10px;margin-top:12px;">
                <div class="luma-spinner" style="width:20px;height:20px;border:2.5px solid rgba(124,92,228,0.3);border-top-color:#a78bfa;border-radius:50%;animation:lumaSpin 0.7s linear infinite;"></div>
                <div style="flex:1;">
                  <div style="font-size:12.5px;font-weight:600;color:#ffffff;">LumaAI распознает расписание уроков и звонков...</div>
                  <div style="font-size:11px;color:#a1a1aa;margin-top:2px;">Анализирую фото, дни недели и звонки уроков. Подождите пару секунд...</div>
                </div>
              </div>
            `;
          } else if (lastOcrError) {
            ocrStatusHtml = `
              <div style="display:flex;align-items:center;gap:10px;color:#f87171;font-size:12px;padding:10px 14px;background:rgba(239,68,68,0.12);border:1px solid rgba(239,68,68,0.3);border-radius:10px;margin-top:12px;">
                ${lucideIcon('alertCircle', 16, '#f87171')}
                <div style="flex:1;">${escapeHtml(lastOcrError)}</div>
                <button type="button" class="luma-autourok-btn-secondary" style="padding:4px 10px;font-size:11px;" id="luma-btn-ocr-retry">Повторить</button>
              </div>
            `;
          }

          tabBody = `
            <div class="luma-test-card">
              <div style="font-size:13px;font-weight:600;color:#ffffff;margin-bottom:4px;">1. Фото расписания уроков и звонков</div>
              <div style="font-size:11.5px;color:#8c8ea0;margin-bottom:12px;">
                Загрузите фото расписания или скриншот. ИИ LumaAI сам определит предметы, дни недели и время звонков.
              </div>

              ${dropzoneHtml}
              <input type="file" id="luma-schedule-file-input" accept="image/*, .png, .jpg, .jpeg, .webp, .bmp, .jfif, *.*" style="position:fixed;top:-1000px;left:-1000px;opacity:0;">

              <div id="luma-ocr-status-container">${ocrStatusHtml}</div>

              <div style="font-size:13px;font-weight:600;color:#ffffff;margin-top:14px;margin-bottom:4px;">2. Или вставьте текстом</div>
              <div style="font-size:11.5px;color:#8c8ea0;margin-bottom:8px;">
                Для выбранного дня (${days.find(d => d.id === activeDay)?.name || ''}) в формате: <code>Предмет 08:30-09:15</code>
              </div>
              <textarea id="luma-import-text" class="luma-autourok-input" style="width:100%;height:90px;box-sizing:border-box;resize:vertical;" placeholder="Алгебра 08:30-09:15&#10;Геометрия 09:25-10:10&#10;Физика 10:20-11:05"></textarea>
            </div>
            <button class="luma-autourok-btn-secondary" style="width:100%;" id="luma-btn-parse-import">
              ${lucideIcon('fileText', 16, 'currentColor')} Применить текст для текущего дня
            </button>
          `;
        } else if (activeMainTab === 'test') {
          // Dedicated Dev Tab for dakowerr@gmail.com
          tabBody = `
            <div class="luma-test-card">
              <div class="luma-test-badge">DEV ТЕСТ ДЛЯ DAKOWERR@GMAIL.COM</div>
              <div style="font-size:14px;font-weight:600;color:#ffffff;margin-bottom:4px;">Тестирование ИИ поиска и заголовков</div>
              <div style="font-size:12px;color:#8c8ea0;margin-bottom:10px;">
                Кликните по любому предмету из списка 15 курсов для мгновенной проверки сопоставления:
              </div>
              <div class="luma-test-chips">
                <span class="luma-test-chip" data-q="Зарубежная литература">1. 11 - В | Зарубіжна література</span>
                <span class="luma-test-chip" data-q="Геометрия">2. Геометрия ➔ Математика 11-В</span>
                <span class="luma-test-chip" data-q="Алгебра">3. Алгебра ➔ Математика 11-В</span>
                <span class="luma-test-chip" data-q="Математика">4. Математика 11-В</span>
                <span class="luma-test-chip" data-q="Украинская литература">5. УКРАЇНСЬКА ЛІТЕРАТУРА | 11-В</span>
                <span class="luma-test-chip" data-q="Украинский язык">6. УКРАЇНСЬКА МОВА | 11-В</span>
                <span class="luma-test-chip" data-q="Химия">7. 11-В ХІМІЯ</span>
                <span class="luma-test-chip" data-q="Биология">8. 11-В Біологія (2026-2027)</span>
                <span class="luma-test-chip" data-q="English">9. 11-B English</span>
                <span class="luma-test-chip" data-q="Физика">10. Фізика | 11-В</span>
                <span class="luma-test-chip" data-q="Астрономия">11. Астрономія | 11-В</span>
                <span class="luma-test-chip" data-q="Физкультура">12. Фізична культура 11-В</span>
                <span class="luma-test-chip" data-q="Всемирная история">13. 11-В клас. Всесвітня історія.</span>
                <span class="luma-test-chip" data-q="История Украины">14. 11-В клас. Історія України.</span>
                <span class="luma-test-chip" data-q="География">15. 11-В | Географія</span>
                <span class="luma-test-chip" data-q="Технологии">16. 11-В ТЕХНОЛОГІЇ</span>
                <span class="luma-test-chip" data-q="Искусство">17. 11В Мистецтво</span>
              </div>
              <div style="display:flex;gap:8px;margin-bottom:10px;">
                <input class="luma-autourok-input" id="luma-test-query-input" style="flex:1;" type="text" value="Зарубежная литература" placeholder="Название урока для проверки...">
              </div>
              <div style="display:flex;gap:8px;">
                <button class="luma-autourok-btn-secondary" id="luma-btn-run-scan-test" style="flex:1;">
                  ${lucideIcon('search', 16, 'currentColor')} 1. Тест сопоставления
                </button>
                <button class="luma-autourok-btn-primary" id="luma-btn-run-full-test" style="flex:1;">
                  ${lucideIcon('video', 16, '#ffffff')} 2. Тест авто-входа в Meet
                </button>
              </div>
            </div>
            <div style="font-size:11px;font-weight:600;color:#7C5CE4;text-transform:uppercase;margin:6px 0;">Лог работы ИИ-поиска:</div>
            <div class="luma-test-console" id="luma-test-console">[Готов к тестированию. Нажмите на чип выше.]</div>
          `;
        }

        setSafeHTML(modal, `
          <div class="luma-autourok-header">
            <div class="luma-autourok-title-group">
              ${lucideIcon('cap', 22, '#7C5CE4')}
              <h3 class="luma-autourok-title">Автоурок</h3>
            </div>
            <div class="luma-autourok-header-actions">
              <div class="luma-autourok-status-badge ${config.enabled ? '' : 'paused'}" id="luma-toggle-status">
                ${lucideIcon('radio', 13, config.enabled ? '#4ade80' : '#f87171')}
                <span>${config.enabled ? 'Активен' : 'Пауза'}</span>
              </div>
              <button class="luma-autourok-close" id="luma-modal-close" title="Закрыть">
                ${lucideIcon('x', 18, 'currentColor')}
              </button>
            </div>
          </div>
          <div class="luma-autourok-content">
            ${heroHtml}
            <div class="luma-autourok-tabs">
              <button class="luma-autourok-tab ${activeMainTab === 'schedule' ? 'active' : ''}" data-tab="schedule">
                ${lucideIcon('calendar', 15, 'currentColor')} Расписание
              </button>
              <button class="luma-autourok-tab ${activeMainTab === 'settings' ? 'active' : ''}" data-tab="settings">
                ${lucideIcon('sliders', 15, 'currentColor')} Настройки
              </button>
              <button class="luma-autourok-tab ${activeMainTab === 'import' ? 'active' : ''}" data-tab="import">
                ${lucideIcon('fileText', 15, 'currentColor')} Импорт
              </button>
              <button class="luma-autourok-tab ${activeMainTab === 'test' ? 'active' : ''}" data-tab="test">
                ${lucideIcon('flask', 15, 'currentColor')} ИИ Тест (dakowerr)
              </button>
            </div>
            ${tabBody}
          </div>
        `);

        // Event listeners
        const closeBtn = modal.querySelector('#luma-modal-close');
        if (closeBtn) closeBtn.onclick = () => toggleModal(false);

        const toggleStatusBtn = modal.querySelector('#luma-toggle-status');
        if (toggleStatusBtn) {
          toggleStatusBtn.onclick = () => {
            config.enabled = !config.enabled;
            saveConfig(config);
            renderContent();
          };
        }

        const joinBtn = modal.querySelector('#luma-btn-join-now');
        if (joinBtn) {
          joinBtn.onclick = () => {
            const target = ongoing || next;
            if (target) {
              toggleModal(false);
              triggerJoinLesson(target.subject);
            }
          };
        }

        // Tab switching
        modal.querySelectorAll('.luma-autourok-tab').forEach(btn => {
          btn.onclick = () => {
            activeMainTab = btn.getAttribute('data-tab');
            renderContent();
          };
        });

        // Day switching in schedule tab
        modal.querySelectorAll('.luma-autourok-day-pill').forEach(btn => {
          btn.onclick = () => {
            activeDay = parseInt(btn.getAttribute('data-day'), 10);
            renderContent();
          };
        });

        // Add lesson
        const addBtn = modal.querySelector('#luma-btn-add-lesson');
        if (addBtn) {
          addBtn.onclick = () => {
            if (!config.schedule[activeDay]) config.schedule[activeDay] = [];
            config.schedule[activeDay].push({ subject: '', start: '', end: '' });
            renderContent();
          };
        }

        // Delete lesson
        modal.querySelectorAll('.luma-autourok-del-btn').forEach(btn => {
          btn.onclick = (e) => {
            const row = e.target.closest('.luma-autourok-lesson-row');
            if (row) {
              const idx = parseInt(row.getAttribute('data-idx'), 10);
              config.schedule[activeDay].splice(idx, 1);
              renderContent();
            }
          };
        });

        // Save schedule
        const saveSchedBtn = modal.querySelector('#luma-btn-save-schedule');
        if (saveSchedBtn) {
          saveSchedBtn.onclick = () => {
            const rows = modal.querySelectorAll('.luma-autourok-lesson-row');
            const newLessons = [];
            rows.forEach(r => {
              const subject = r.querySelector('.luma-autourok-input-name').value.trim();
              const times = r.querySelectorAll('.luma-autourok-input-time');
              const start = times[0] ? times[0].value.trim() : '';
              const end = times[1] ? times[1].value.trim() : '';
              if (subject) newLessons.push({ subject, start, end });
            });
            config.schedule[activeDay] = newLessons;
            saveConfig(config);
            showToast('Расписание успешно сохранено!');
            renderContent();
          };
        }

        // Save settings
        const saveCfgBtn = modal.querySelector('#luma-btn-save-settings');
        if (saveCfgBtn) {
          saveCfgBtn.onclick = () => {
            config.autoJoin = modal.querySelector('#luma-cfg-autojoin').checked;
            config.muteMicAndCam = modal.querySelector('#luma-cfg-mute').checked;
            config.notify = modal.querySelector('#luma-cfg-notify').checked;
            config.leadMinutes = parseInt(modal.querySelector('#luma-cfg-lead').value, 10) || 2;
            saveConfig(config);
            showToast('Настройки обновлены!');
            renderContent();
          };
        }

        // Handle image schedule upload & OCR
        function triggerOcrForImage(imgData) {
          if (!imgData || !imgData.base64) return;
          isOcrRunning = true;
          lastOcrError = null;
          renderContent();
          showToast('LumaAI: Анализирую фото расписания и звонков...');
          if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage({
              kind: 'luma-autourok-ocr-schedule',
              imageBase64: imgData.base64,
              mimeType: imgData.mime || 'image/jpeg'
            });
          } else {
            setTimeout(() => {
              isOcrRunning = false;
              lastOcrError = 'Для ИИ-распознавания фото используйте браузер Luma';
              renderContent();
            }, 1200);
          }
        }

        function handleScheduleImageFile(file) {
          if (!file) return;
          const name = file.name || 'image.png';
          const size = file.size || 0;
          const reader = new FileReader();
          reader.onload = (e) => {
            const dataUrl = e.target.result;
            const b64 = dataUrl.includes(',') ? dataUrl.split(',')[1] : '';
            let mime = file.type;
            if (!mime || !mime.startsWith('image/')) {
              if (name.toLowerCase().endsWith('.png')) mime = 'image/png';
              else if (name.toLowerCase().endsWith('.webp')) mime = 'image/webp';
              else if (name.toLowerCase().endsWith('.bmp')) mime = 'image/bmp';
              else mime = 'image/jpeg';
            }
            currentUploadedImage = {
              name: name,
              sizeStr: formatFileSize(size),
              dataUrl: dataUrl,
              base64: b64,
              mime: mime
            };
            triggerOcrForImage(currentUploadedImage);
          };
          reader.readAsDataURL(file);
        }

        const dropzone = modal.querySelector('#luma-schedule-dropzone');
        const fileInput = modal.querySelector('#luma-schedule-file-input');
        const explorerBtn = modal.querySelector('#luma-btn-open-explorer');
        const changePhotoBtn = modal.querySelector('#luma-btn-change-photo');
        const retriggerOcrBtn = modal.querySelector('#luma-btn-retrigger-ocr');
        const retryOcrBtn = modal.querySelector('#luma-btn-ocr-retry');

        const openNativePicker = (e) => {
          if (e) {
            e.preventDefault();
            e.stopPropagation();
          }
          if (window.chrome && window.chrome.webview) {
            isOcrRunning = true;
            lastOcrError = null;
            renderContent();
            window.chrome.webview.postMessage({ kind: 'luma-autourok-pick-image' });
          } else if (fileInput) {
            fileInput.value = '';
            fileInput.click();
          }
        };

        if (explorerBtn) explorerBtn.onclick = openNativePicker;
        if (changePhotoBtn) changePhotoBtn.onclick = openNativePicker;

        if (retriggerOcrBtn) {
          retriggerOcrBtn.onclick = () => {
            if (currentUploadedImage) triggerOcrForImage(currentUploadedImage);
          };
        }

        if (retryOcrBtn) {
          retryOcrBtn.onclick = () => {
            if (currentUploadedImage) triggerOcrForImage(currentUploadedImage);
          };
        }

        if (fileInput) {
          fileInput.onchange = (e) => {
            e.stopPropagation();
            if (fileInput.files && fileInput.files.length > 0) {
              handleScheduleImageFile(fileInput.files[0]);
            }
          };
        }

        if (dropzone) {
          dropzone.ondragover = (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.add('dragover');
          };
          dropzone.ondragenter = (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.add('dragover');
          };
          dropzone.ondragleave = (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.remove('dragover');
          };
          dropzone.ondrop = (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropzone.classList.remove('dragover');
            if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length > 0) {
              handleScheduleImageFile(e.dataTransfer.files[0]);
            }
          };
        }

        // Parse text import
        const parseImportBtn = modal.querySelector('#luma-btn-parse-import');
        if (parseImportBtn) {
          parseImportBtn.onclick = () => {
            const txt = modal.querySelector('#luma-import-text').value;
            const lines = txt.split('\n').map(l => l.trim()).filter(Boolean);
            const parsed = [];
            for (const l of lines) {
              const timeMatch = l.match(/(\d{1,2}:\d{2})\s*[-–—]\s*(\d{1,2}:\d{2})/);
              if (timeMatch) {
                const sub = l.replace(timeMatch[0], '').replace(/^\d+[\s.)]*/, '').trim();
                parsed.push({ subject: sub, start: timeMatch[1], end: timeMatch[2] });
              } else {
                parsed.push({ subject: l.replace(/^\d+[\s.)]*/, '').trim(), start: '', end: '' });
              }
            }
            if (parsed.length > 0) {
              config.schedule[activeDay] = parsed;
              saveConfig(config);
              showToast(`Импортировано ${parsed.length} уроков!`);
              activeMainTab = 'schedule';
              renderContent();
            }
          };
        }

        // Dev / Test tab handlers
        const consoleEl = modal.querySelector('#luma-test-console');
        const appendLog = (msg) => {
          if (!consoleEl) return;
          const time = new Date().toLocaleTimeString();
          consoleEl.textContent += `\n[${time}] ${msg}`;
          consoleEl.scrollTop = consoleEl.scrollHeight;
        };

        modal.querySelectorAll('.luma-test-chip').forEach(chip => {
          chip.onclick = () => {
            const q = chip.getAttribute('data-q');
            const qInput = modal.querySelector('#luma-test-query-input');
            if (qInput) qInput.value = q;
            const scanBtn = modal.querySelector('#luma-btn-run-scan-test');
            if (scanBtn) scanBtn.click();
          };
        });

        const scanTestBtn = modal.querySelector('#luma-btn-run-scan-test');
        if (scanTestBtn) {
          scanTestBtn.onclick = () => {
            const q = modal.querySelector('#luma-test-query-input')?.value || '';
            if (consoleEl) consoleEl.textContent = `=== ТЕСТ СОПОСТАВЛЕНИЯ ДЛЯ: "${q}" ===`;
            appendLog(`Сбор курсов на странице Google Classroom...`);
            let courses = scanPageCourses();
            if (courses.length === 0) {
              appendLog(`Внимание: активных карточек в DOM не обнаружено (страница загружается или открыт Meet).`);
              appendLog(`Использую список 15 реальных курсов пользователя (Screen 3) для верификации:`);
              courses = MOCK_COURSES;
            } else {
              appendLog(`Найдено активных курсов в DOM: ${courses.length}`);
            }
            const match = matchCourse(q, courses);
            appendLog(`\nРЕЗУЛЬТАТЫ СКАНИРОВАНИЯ:`);
            match.results.forEach((c, i) => {
              const subInfo = c.subtitle ? ` | Подзаголовок: "${c.subtitle}"` : '';
              const ruleNote = c.matchedRule ? ` [БАЗА: ${c.matchedRule}]` : '';
              const line = `  [#${i+1}] "${c.title}"${subInfo} -> Балл: ${c.score}%${ruleNote}`;
              appendLog(line);
            });
            if (match.best) {
              appendLog(`\nИТОГ ИИ: Выбран курс #${match.results.indexOf(match.best) + 1}`);
              appendLog(`  -> Заголовок: "${match.best.title}"`);
              if (match.best.subtitle) appendLog(`  -> Подзаголовок: "${match.best.subtitle}"`);
              if (match.best.matchedRule) {
                appendLog(`  -> [ТОЧНОЕ СОВПАДЕНИЕ ИЗ БАЗЫ КЛАССОВ 11-В]: "${match.best.matchedRule}"`);
                appendLog(`  -> Балл совпадения: 100% (ПОПАДАНИЕ 100%)`);
              } else {
                appendLog(`  -> Балл совпадения: ${match.best.score}%`);
              }
              if (match.best.url) appendLog(`  -> URL: ${match.best.url}`);
            } else {
              appendLog(`\nИТОГ ИИ: Ни один курс не набрал порог 60%.`);
            }
          };
        }

        const fullTestBtn = modal.querySelector('#luma-btn-run-full-test');
        if (fullTestBtn) {
          fullTestBtn.onclick = () => {
            const q = modal.querySelector('#luma-test-query-input')?.value || '';
            if (consoleEl) consoleEl.textContent = `=== ПОЛНЫЙ ТЕСТ АВТО-ВХОДА ДЛЯ: "${q}" ===`;
            triggerJoinLesson(q, appendLog);
          };
        }
      }

      renderContent();
      overlay.appendChild(modal);
      (document.body || document.documentElement).appendChild(overlay);

      overlay.onclick = (e) => {
        if (e.target === overlay) {
          renderCurrentModal = null;
          toggleModal(false);
        }
      };

      document.addEventListener('keydown', function escHandler(e) {
        if (e.key === 'Escape') {
          renderCurrentModal = null;
          toggleModal(false);
          document.removeEventListener('keydown', escHandler);
        }
      });
    }

    function applyOcrResult(data) {
      isOcrRunning = false;
      if (!data || !data.success) {
        const err = data?.error || 'Не удалось распознать расписание';
        lastOcrError = err;
        showToast(`Ошибка: ${err}`);
        if (renderCurrentModal) renderCurrentModal();
        return;
      }

      let scheduleObj = data.schedule;
      if (typeof scheduleObj === 'string') {
        try { scheduleObj = JSON.parse(scheduleObj); } catch (_) {}
      }

      if (!scheduleObj || typeof scheduleObj !== 'object') {
        lastOcrError = 'Не удалось извлечь уроки из ответа ИИ';
        showToast(lastOcrError);
        if (renderCurrentModal) renderCurrentModal();
        return;
      }

      let count = 0;
      for (let d = 1; d <= 6; d++) {
        const dayKey = String(d);
        if (Array.isArray(scheduleObj[dayKey]) && scheduleObj[dayKey].length > 0) {
          config.schedule[d] = scheduleObj[dayKey].map(item => ({
            subject: (item.subject || item.name || '').trim(),
            start: (item.start || item.start_time || '').trim(),
            end: (item.end || item.end_time || '').trim()
          })).filter(item => item.subject);
          count += config.schedule[d].length;
        }
      }

      if (count === 0 && Array.isArray(scheduleObj)) {
        config.schedule[activeDay] = scheduleObj.map(item => ({
          subject: (item.subject || item.name || '').trim(),
          start: (item.start || item.start_time || '').trim(),
          end: (item.end || item.end_time || '').trim()
        })).filter(item => item.subject);
        count = config.schedule[activeDay].length;
      }

      lastOcrError = null;
      saveConfig(config);
      showToast(`ИИ успешно распознал и сохранил ${count} уроков!`);
      activeMainTab = 'schedule';
      if (renderCurrentModal) renderCurrentModal();
    }

    function showImagePreview(payload) {
      if (typeof payload === 'string') {
        try { payload = JSON.parse(payload); } catch (_) {}
      }
      if (!payload) return;
      if (payload.kind === 'luma-autourok-picker-cancelled') {
        isOcrRunning = false;
        if (renderCurrentModal) renderCurrentModal();
        return;
      }
      if (payload.dataUrl) {
        const b64 = payload.imageBase64 || (payload.dataUrl.includes(',') ? payload.dataUrl.split(',')[1] : '');
        currentUploadedImage = {
          name: payload.fileName || 'Фото расписания',
          sizeStr: formatFileSize(payload.fileSize),
          dataUrl: payload.dataUrl,
          base64: b64,
          mime: payload.mimeType || 'image/jpeg'
        };
        isOcrRunning = true;
        lastOcrError = null;
        if (activeMainTab !== 'import') activeMainTab = 'import';
        if (renderCurrentModal) renderCurrentModal();
        showToast('LumaAI: Фото выбрано! Распознаю расписание...');
      }
    }

    window.__lumaShowPreview = showImagePreview;
    window.__lumaApplyOcr = applyOcrResult;

    window.addEventListener('luma-autourok-preview-image', (e) => showImagePreview(e.detail));
    window.addEventListener('luma-autourok-ocr-result', (e) => applyOcrResult(e.detail));

    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.addEventListener('message', (e) => {
        if (!e.data) return;
        if (e.data.kind === 'luma-autourok-preview-image' || e.data.kind === 'luma-autourok-picker-cancelled') {
          showImagePreview(e.data);
        } else if (e.data.kind === 'luma-autourok-ocr-result') {
          applyOcrResult(e.data);
        }
      });
    }

    // Global paste support: pressing Ctrl+V with an image
    window.addEventListener('paste', (e) => {
      const items = e.clipboardData?.items;
      if (!items) return;
      for (const item of items) {
        if (item.type && item.type.startsWith('image/')) {
          const file = item.getAsFile();
          if (file) {
            toggleModal(true);
            activeMainTab = 'import';
            if (renderCurrentModal) renderCurrentModal();
            const name = file.name || 'clipboard.png';
            const size = file.size || 0;
            const reader = new FileReader();
            reader.onload = (ev) => {
              const dataUrl = ev.target.result;
              const b64 = dataUrl.includes(',') ? dataUrl.split(',')[1] : '';
              currentUploadedImage = {
                name: name,
                sizeStr: formatFileSize(size),
                dataUrl: dataUrl,
                base64: b64,
                mime: file.type || 'image/png'
              };
              isOcrRunning = true;
              lastOcrError = null;
              if (renderCurrentModal) renderCurrentModal();
              showToast('LumaAI: Фото из буфера обмена получено! Распознаю расписание...');
              if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage({
                  kind: 'luma-autourok-ocr-schedule',
                  imageBase64: b64,
                  mimeType: file.type || 'image/png'
                });
              }
            };
            reader.readAsDataURL(file);
            break;
          }
        }
      }
    });

    // ────────────────────── INITIALIZATION ──────────────────────
    setInterval(runBackgroundCheck, 12000);
    if (isMeet) {
      handleGoogleMeet();
      if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', handleGoogleMeet);
      }
      window.addEventListener('load', handleGoogleMeet);
    }
  } catch (err) {
    console.error('Luma AutoLesson error:', err);
  }
})();
