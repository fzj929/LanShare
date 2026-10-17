import { request } from './http.js';

export function getStatus() {
  return request('/api/status');
}
