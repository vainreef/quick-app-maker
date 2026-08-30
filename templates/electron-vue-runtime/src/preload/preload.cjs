const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('qam', Object.freeze({
  loadState: () => ipcRenderer.invoke('state:load'),
  saveState: value => ipcRenderer.invoke('state:save', value),
  appInfo: () => ipcRenderer.invoke('app:info')
}));
