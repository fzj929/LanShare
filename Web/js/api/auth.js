import { request } from './http.js';

export function login(password) {
  return request('/api/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ password }),
  });
}

export function logout() {
  return request('/api/logout', { method: 'POST' });
}
