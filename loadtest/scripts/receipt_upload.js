import http from 'k6/http';
import { check } from 'k6';
import { loadConfig, authTokenForRole } from './helpers.js';
import { readFileSync } from 'fs';

const cfg = loadConfig();
const BASE = __ENV.BASE_URL || cfg.baseUrl || 'http://localhost:5000';
const filePath = __ENV.RECEIPT_FILE || '../../loadtest/assets/receipt.jpg';

export let options = {
 vus:5,
 duration: '30s',
 thresholds: {
 'http_req_failed': ['rate<0.05'],
 'http_req_duration': ['p(95)<1500']
 }
};

export default function () {
 const recycler = __ENV.RECYCLER_USER || cfg.defaultUsers.recycler.username;
 const recyclerPass = __ENV.RECYCLER_PASS || cfg.defaultUsers.recycler.password;
 const token = authTokenForRole(BASE, 'recycler', recycler, recyclerPass);
 if (!token) return;
 const headers = { 'Authorization': `Bearer ${token}` };

 const listingId = __ENV.TEST_LISTING_ID || '1';
 const file = open(filePath, 'b');
 const formData = { listingId: listingId, reportedAmount: '12.50', file: http.file(file, 'receipt.jpg', 'image/jpeg') };
 const res = http.post(`${BASE}/listings/receipt/upload`, formData, { headers });
 check(res, { 'upload ok': (r) => r.status ===200 });
}
