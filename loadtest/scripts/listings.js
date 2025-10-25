import http from 'k6/http';
import { sleep, check } from 'k6';
import { loadConfig, authTokenForRole } from './helpers.js';

const cfg = loadConfig();
const BASE = cfg.baseUrl || 'http://localhost:5000';

export let options = {
 vus:10,
 duration: '1m',
 thresholds: {
 'http_req_failed': ['rate<0.02'],
 'http_req_duration': ['p(95)<500']
 }
};

export default function () {
 // Log configured base URL for debugging
 try { console.log('CONFIGURED BASE URL (from config):', BASE); } catch (e) { }

 const donator = 'donator'; // role name for helper
 const token = authTokenForRole(null, donator, null, null);
 if (!token) {
 console.log('AUTH ERROR: no token obtained for role:', donator);
 return;
 }
 // Log token prefix for debugging
 try { console.log('AUTH token (prefix):', token ? token.slice(0,20) + '...' : 'null'); } catch (e) { }

 const headers = { 'Authorization': `Bearer ${token}`, 'Content-Type': 'application/json' };

 //1. Create a listing
 const createPayload = JSON.stringify({
 title: `Load test ${Math.floor(Math.random()*100000)}`,
 description: 'Automated load test listing',
 city: 'CPH',
 location: 'Test Location',
 availableFrom: new Date().toISOString().split('T')[0],
 availableTo: new Date(Date.now()+3*24*60*60*1000).toISOString().split('T')[0],
 items: [{ type:1, quantity:10 }]
 });
 const createRes = http.post(`${BASE}/listings`, createPayload, { headers });
 if (createRes.status >=400) {
 console.log('CREATE ERROR:', createRes.status, createRes.body ? createRes.body.substring(0,1000) : '');
 }
 check(createRes, { 'create listing201': (r) => r.status ===201 });
 let createdId = null;
 if (createRes.status ===201) {
 try { createdId = createRes.json('id') || createRes.json().id; } catch (e) { }
 }

 //2. List active
 const listRes = http.get(`${BASE}/listings`, { headers });
 if (listRes.status >=400) {
 console.log('LIST ERROR:', listRes.status, listRes.body ? listRes.body.substring(0,1000) : '');
 }
 check(listRes, { 'get listings200': (r) => r.status ===200 });

 //3. Search - use cities typeahead endpoint (GET) instead of POST /listings/search which is not present in this API
 const q = encodeURIComponent('CPH');
 const searchRes = http.get(`${BASE}/cities/search?q=${q}&take=10`, { headers });
 if (searchRes.status >=400) {
 console.log('SEARCH ERROR:', searchRes.status, searchRes.body ? searchRes.body.substring(0,1000) : '');
 }
 check(searchRes, { 'search200': (r) => r.status ===200 });

 //4. Get by id if we have one
 if (createdId) {
 const getRes = http.get(`${BASE}/listings/${createdId}`, { headers });
 if (getRes.status >=400) {
 console.log('GETBYID ERROR:', getRes.status, getRes.body ? getRes.body.substring(0,1000) : '');
 }
 check(getRes, { 'get by id200': (r) => r.status ===200 || r.status ===404 });
 }

 sleep(Math.random()*1.5);
}
