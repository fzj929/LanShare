'use strict';
const $ = id => document.getElementById(id);
let status = {}, files = [], uploads = [], uploading = false, toastTimer;
function toast(message) { $('toast').textContent = message; $('toast').hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => $('toast').hidden = true, 4000); }
function size(bytes) { if (bytes === 0) return '0 B'; const units = ['B','KB','MB','GB','TB']; const i = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), 4); return `${(bytes / 1024 ** i).toFixed(i ? 1 : 0)} ${units[i]}`; }
async function api(path, options = {}) {
  const response = await fetch(path, { ...options, headers: { 'X-LanShare-Request': '1', ...options.headers } });
  if (!response.ok) { let message = `请求失败（${response.status}）`; try { message = (await response.json()).error || message; } catch {} if (response.status === 401 && path !== '/api/login') { await refreshStatus(); } throw new Error(message); }
  return response.status === 204 ? null : response.json();
}
async function refreshStatus() {
  status = await api('/api/status');
  $('connection').textContent = '服务已连接';
  $('workspace').hidden = !status.authenticated;
  $('locked').hidden = status.authenticated;
  $('account').hidden = status.admin;
  $('logout').hidden = !status.admin && (!status.requiresPassword || !status.authenticated);
  $('role').textContent = status.admin ? '管理员 · 可删除文件' : status.requiresPassword && status.authenticated ? '已登录' : '普通访客';
  $('upload-limit').textContent = `单文件最大 ${size(status.maxFileSize)} · 同名文件独立保存，不会覆盖`;
  const previous = $('addresses').value;
  $('addresses').replaceChildren();
  const addresses = status.addresses.length ? status.addresses : [location.origin];
  for (const address of addresses) { const option = document.createElement('option'); option.value = address; option.textContent = address; $('addresses').append(option); }
  if (addresses.includes(previous)) $('addresses').value = previous;
}
async function loadFiles() {
  if (!status.authenticated) return;
  $('refresh').disabled = true; $('list-error').hidden = true;
  try { files = await api('/api/files'); renderFiles(); }
  catch (error) { $('list-error').textContent = error.message; $('list-error').hidden = false; }
  finally { $('refresh').disabled = false; }
}
function renderFiles() {
  const search = $('search').value.toLocaleLowerCase();
  const filtered = files.filter(f => f.name.toLocaleLowerCase().includes(search));
  filtered.sort((a,b) => $('sort').value === 'name' ? a.name.localeCompare(b.name) : $('sort').value === 'size' ? b.size-a.size : Date.parse(b.uploadedAt)-Date.parse(a.uploadedAt));
  $('file-count').textContent = files.length;
  $('summary').textContent = `共 ${files.length} 个文件 · ${size(files.reduce((sum,f)=>sum+f.size,0))}${search ? ` · 找到 ${filtered.length} 个结果` : ''}`;
  $('files').replaceChildren(); $('empty').hidden = filtered.length !== 0;
  $('empty').querySelector('h3').textContent = search ? '没有找到匹配的文件' : '还没有共享文件';
  $('empty').querySelector('p').textContent = search ? '试试其他关键词。' : '上传第一个文件，让分享从这里开始。';
  for (const file of filtered) {
    const row = document.createElement('tr');
    const nameCell = document.createElement('td'), nameWrap = document.createElement('div'); nameWrap.className='filename';
    const icon = document.createElement('span'); icon.className = 'file-icon'; icon.textContent = (file.name.includes('.') ? file.name.split('.').pop().slice(0,4) : 'FILE');
    const name = document.createElement('span'); name.className = 'file-name'; name.textContent = file.name;
    nameWrap.append(icon,name); nameCell.append(nameWrap);
    const bytes = document.createElement('td'); bytes.textContent = size(file.size);
    const date = document.createElement('td'); date.textContent = new Date(file.uploadedAt).toLocaleString('zh-CN', {year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',hour12:false});
    const actions = document.createElement('td'), group = document.createElement('div'); group.className='file-actions';
    const download = document.createElement('a'); download.href=`/api/files/${file.id}/download`; download.textContent='下载 ↓'; download.setAttribute('download',file.name); group.append(download);
    if (status.admin) { const remove = document.createElement('button'); remove.textContent='删除'; remove.onclick=async()=>{
      if (!confirm(`确定删除“${file.name}”？此操作无法撤销。`)) return;
      remove.disabled=true; try { await api(`/api/files/${file.id}`,{method:'DELETE'}); toast('文件已删除'); await loadFiles(); } catch(e){toast(e.message);remove.disabled=false;}
    }; group.append(remove); }
    actions.append(group); row.append(nameCell,bytes,date,actions); $('files').append(row);
  }
}
function enqueue(selected) {
  if (!status.authenticated) return;
  for (const file of selected) uploads.push({file,progress:0,state:file.size>status.maxFileSize ? '超过文件大小限制' : '等待上传',done:false,failed:file.size>status.maxFileSize});
  renderUploads(); runQueue();
}
function renderUploads() {
  $('uploads').hidden = uploads.length === 0; $('upload-list').replaceChildren();
  for (const item of uploads) {
    const row = document.createElement('div'); row.className='upload-item';
    const name=document.createElement('span');name.className='upload-name';name.textContent=`${item.file.name} · ${size(item.file.size)}`;
    const state=document.createElement('span');state.className='upload-state';const text=document.createElement('span');text.textContent=item.state;state.append(text);
    if (item.failed) { const retry=document.createElement('button');retry.textContent='重试';retry.onclick=()=>{if(item.file.size>status.maxFileSize){toast('文件超过大小限制，请修改服务端配置后重启');return;}item.failed=false;item.state='等待上传';runQueue();};state.append(retry); }
    const bar=document.createElement('progress');bar.max=100;bar.value=item.progress;bar.setAttribute('aria-label',`${item.file.name} 上传进度`);
    row.append(name,state,bar); $('upload-list').append(row); item.progressElement=bar;item.stateElement=text;
  }
}
async function runQueue() {
  if (uploading) return; uploading=true;
  try {
    let item;
    while ((item=uploads.find(u=>!u.done&&!u.failed))) {
      item.state='上传中…';renderUploads();
      try { await sendFile(item);item.done=true;item.progress=100;item.state='上传完成'; }
      catch(e) {item.failed=true;item.state=e.message;}
      renderUploads();await loadFiles();
    }
  } finally {uploading=false;}
}
function sendFile(item) {
  return new Promise((resolve,reject)=>{
    const xhr=new XMLHttpRequest();xhr.open('PUT',`/api/files?name=${encodeURIComponent(item.file.name)}`);xhr.setRequestHeader('X-LanShare-Request','1');xhr.setRequestHeader('Content-Type','application/octet-stream');
    xhr.upload.onprogress=e=>{if(e.lengthComputable){item.progress=Math.round(e.loaded/e.total*100);item.progressElement.value=item.progress;item.stateElement.textContent=item.progress===100?'正在保存…':`${item.progress}%`;}};
    xhr.onload=()=>{if(xhr.status>=200&&xhr.status<300)resolve();else {let message=`上传失败（${xhr.status}）`;try{message=JSON.parse(xhr.responseText).error||message;}catch{}reject(new Error(message));if(xhr.status===401)refreshStatus().catch(()=>{});}};
    xhr.onerror=()=>reject(new Error('连接中断，请检查网络后重试'));xhr.onabort=()=>reject(new Error('上传已取消'));xhr.send(item.file);
  });
}
function showLogin(admin) { $('login-title').textContent=admin?'管理员登录':'登录文件站';$('login-description').textContent=admin?'管理员可删除共享文件。密码显示在服务电脑的启动窗口中。':'请输入此文件站的访问密码。';$('login-error').textContent='';$('password').value='';$('login-dialog').showModal();$('password').focus(); }
$('account').onclick=()=>showLogin(true);$('unlock').onclick=()=>showLogin(false);$('close-dialog').onclick=()=>$('login-dialog').close();
$('login-form').onsubmit=async e=>{e.preventDefault();$('login-submit').disabled=true;try{await api('/api/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({password:$('password').value})});$('login-dialog').close();await refreshStatus();await loadFiles();toast('登录成功');}catch(error){$('login-error').textContent=error.message;}finally{$('login-submit').disabled=false;}};
$('logout').onclick=async()=>{try{await api('/api/logout',{method:'POST'});await refreshStatus();await loadFiles();toast('已退出登录');}catch(e){toast(e.message);}};
$('dropzone').onclick=()=>$('file-input').click();$('dropzone').onkeydown=e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();$('file-input').click();}};
$('file-input').onchange=e=>{enqueue(e.target.files);e.target.value='';};
for(const event of ['dragenter','dragover'])$('dropzone').addEventListener(event,e=>{e.preventDefault();$('dropzone').classList.add('dragover');});
for(const event of ['dragleave','drop'])$('dropzone').addEventListener(event,e=>{e.preventDefault();$('dropzone').classList.remove('dragover');});
$('dropzone').addEventListener('drop',e=>enqueue(e.dataTransfer.files));
window.addEventListener('dragover',e=>e.preventDefault());window.addEventListener('drop',e=>e.preventDefault());
window.addEventListener('beforeunload',e=>{if(uploading){e.preventDefault();e.returnValue='';}});
$('search').oninput=renderFiles;$('sort').onchange=renderFiles;$('refresh').onclick=loadFiles;
$('clear-uploads').onclick=()=>{uploads=uploads.filter(u=>!u.done);renderUploads();};
$('copy').onclick=async()=>{const address=$('addresses').value;try{await navigator.clipboard.writeText(address);toast('地址已复制');}catch{const input=document.createElement('textarea');input.value=address;document.body.append(input);input.select();const copied=document.execCommand('copy');input.remove();if(copied)toast('地址已复制');else prompt('请复制以下地址',address);}};
async function init(){try{await refreshStatus();await loadFiles();}catch(e){$('connection').textContent='连接失败';$('list-error').textContent='无法连接服务，请确认程序正在运行后刷新页面。';$('list-error').hidden=false;}}
init();
