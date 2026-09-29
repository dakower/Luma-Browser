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
            const cards = Array.from(document.querySelectorAll(
                'rz-catalog-tile, .catalog-grid__cell, .goods-tile, app-goods-tile, [data-goods-id], .product-item, .product-card, .catalog-item, article.product, .c-product-card'
            ));

            for (const card of cards.slice(0, 30)) {
                // Find title
                const titleEl = card.querySelector('.goods-tile__heading, .goods-tile__title, a[class*="heading"], a[class*="title"], h2, h3, a[href*="/p"]') || card.querySelector('a');
                const title = (titleEl ? (titleEl.textContent || titleEl.getAttribute('title') || '') : '').trim();
                if (!title) continue;

                // Category verification: must be a computer/laptop
                const lower = title.toLowerCase();
                const isLaptop = /(?:ноутбук|laptop|notebook|thin|loq|tuf|nitro|legion|victus|macbook|thinkpad|rog|predator|katana|sword|pulse|cyborg|ideapad|vivobook|zenbook|pavilion|omen|alienware|aspire)/i.test(lower);
                if (!isLaptop) continue;

                // Exclude accessories & non-computers
                const isAccessory = /(?:сумка|рюкзак|чохол|чехол|підставка|подставка|миша|мышь|клавіатура|клавиатура|порошок|кабель|зарядн|адаптер|блок|коврик|гарнітура)/i.test(lower);
                if (isAccessory) continue;

                // Price extraction: get real current price (skip old/strikethrough prices)
                let price = 0;
                const priceEl = card.querySelector('.goods-tile__price-value, [class*="price-value"], .price__value, [class*="current-price"]');
                if (priceEl) {
                    const digits = priceEl.textContent.replace(/\D/g, '');
                    price = parseInt(digits, 10) || 0;
                } else {
                    const priceBlocks = Array.from(card.querySelectorAll('[class*="price"]'));
                    for (const pb of priceBlocks) {
                        if (pb.closest('[class*="old"], [class*="strikethrough"], del, s')) continue;
                        const digits = pb.textContent.replace(/\D/g, '');
                        const val = parseInt(digits, 10) || 0;
                        if (val >= 15000 && val <= 300000) {
                            price = val;
                            break;
                        }
                    }
                }

                // Direct product link
                let link = '';
                if (titleEl && titleEl.href && titleEl.href.includes('/p')) link = titleEl.href;
                if (!link) {
                    const anyA = card.querySelector('a[href*="/p"]');
                    if (anyA) link = anyA.href;
                }
                if (!link && titleEl && titleEl.href) link = titleEl.href;

                // Specs
                const specsEl = card.querySelector('.goods-tile__description, [class*="specs"], [class*="desc"], ul');
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
    /// Generates targeted JavaScript to find and click the buy button for the specified product model,
    /// whether on a product detail page or inside a catalog tile.
    /// </summary>
    public static string BuildClickBuyButtonScript(string? targetModelName)
    {
        var modelSafe = (targetModelName ?? "").Replace("\"", "\\\"").Replace("\n", " ").Trim();
        return $$"""
        (() => {
            try {
                const targetModel = "{{modelSafe}}".toLowerCase();
                let button = null;

                // 1. Single product page buy button
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
                    if (b && b.offsetParent !== null && !b.closest('.similar, .recommended, .cross-sell, .carousel, .slider, footer')) {
                        button = b;
                        break;
                    }
                }

                // 2. In catalog grid: find the specific card matching target model
                if (!button) {
                    const cards = Array.from(document.querySelectorAll('rz-catalog-tile, .goods-tile, .product-card, article, [data-product-id], .catalog-grid__cell'));
                    const tokens = targetModel.split(/[\s,()\/]+/).filter(w => w.length > 2);
                    
                    let matchingCard = null;
                    if (tokens.length > 0) {
                        matchingCard = cards.find(c => {
                            const txt = (c.textContent || '').toLowerCase();
                            let matches = 0;
                            for (const tok of tokens) {
                                if (txt.includes(tok)) matches++;
                            }
                            return matches >= Math.min(2, tokens.length);
                        });
                    }

                    // Fallback to first valid laptop card if exact match isn't found
                    if (!matchingCard) {
                        matchingCard = cards.find(c => {
                            const txt = (c.textContent || '').toLowerCase();
                            return /(ноутбук|laptop|notebook|thin|loq|tuf|nitro)/.test(txt) && !/(порошок|сумка|чохол)/.test(txt);
                        });
                    }

                    if (matchingCard) {
                        button = matchingCard.querySelector('app-buy-button button, button[class*="buy"], button[aria-label*="Купити"], button[aria-label*="Купить"], [data-qa="buy-button"]');
                    }
                }

                if (!button) return JSON.stringify({ success: false, reason: 'Buy button not found' });

                button.scrollIntoView({ behavior: 'smooth', block: 'center' });
                button.style.outline = '3px solid #7468c7';
                button.style.outlineOffset = '2px';

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
    /// Opens the cart / checkout modal if not already open, and purges accidental non-computer items.
    /// </summary>
    public const string OpenCartAndCleanAccidentalItemsScript = """
    (() => {
        try {
            // Step 1: Remove accidental non-laptop items (e.g. washing powder from prior failed attempts)
            const cartItems = Array.from(document.querySelectorAll('.cart-list__item, .cart-product, li[class*="cart"], .popup-cart-item'));
            for (const item of cartItems) {
                const text = (item.textContent || '').toLowerCase();
                if (text.includes('порошок') || text.includes('green line') || text.includes('ополіскувач') || text.includes('sensua')) {
                    const delBtn = item.querySelector('button[aria-label*="Видалити"], button[class*="delete"], [data-qa="delete-button"], button[class*="trash"]');
                    if (delBtn) delBtn.click();
                }
            }

            // Step 2: Ensure the cart popup / modal is opened and visible
            const modal = document.querySelector('.modal-dialog, rz-cart, [class*="cart-modal"], [class*="cart-popup"]');
            if (modal && modal.offsetParent !== null) {
                return JSON.stringify({ cartOpen: true });
            }

            // Click header cart button
            const headerCartBtn = document.querySelector(
                'a[href*="/cart"], button[aria-label*="Кошик"], button[aria-label*="Корзина"], rz-cart-icon, .header-actions__item--cart, [data-qa="cart-button"]'
            );
            if (headerCartBtn) {
                headerCartBtn.click();
                return JSON.stringify({ cartOpen: true, action: 'clicked_header_cart' });
            }

            return JSON.stringify({ cartOpen: false });
        } catch(e) {
            return JSON.stringify({ cartOpen: false, error: e.toString() });
        }
    })()
    """;
}
