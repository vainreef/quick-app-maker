const { createApp, reactive, ref } = Vue;
createApp({
  setup() {
    const info = reactive({ name: '__APP_NAME__', version: 'loading' });
    const state = reactive({ version: 1, items: [] });
    const draft = ref('');
    const status = ref('就绪');
    async function reload() { Object.assign(state, await window.qam.loadState()); status.value = '已读取'; }
    async function save() { const text = draft.value.trim(); if (!text) { status.value = '请输入内容'; return; } state.items.unshift({ id: crypto.randomUUID(), text, createdAt: new Date().toLocaleString('zh-CN') }); await window.qam.saveState(state); draft.value = ''; status.value = '已保存'; }
    window.qam.appInfo().then(value => Object.assign(info, value));
    reload();
    return { info, state, draft, status, save, reload };
  }
}).mount('#app');
