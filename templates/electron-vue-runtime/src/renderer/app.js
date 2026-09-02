const { createApp, reactive, ref } = Vue;
const app = createApp({
  setup() {
    const info = reactive({ name: '__APP_NAME__', version: 'loading' });
    const state = reactive({ version: 1, items: [] });
    const draft = ref('');
    const status = ref('就绪');
    const loading = ref(true);
    const saving = ref(false);
    const errorMessage = ref('');
    function cloneItems() { return state.items.map(item => ({ ...item })); }
    async function reload() {
      if (saving.value) { status.value = '正在保存，请稍候'; return; }
      loading.value = true; errorMessage.value = '';
      try {
        const loaded = await window.qam.loadState();
        if (!loaded || !Array.isArray(loaded.items)) throw new Error('state.items must be an array');
        state.version = 1; state.items = loaded.items;
        status.value = '已读取';
      } catch (error) {
        state.items = []; errorMessage.value = `读取失败：${error?.message ?? '未知错误'}`; status.value = '读取失败';
      } finally { loading.value = false; }
    }
    async function save() {
      if (loading.value) { status.value = '正在读取，请稍候'; return; }
      if (saving.value) { status.value = '正在保存，请稍候'; return; }
      const text = draft.value.trim(); if (!text) { status.value = '请输入内容'; return; }
      const previous = cloneItems();
      state.items.unshift({ id: globalThis.crypto?.randomUUID?.() ?? `item-${Date.now()}`, text, createdAt: new Date().toISOString() });
      saving.value = true;
      try {
        await window.qam.saveState({ version: 1, items: cloneItems() });
        draft.value = ''; errorMessage.value = ''; status.value = '已保存';
      } catch (error) {
        state.items = previous; errorMessage.value = `保存失败：${error?.message ?? '未知错误'}`; status.value = '保存失败';
      } finally {
        saving.value = false;
      }
    }
    window.qam.appInfo().then(value => Object.assign(info, value)).catch(error => { errorMessage.value = `应用信息读取失败：${error?.message ?? '未知错误'}`; });
    reload();
    return { info, state, draft, status, loading, saving, errorMessage, save, reload };
  }
});

app.config.errorHandler = (error, _instance, info) => {
  console.error('Vue runtime error:', error, info);
  const root = document.querySelector('#app');
  if (root) root.removeAttribute('v-cloak');
  let errBox = document.querySelector('#qam-render-error');
  if (!errBox) {
    errBox = document.createElement('div');
    errBox.id = 'qam-render-error';
    errBox.style.cssText = 'position:fixed;inset:0;padding:24px;background:#1a1412;color:#ff6b6b;font-family:monospace;z-index:99999;overflow:auto;line-height:1.6;box-sizing:border-box;';
    document.body.prepend(errBox);
  }
  errBox.innerHTML = `<h3 style="margin:0 0 12px;color:#ff8787;">界面渲染异常（已解除黑屏隐藏）</h3><p style="color:#ddd;margin:0 0 8px;"><strong>错误原因：</strong>${error?.message ?? String(error)}</p><pre style="background:#2b1d1a;padding:12px;border-radius:6px;overflow:auto;color:#fcc;font-size:13px;white-space:pre-wrap;">${error?.stack || String(error)}</pre>`;
};

window.addEventListener('error', () => {
  const root = document.querySelector('#app');
  if (root && root.hasAttribute('v-cloak')) root.removeAttribute('v-cloak');
});

app.mount('#app');
