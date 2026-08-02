window.malievCulture = {
    get: function () {
        try {
            const stored = window.localStorage.getItem('maliev_culture');
            if (stored) return stored;
        } catch (_) { }

        const cookie = document.cookie.split('; ').find(value => value.startsWith('maliev_culture='));
        return cookie ? decodeURIComponent(cookie.substring('maliev_culture='.length)) : null;
    },
    set: function (culture) {
        const normalized = culture === 'th-TH' ? 'th-TH' : 'en-TH';
        try { window.localStorage.setItem('maliev_culture', normalized); } catch (_) { }
        document.cookie = 'maliev_culture=' + encodeURIComponent(normalized) + '; path=/; max-age=31536000; SameSite=Lax';
        document.documentElement.lang = normalized.startsWith('th') ? 'th' : 'en';
    }
};

const initialWorkspaceCulture = window.malievCulture.get();
const workspaceUsesThai = initialWorkspaceCulture && initialWorkspaceCulture.startsWith('th');
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
