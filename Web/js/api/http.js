export class ApiError extends Error {
  constructor(message, status) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

export function notifyUnauthorized() {
  window.dispatchEvent(new CustomEvent('lanshare:unauthorized'));
}

export async function request(path, options = {}) {
  const response = await fetch(path, {
    ...options,
    headers: { 'X-LanShare-Request': '1', ...options.headers },
  });
  if (!response.ok) {
    let message = `请求失败（${response.status}）`;
    try { message = (await response.json()).error || message; } catch { /* Use the HTTP fallback. */ }
    if (response.status === 401 && path !== '/api/login') notifyUnauthorized();
    throw new ApiError(message, response.status);
  }
  if (response.status === 204) return null;
  const body = await response.text();
  return body ? JSON.parse(body) : null;
}
