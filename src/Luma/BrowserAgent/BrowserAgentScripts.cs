namespace Luma.BrowserAgent;

public static class BrowserAgentScripts
{
    /// <summary>
    /// Injected into active store pages to extract product tiles, prices, specs, and buy buttons.
    /// Works with Rozetka, Telemart, Comfy, Brain, Hotline, Allo, Foxtrot, and generic stores.
    /// </summary>
    public const string ScanECommercePageScript = """
    (() => {
        try {
            const results = [];
            // Common product card selectors
            const cardSelectors = [
                '.goods-tile', '.product-item', '.product-card', '.product-wrapper',
                '.catalog-item', '.products-layout__item', '[data-product-id]',
                'article.product', '.item-card', '.c-product-card'
            ];
            
            let cards = [];
            for (const sel of cardSelectors) {
                const found = document.querySelectorAll(sel);
                if (found.length > 0) {
                    cards = Array.from(found);
                    break;
                }
            }

            if (cards.length === 0) {
                // Generic fallback for any repeating article/div with prices
                cards = Array.from(document.querySelectorAll('article, div[class*="product"], div[class*="goods"]'))
                    .filter(c => c.textContent.match(/\d{2,3}[\s.,]?\d{3}\s*(?:грн|₴|uah)/i));
            }

            for (const card of cards.slice(0, 15)) {
                // Title
                const titleEl = card.querySelector('a[class*="title"], .goods-tile__title, .product-title, h2, h3, a[href*="/p"]') || card.querySelector('a');
                const title = titleEl ? (titleEl.textContent || titleEl.getAttribute('title') || '').trim() : '';
                const link = titleEl && titleEl.href ? titleEl.href : window.location.href;

                // Price
                const priceMatch = card.textContent.match(/(\d{1,3}(?:[\s.,]\d{3})+|\d{4,6})\s*(?:грн|₴|uah|\$|€)?/i);
                let price = 0;
                let currency = '₴';
                if (priceMatch) {
                    const cleanNum = priceMatch[1].replace(/[\s.,]/g, '');
                    price = parseInt(cleanNum, 10) || 0;
                }

                // Specs description
                const specsEl = card.querySelector('.goods-tile__description, .product-specs, .short-desc, ul');
                const specs = specsEl ? specsEl.textContent.trim() : '';

                // Buy button selector or presence
                const buyBtn = card.querySelector('button[class*="buy"], button[class*="cart"], button[class*="basket"], [data-qa="buy-button"], a[class*="buy"]');
                const hasBuyBtn = !!buyBtn;

                if (title && price > 0) {
                    results.push({
                        title: title.slice(0, 140),
                        price: price,
                        currency: currency,
                        url: link,
                        specs: specs.slice(0, 200),
                        hasBuyBtn: hasBuyBtn
                    });
                }
            }

            return JSON.stringify(results);
        } catch(e) {
            return JSON.stringify([]);
        }
    })()
    """;

    /// <summary>
    /// Clicks the buy/cart button on the active page, animating a visual Luma glow ring.
    /// </summary>
    public const string ClickBuyButtonScript = """
    (() => {
        try {
            // Find the buy button using various strategies
            const selectors = [
                'button[class*="buy"]',
                'button[class*="cart"]',
                'button[class*="basket"]',
                '[data-qa="buy-button"]',
                'a[class*="buy"]',
                'button.btn-success',
                'button.primary-btn'
            ];

            let button = null;
            for (const sel of selectors) {
                const b = document.querySelector(sel);
                if (b && b.offsetParent !== null) {
                    button = b;
                    break;
                }
            }

            if (!button) {
                // Search by button text
                const allButtons = Array.from(document.querySelectorAll('button, a, div[role="button"]'));
                button = allButtons.find(b => {
                    const t = (b.textContent || '').trim().toLowerCase();
                    return (t === 'купить' || t === 'купити' || t === 'в корзину' || t === 'до кошика' || t === 'в кошик' || t === 'add to cart' || t === 'додати в кошик');
                });
            }

            if (!button) return JSON.stringify({ success: false, reason: 'Buy button not found' });

            // Visual Highlight (Luma Agent pulse)
            button.scrollIntoView({ behavior: 'smooth', block: 'center' });
            
            const pulse = document.createElement('div');
            pulse.id = '__luma_agent_pulse';
            pulse.style.position = 'fixed';
            pulse.style.pointerEvents = 'none';
            pulse.style.zIndex = '999999';
            pulse.style.border = '3px solid #9f86ff';
            pulse.style.boxShadow = '0 0 25px #9f86ff, inset 0 0 15px #9f86ff';
            pulse.style.borderRadius = '12px';
            pulse.style.transition = 'all 0.3s ease-out';

            const rect = button.getBoundingClientRect();
            pulse.style.left = (rect.left - 4) + 'px';
            pulse.style.top = (rect.top - 4) + 'px';
            pulse.style.width = (rect.width + 8) + 'px';
            pulse.style.height = (rect.height + 8) + 'px';
            document.body.appendChild(pulse);

            setTimeout(() => {
                try {
                    // Full mouse interaction events sequence
                    ['mouseover', 'mouseenter', 'mousedown', 'mouseup', 'click'].forEach(evtType => {
                        const evt = new MouseEvent(evtType, { bubbles: true, cancelable: true, view: window });
                        button.dispatchEvent(evt);
                    });
                    if (button.click) button.click();
                } catch(e) {}
            }, 350);

            setTimeout(() => {
                if (pulse.parentNode) pulse.parentNode.removeChild(pulse);
            }, 2500);

            return JSON.stringify({ success: true, buttonText: (button.textContent || '').trim() });
        } catch(err) {
            return JSON.stringify({ success: false, error: err.toString() });
        }
    })()
    """;

    /// <summary>
    /// Checks if a cart modal, badge, or notification appeared after clicking Buy.
    /// </summary>
    public const string CheckCartUpdatedScript = """
    (() => {
        try {
            const modal = document.querySelector('[class*="cart-modal"], [class*="basket-modal"], [class*="cart-popup"], .modal-dialog, [role="dialog"]');
            const cartBadge = document.querySelector('[class*="cart-badge"], [class*="basket-count"], [class*="counter"]');
            const textFound = document.body.textContent.includes('Товар добавлен в корзину') || document.body.textContent.includes('Товар додано до кошика') || document.body.textContent.includes('В корзине') || document.body.textContent.includes('У кошику');
            return JSON.stringify({ inCart: !!(modal || textFound || (cartBadge && parseInt(cartBadge.textContent || '0', 10) > 0)) });
        } catch(e) {
            return JSON.stringify({ inCart: true });
        }
    })()
    """;
}
