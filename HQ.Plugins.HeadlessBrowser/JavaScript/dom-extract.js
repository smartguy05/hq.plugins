(maxTextLength) => {
    const SKIP_TAGS = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'SVG', 'LINK', 'META']);
    const INTERACTIVE_TAGS = new Set(['A', 'BUTTON', 'INPUT', 'TEXTAREA', 'SELECT', 'DETAILS', 'SUMMARY']);
    const SEMANTIC_TAGS = new Set(['MAIN', 'ARTICLE', 'SECTION', 'NAV', 'HEADER', 'FOOTER', 'ASIDE', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'TABLE', 'THEAD', 'TBODY', 'TR', 'TH', 'TD', 'UL', 'OL', 'LI', 'FORM', 'FIELDSET', 'LEGEND', 'LABEL', 'FIGURE', 'FIGCAPTION', 'BLOCKQUOTE', 'PRE', 'CODE', 'P']);

    function isHidden(el) {
        if (el.getAttribute('aria-hidden') === 'true') return true;
        const style = window.getComputedStyle(el);
        return style.display === 'none' || style.visibility === 'hidden' || (style.opacity === '0' && !INTERACTIVE_TAGS.has(el.tagName));
    }

    function isTrackingPixel(el) {
        if (el.tagName !== 'IMG') return false;
        const w = el.naturalWidth || parseInt(el.getAttribute('width') || '0');
        const h = el.naturalHeight || parseInt(el.getAttribute('height') || '0');
        return w <= 1 && h <= 1;
    }

    function extractNode(el, depth) {
        if (depth > 30) return null;
        if (el.nodeType === Node.TEXT_NODE) {
            const text = el.textContent.trim();
            if (!text) return null;
            return { type: 'text', content: text.slice(0, maxTextLength) };
        }
        if (el.nodeType !== Node.ELEMENT_NODE) return null;

        const tag = el.tagName;
        if (SKIP_TAGS.has(tag)) return null;
        if (isHidden(el)) return null;
        if (isTrackingPixel(el)) return null;

        const node = { tag: tag.toLowerCase() };

        // Preserve meaningful attributes only
        if (el.id) node.id = el.id;
        if (el.getAttribute('role')) node.role = el.getAttribute('role');
        if (el.getAttribute('aria-label')) node.ariaLabel = el.getAttribute('aria-label');
        if (el.getAttribute('name')) node.name = el.getAttribute('name');
        if (tag === 'A' && el.href) node.href = el.href;
        if (tag === 'IMG' && el.alt) node.alt = el.alt;
        if (tag === 'IMG' && el.src) node.src = el.src.slice(0, 200);
        if (tag === 'INPUT') {
            node.inputType = el.type;
            if (el.placeholder) node.placeholder = el.placeholder;
            if (el.value) node.value = el.value.slice(0, 50);
        }

        const children = [];
        for (const child of el.childNodes) {
            const extracted = extractNode(child, depth + 1);
            if (extracted) children.push(extracted);
        }

        if (children.length > 0) node.children = children;
        return node;
    }

    return extractNode(document.body, 0);
}
