import { getStatus } from './js/api/status.js';
import { login, logout } from './js/api/auth.js';
import { deleteFile, downloadUrl, listFiles, uploadFile } from './js/api/files.js';

const $ = id => document.getElementById(id);
let status = {};
let files = [];
let uploads = [];
let uploading = false;
let toastTimer;

function toast(message) {
  $('toast').textContent = message;
  $('toast').hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { $('toast').hidden = true; }, 4000);
}

function size(bytes) {
  if (bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const index = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), 4);
  return `${(bytes / 1024 ** index).toFixed(index ? 1 : 0)} ${units[index]}`;
}

async function refreshStatus() {
  status = await getStatus();
  $('connection').textContent = '服务已连接';
  $('connection').classList.remove('offline');
  $('workspace').hidden = !status.authenticated;
  $('locked').hidden = status.authenticated;
  $('account').hidden = status.admin;
  $('logout').hidden = !status.admin && (!status.requiresPassword || !status.authenticated);
  $('role').textContent = status.admin ? '管理员' : status.requiresPassword && status.authenticated ? '已登录' : '普通访客';
  $('upload-limit').textContent = `单文件最大 ${size(status.maxFileSize)}`;
  const previous = $('addresses').value;
  const addresses = status.addresses.length ? status.addresses : [location.origin];
  $('addresses').replaceChildren(...addresses.map(address => {
    const option = document.createElement('option');
    option.value = address;
    option.textContent = address;
    return option;
  }));
  if (addresses.includes(previous)) $('addresses').value = previous;
  if (!status.authenticated) {
    files = [];
    renderFiles();
  }
}

async function loadFiles() {
  if (!status.authenticated) return;
  $('refresh').disabled = true;
  $('list-error').hidden = true;
  try {
    files = await listFiles();
    renderFiles();
  } catch (error) {
    $('list-error').textContent = error.message;
    $('list-error').hidden = false;
  } finally {
    $('refresh').disabled = false;
  }
}

function fileType(extension) {
  if (['doc', 'docx', 'pdf', 'txt', 'md'].includes(extension)) return 'document';
  if (['xls', 'xlsx', 'csv'].includes(extension)) return 'sheet';
  if (['png', 'jpg', 'jpeg', 'webp', 'gif', 'svg'].includes(extension)) return 'image';
  if (['zip', 'gz', 'tar', '7z', 'rar'].includes(extension)) return 'archive';
  return 'other';
}

function renderFiles() {
  const query = $('search').value.toLocaleLowerCase();
  const filtered = files.filter(file => file.name.toLocaleLowerCase().includes(query));
  filtered.sort((a, b) => {
    if ($('sort').value === 'name') return a.name.localeCompare(b.name);
    if ($('sort').value === 'size') return b.size - a.size;
    return Date.parse(b.uploadedAt) - Date.parse(a.uploadedAt);
  });
  $('file-count').textContent = files.length;
  $('summary').textContent = `${files.length} 个文件 · 共 ${size(files.reduce((sum, file) => sum + file.size, 0))}${query ? ` · 找到 ${filtered.length} 个结果` : ''}`;
  $('files').replaceChildren();
  $('empty').hidden = filtered.length !== 0;
  $('empty').querySelector('h3').textContent = query ? '没有找到匹配的文件' : '还没有共享文件';
  $('empty').querySelector('p').textContent = query ? '换个关键词试试看。' : '上传第一个文件，即可在其他设备下载。';

  for (const file of filtered) {
    const row = document.createElement('tr');
    const nameCell = document.createElement('td');
    const filename = document.createElement('div');
    filename.className = 'filename';
    const extension = file.name.includes('.') ? file.name.split('.').pop().toLowerCase() : '';
    const icon = document.createElement('span');
    icon.className = 'file-icon';
    icon.dataset.type = fileType(extension);
    icon.textContent = extension.slice(0, 4) || 'FILE';
    icon.setAttribute('aria-hidden', 'true');
    const name = document.createElement('span');
    name.className = 'file-name';
    name.textContent = file.name;
    filename.append(icon, name);
    nameCell.append(filename);
    const bytes = document.createElement('td');
    bytes.textContent = size(file.size);
    const date = document.createElement('td');
    date.className = 'date-column';
    date.textContent = new Date(file.uploadedAt).toLocaleString('zh-CN', {
      year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hour12: false,
    });
    const actions = document.createElement('td');
    const group = document.createElement('div');
    group.className = 'file-actions';
    const download = document.createElement('a');
    download.href = downloadUrl(file.id);
    download.textContent = '下载';
    download.setAttribute('download', file.name);
    download.setAttribute('aria-label', `下载 ${file.name}`);
    group.append(download);
    if (status.admin) {
      const remove = document.createElement('button');
      remove.textContent = '删除';
      remove.setAttribute('aria-label', `删除 ${file.name}`);
      remove.onclick = async () => {
        if (!confirm(`确定删除“${file.name}”？此操作无法撤销。`)) return;
        remove.disabled = true;
        try {
          await deleteFile(file.id);
          toast('文件已删除');
          await loadFiles();
        } catch (error) {
          toast(error.message);
          remove.disabled = false;
        }
      };
      group.append(remove);
    }
    actions.append(group);
    row.append(nameCell, bytes, date, actions);
    $('files').append(row);
  }
}

function enqueue(selected) {
  if (!status.authenticated) return;
  for (const file of selected) {
    const tooLarge = file.size > status.maxFileSize;
    uploads.push({ file, progress: 0, state: tooLarge ? '超过文件大小限制' : '等待上传', done: false, failed: tooLarge });
  }
  renderUploads();
  runQueue();
}

function renderUploads() {
  $('uploads').hidden = uploads.length === 0;
  $('queue-count').textContent = uploads.length;
  $('upload-list').replaceChildren();
  for (const item of uploads) {
    const row = document.createElement('div');
    row.className = `upload-item${item.failed ? ' is-error' : item.done ? ' is-done' : ''}`;
    const name = document.createElement('span');
    name.className = 'upload-name';
    name.textContent = `${item.file.name} · ${size(item.file.size)}`;
    const state = document.createElement('span');
    state.className = 'upload-state';
    const text = document.createElement('span');
    text.textContent = item.state;
    state.append(text);
    if (item.failed) {
      const retry = document.createElement('button');
      retry.textContent = '重试';
      retry.onclick = () => {
        if (!status.authenticated) { showLogin(false); return; }
        if (item.file.size > status.maxFileSize) { toast('文件超过大小限制，请修改服务端配置后重启'); return; }
        item.failed = false;
        item.progress = 0;
        item.state = '等待上传';
        renderUploads();
        runQueue();
      };
      state.append(retry);
    }
    const progress = document.createElement('progress');
    progress.max = 100;
    progress.value = item.progress;
    progress.setAttribute('aria-label', `${item.file.name} 上传进度`);
    row.append(name, state, progress);
    $('upload-list').append(row);
    item.progressElement = progress;
    item.stateElement = text;
  }
}

async function runQueue() {
  if (uploading) return;
  uploading = true;
  try {
    let item;
    while ((item = uploads.find(upload => !upload.done && !upload.failed))) {
      item.state = '上传中…';
      renderUploads();
      try {
        await uploadFile(item.file, progress => {
          item.progress = progress;
          item.progressElement.value = progress;
          item.stateElement.textContent = progress === 100 ? '正在保存…' : `${progress}%`;
        });
        item.done = true;
        item.progress = 100;
        item.state = '上传完成';
      } catch (error) {
        item.failed = true;
        item.state = error.message;
        if (error.status === 401) {
          for (const pending of uploads.filter(upload => !upload.done && !upload.failed)) {
            pending.failed = true;
            pending.state = '请登录后重试';
          }
        }
      }
      renderUploads();
      await loadFiles();
    }
  } finally { uploading = false; }
}

function showLogin(admin) {
  $('login-title').textContent = admin ? '管理员登录' : '登录文件站';
  $('login-description').textContent = admin
    ? '登录后可管理和删除共享文件。密码显示在服务电脑的启动窗口中。'
    : '请输入访问密码，即可上传和下载文件。';
  $('login-error').textContent = '';
  $('password').value = '';
  $('login-dialog').showModal();
  $('password').focus();
}

$('account').onclick = () => showLogin(true);
$('unlock').onclick = () => showLogin(false);
$('close-dialog').onclick = () => $('login-dialog').close();
$('login-form').onsubmit = async event => {
  event.preventDefault();
  $('login-submit').disabled = true;
  try {
    await login($('password').value);
    $('login-dialog').close();
    await refreshStatus();
    await loadFiles();
    toast('登录成功');
  } catch (error) { $('login-error').textContent = error.message; }
  finally { $('login-submit').disabled = false; }
};
$('logout').onclick = async () => {
  try {
    await logout();
    await refreshStatus();
    await loadFiles();
    toast('已退出登录');
  } catch (error) { toast(error.message); }
};
$('dropzone').onclick = () => $('file-input').click();
$('dropzone').onkeydown = event => {
  if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); $('file-input').click(); }
};
$('file-input').onchange = event => { enqueue(event.target.files); event.target.value = ''; };
for (const event of ['dragenter', 'dragover']) {
  $('dropzone').addEventListener(event, e => { e.preventDefault(); $('dropzone').classList.add('dragover'); });
}
for (const event of ['dragleave', 'drop']) {
  $('dropzone').addEventListener(event, e => { e.preventDefault(); $('dropzone').classList.remove('dragover'); });
}
$('dropzone').addEventListener('drop', event => enqueue(event.dataTransfer.files));
window.addEventListener('dragover', event => event.preventDefault());
window.addEventListener('drop', event => event.preventDefault());
window.addEventListener('beforeunload', event => { if (uploading) { event.preventDefault(); event.returnValue = ''; } });
window.addEventListener('lanshare:unauthorized', () => refreshStatus().catch(() => {}));
$('search').oninput = renderFiles;
$('sort').onchange = renderFiles;
$('refresh').onclick = loadFiles;
$('clear-uploads').onclick = () => { uploads = uploads.filter(upload => !upload.done); renderUploads(); };
$('copy').onclick = async () => {
  const address = $('addresses').value;
  try { await navigator.clipboard.writeText(address); toast('链接已复制'); }
  catch {
    const input = document.createElement('textarea');
    input.value = address;
    document.body.append(input);
    input.select();
    const copied = document.execCommand('copy');
    input.remove();
    if (copied) toast('链接已复制');
    else prompt('请复制以下地址', address);
  }
};

async function init() {
  try { await refreshStatus(); await loadFiles(); }
  catch {
    $('connection').textContent = '连接失败';
    $('connection').classList.add('offline');
    $('workspace').hidden = false;
    $('dropzone').hidden = true;
    $('list-error').textContent = '无法连接服务，请确认程序正在运行后刷新页面。';
    $('list-error').hidden = false;
  }
}
init();
