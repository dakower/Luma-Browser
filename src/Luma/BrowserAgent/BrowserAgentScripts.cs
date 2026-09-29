namespace Luma.BrowserAgent;

public static class BrowserAgentScripts
{
    /// <summary>
    /// Injected into active store pages to extract product tiles, prices, specs, and direct product links.
    /// Filters out accessories, cleaning products, and non-target items.
    /// </summary>
    public const string ScanECommercePageScript = """
    (() => {
        try {
            const results = [];
            const cardSelectors = [
                '.goods-tile', 'rz-goods-tile', 'app-goods-tile', '.catalog-grid__cell',
                '.product-item', '.product-card', '.product-wrapper', '.catalog-item',
                '.products-layout__item', '[data-product-id]', 'article.product',
                '.item-card', '.c-product-card', '.products-item'
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
                cards = Array.from(document.querySelectorAll('article, div[class*="product"], div[class*="goods"]'))
                    .filter(c => c.textContent.match(/\d{2,3}[\s.,]?\d{3}\s*(?:грн|₴|uah)/i));
            }

            for (const card of cards.slice(0, 20)) {
                // Title
                const titleEl = card.querySelector('a[class*="title"], .goods-tile__title, .product-title, h2, h3, a[href*="/p/"]') || card.querySelector('a');
                const title = titleEl ? (titleEl.textContent || titleEl.getAttribute('title') || '').trim() : '';
                if (!title) continue;

                // Category verification: must be a computer/laptop, never detergent/accessories/cleaning supplies
                const lower = title.toLowerCase();
                const isLaptop = /(?:ноутбук|laptop|notebook|loq|tuf|nitro|legion|victus|macbook|thinkpad|rog|predator|katana|sword|pulse|thin|cyborg|ideapad|vivobook|zenbook|pavilion|omen|alienware)/i.test(lower);
                if (!isLaptop) continue;

                const isAccessory = /(?:сумка|рюкзак|чохол|чехол|підставка|подставка|миша|мышь|клавіатура|клавиатура|порошок|кабель|зарядн|адаптер)/i.test(lower);
                if (isAccessory) continue;

                // Direct link
                let link = titleEl && titleEl.href ? titleEl.href : '';
                if (!link) {
                    const anyA = card.querySelector('a[href]');
                    if (anyA) link = anyA.href;
                }

                // Price extraction
                const priceMatch = card.textContent.match(/(\d{1,3}(?:[\s.,]\d{3})+|\d{4,6})\s*(?:грн|₴|uah|\$|€)?/i);
                let price = 0;
                if (priceMatch) {
                    const cleanNum = priceMatch[1].replace(/[\s.,]/g, '');
                    price = parseInt(cleanNum, 10) || 0;
                }

                // Specs
                const specsEl = card.querySelector('.goods-tile__description, .product-specs, .short-desc, ul');
                const specs = specsEl ? specsEl.textContent.trim() : '';

                if (price > 0) {
                    results.push({
                        title: title.slice(0, 140),
                        price: price,
                        currency: '₴',
                        url: link || window.location.href,
                        specs: specs.slice(0, 200)
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
    /// Generates targeted JavaScript to find and click the buy button for the specified product model.
    /// Handles both product detail pages and catalog grids, never clicking unrelated or promoted items.
    /// </summary>
    public static string BuildClickBuyButtonScript(string? targetModelName)
    {
        var modelSafe = (targetModelName ?? "").Replace("\"", "\\\"").Replace("\n", " ").Trim();
        return $$"""
        (() => {
            try {
                const targetModel = "{{modelSafe}}".toLowerCase();
                let button = null;

                // Strategy A: We are on a single product page
                const productBtnSelectors = [
                    'app-buy-button button',
                    'button.buy-button',
                    'button.button--green',
                    'button[data-qa="buy-button"]',
                    '.product-about__buy button',
                    '.product__buy button',
                    '.product-buy-btn',
                    '.price-box__buy button',
                    'button.btn-buy'
                ];

                for (const sel of productBtnSelectors) {
                    const b = document.querySelector(sel);
                    if (b && b.offsetParent !== null && !b.closest('.similar, .recommended, .cross-sell, .carousel, .slider')) {
                        button = b;
                        break;
                    }
                }

                // Strategy B: If on a catalog/search grid, find the specific card matching target model
                if (!button && targetModel) {
                    const cards = Array.from(document.querySelectorAll('.goods-tile, .product-card, article, [data-product-id], .catalog-grid__cell'));
                    const tokens = targetModel.split(/[\s,()\/]+/).filter(w => w.length > 2);
                    
                    const matchingCard = cards.find(c => {
                        const txt = (c.textContent || '').toLowerCase();
                        let matches = 0;
                        for (const tok of tokens) {
                            if (txt.includes(tok)) matches++;
                        }
                        return matches >= Math.min(2, tokens.length);
                    });

                    if (matchingCard) {
                        button = matchingCard.querySelector('button[class*="buy"], button[aria-label*="Купити"], button[aria-label*="Купить"], app-buy-button button, [data-qa="buy-button"], a[class*="buy"]');
                    }
                }

                // Strategy C: Text-based lookup excluding promos/recommendations
                if (!button) {
                    const allButtons = Array.from(document.querySelectorAll('button, a[role="button"], a.btn'));
                    button = allButtons.find(b => {
                        if (b.offsetParent === null) return false;
                        if (b.closest('.similar, .recommended, .cross-sell, .carousel, .slider, footer')) return false;
                        const t = (b.textContent || '').trim().toLowerCase();
                        return t === 'купити' || t === 'купить' || t === 'в корзину' || t === 'до кошика' || t === 'в кошик';
                    });
                }

                if (!button) return JSON.stringify({ success: false, reason: 'Buy button not found' });

                // Smooth scroll into view
                button.scrollIntoView({ behavior: 'smooth', block: 'center' });

                // Highlight button
                button.style.outline = '3px solid #7468c7';
                button.style.outlineOffset = '2px';

                // Dispatch synthetic user mouse events
                ['mouseover', 'mouseenter', 'mousedown', 'mouseup', 'click'].forEach(evtType => {
                    const evt = new MouseEvent(evtType, { bubbles: true, cancelable: true, view: window });
                    button.dispatchEvent(evt);
                });
                if (button.click) button.click();

                return JSON.stringify({ success: true, buttonText: (button.textContent || '').trim() });
            } catch(err) {
                return JSON.stringify({ success: false, error: err.toString() });
            }
        })()
        """;
    }

    /// <summary>
    /// Checks if a cart modal, badge, or notification appeared after clicking Buy.
    /// </summary>
    public const string CheckCartUpdatedScript = """
    (() => {
        try {
            const modal = document.querySelector('[class*="cart-modal"], [class*="basket-modal"], [class*="cart-popup"], .modal-dialog, [role="dialog"], rz-cart');
            const cartBadge = document.querySelector('[class*="cart-badge"], [class*="basket-count"], [class*="counter"]');
            const textFound = document.body.textContent.includes('Товар добавлен в корзину') || document.body.textContent.includes('Товар додано до кошика') || document.body.textContent.includes('В корзине') || document.body.textContent.includes('У кошику');
            return JSON.stringify({ inCart: !!(modal || textFound || (cartBadge && parseInt(cartBadge.textContent || '0', 10) > 0)) });
        } catch(e) {
            return JSON.stringify({ inCart: true });
        }
    })()
    """;
}
