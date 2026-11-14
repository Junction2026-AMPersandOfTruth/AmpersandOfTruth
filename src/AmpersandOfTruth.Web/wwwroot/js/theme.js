const STORAGE_KEY = "aot.theme";
const mq = typeof window !== "undefined" && window.matchMedia
    ? window.matchMedia("(prefers-color-scheme: dark)")
    : null;

function applySystem() {
    const d = document.documentElement;
    d.removeAttribute("data-theme");
    const dark = !!(mq && mq.matches);
    d.setAttribute("data-bs-theme", dark ? "dark" : "light");
}

if (mq && typeof mq.addEventListener === "function") {
    mq.addEventListener("change", () => {
        if (!document.documentElement.dataset.theme) {
            applySystem();
        }
    });
}

export function get() {
    try {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (stored === "light" || stored === "dark") {
            return stored;
        }
    } catch {
        // Ignore storage access errors (private mode, blocked cookies).
    }
    return "system";
}

export function set(mode) {
    const d = document.documentElement;
    if (mode === "light" || mode === "dark") {
        try {
            localStorage.setItem(STORAGE_KEY, mode);
        } catch {
            // Ignore storage write errors; still apply theme to the document.
        }
        d.dataset.theme = mode;
        d.setAttribute("data-bs-theme", mode);
    } else {
        try {
            localStorage.removeItem(STORAGE_KEY);
        } catch {
            // Ignore storage removal errors.
        }
        applySystem();
    }
}
