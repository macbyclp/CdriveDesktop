'use strict';
const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('cdrivePrompt', {
  submit: (value) => ipcRenderer.send('prompt:result', value),
});
