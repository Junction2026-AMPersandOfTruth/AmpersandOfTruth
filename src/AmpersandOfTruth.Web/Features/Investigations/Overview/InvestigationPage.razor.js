const mq = typeof window !== "undefined" && window.matchMedia
    ? window.matchMedia("(min-width: 992px)")
    : null;

const tracked = new Set();
let globalListenersBound = false;

function ensureGlobalListeners() {
    if (globalListenersBound) return;
    globalListenersBound = true;

    if (mq) {
        mq.addEventListener("change", (e) => {
            for (const el of tracked) {
                if (el && el.isConnected) {
                    el.open = e.matches;
                } else {
                    tracked.delete(el);
                }
            }
        });
    }

    if (typeof document !== "undefined") {
        document.addEventListener("click", (e) => {
            for (const menu of document.querySelectorAll("details.aot-signal-menu[open]")) {
                if (!menu.contains(e.target)) {
                    menu.open = false;
                }
            }
        });

        document.addEventListener("keydown", (e) => {
            if (e.key === "Escape") {
                for (const menu of document.querySelectorAll("details.aot-signal-menu[open]")) {
                    menu.open = false;
                }
            }
        });
    }
}

export function syncDisclosure(el) {
    ensureGlobalListeners();
    if (!el) return false;
    tracked.add(el);
    const desktop = mq ? mq.matches : (typeof window !== "undefined" && window.innerWidth >= 992);
    el.open = desktop;
    return desktop;
}
