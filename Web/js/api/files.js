import { ApiError, notifyUnauthorized, request } from './http.js';

export function listFiles() {
  return request('/api/files');
}

export function deleteFile(id) {
  return request(`/api/files/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

export function downloadUrl(id) {
  return `/api/files/${encodeURIComponent(id)}/download`;
}

export function uploadFile(file, onProgress) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('PUT', `/api/files?name=${encodeURIComponent(file.name)}`);
    xhr.setRequestHeader('X-LanShare-Request', '1');
    xhr.setRequestHeader('Content-Type', 'application/octet-stream');
    xhr.upload.onprogress = event => {
      if (event.lengthComputable) onProgress?.(Math.round(event.loaded / event.total * 100));
    };
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(JSON.parse(xhr.responseText));
        return;
      }
      let message = `上传失败（${xhr.status}）`;
      try { message = JSON.parse(xhr.responseText).error || message; } catch { /* Use the HTTP fallback. */ }
      if (xhr.status === 401) notifyUnauthorized();
      reject(new ApiError(message, xhr.status));
    };
    xhr.onerror = () => reject(new Error('连接中断，请检查网络后重试'));
    xhr.onabort = () => reject(new Error('上传已取消'));
    xhr.send(file);
  });
}
