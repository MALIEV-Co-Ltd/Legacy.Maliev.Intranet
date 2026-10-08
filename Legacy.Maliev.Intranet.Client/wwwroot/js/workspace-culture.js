// Keep bootstrap normalization aligned with WorkspaceCulture.Normalize.
// Reading a preference does not rewrite storage or change provider precedence.
function normalizeWorkspaceCulture(value) {
    // String.trim differs from .NET String.Trim for U+0085 and U+FEFF.
    const candidate = typeof value === 'string'
        ? value.replace(/^[\u0009-\u000d\u0020\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+|[\u0009-\u000d\u0020\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+$/g, '').toLowerCase()
        : '';
    if (candidate === 'th-th') return 'th-TH';
    if (candidate === 'en-us') return 'en-US';
    return 'en-TH';
}

window.malievCulture = {
    get: function () {
        try {
            const stored = window.localStorage.getItem('maliev_culture');
            if (stored) return normalizeWorkspaceCulture(stored);
        } catch (_) { }

        const cookie = document.cookie.split('; ').find(value => value.startsWith('maliev_culture='));
        try {
            return normalizeWorkspaceCulture(cookie ? decodeURIComponent(cookie.substring('maliev_culture='.length)) : null);
        } catch (_) {
            // A malformed percent-encoded cookie is an invalid preference, not a bootstrap failure.
            return 'en-TH';
        }
    },
    set: function (culture) {
        const normalized = culture === 'th-TH' ? 'th-TH' : 'en-TH';
        try { window.localStorage.setItem('maliev_culture', normalized); } catch (_) { }
        document.cookie = 'maliev_culture=' + encodeURIComponent(normalized) + '; path=/; max-age=31536000; SameSite=Lax';
        document.documentElement.lang = normalized.startsWith('th') ? 'th' : 'en';
    }
};

const initialWorkspaceCulture = window.malievCulture.get();
const workspaceUsesThai = initialWorkspaceCulture === 'th-TH';
document.documentElement.lang = workspaceUsesThai ? 'th' : 'en';

const loading = document.getElementById('workspace-loading');
const fatalMessage = document.getElementById('workspace-fatal-message');
const reload = document.getElementById('workspace-reload');
const dismiss = document.getElementById('workspace-dismiss');
if (workspaceUsesThai) {
    loading?.setAttribute('aria-label', 'กำลังโหลดระบบอินทราเน็ต MALIEV');
    if (fatalMessage) fatalMessage.textContent = 'เกิดข้อผิดพลาดที่ไม่คาดคิด';
    if (reload) reload.textContent = 'โหลดใหม่';
    dismiss?.setAttribute('aria-label', 'ปิดข้อความข้อผิดพลาด');
}
