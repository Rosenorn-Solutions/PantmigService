import http from 'k6/http';
import { check } from 'k6';
import config from './config.js';

const tokenCache = {}; // per-VU cache

export function loadConfig() {
 // Prefer JS config module if present
 try {
 if (config) {
 try { console.log('Loaded config from: loadtest/scripts/config.js'); } catch (e) {}
 return config;
 }
 } catch (e) { }

 const candidates = [
 'C:/Users/Nightfrost/source/repos/PantmigService/loadtest/k6-config.json',
 '../k6-config.json',
 'loadtest/k6-config.json',
 '../../loadtest/k6-config.json',
 '../loadtest/k6-config.json',
 './loadtest/k6-config.json',
 'k6-config.json',
 './k6-config.json',
 ];
 for (let p of candidates) {
 try {
 const raw = JSON.parse(open(p));
 try { console.log('Loaded config from:', p); } catch (e) { }
 return raw;
 } catch (e) {
 // ignore and try next
 }
 }
 // fallback
 try { console.log('Using fallback config'); } catch (e) { }
 return { baseUrl: 'http://localhost:5000' };
}

function extractAuthFromBody(body) {
 if (!body) return null;
 if (body.authResponse) return body.authResponse;
 // fallback to common shapes
 return body;
}

// Now strictly use values from config file. Do not read environment variables.
export function authTokenForRole(baseUrlIgnored, roleIgnored, userIgnored, passIgnored) {
 const cfg = loadConfig();
 const role = roleIgnored; // keep API compatible

 // No env token support - use config defaults
 const now = Date.now();
 const cache = tokenCache[role];
 if (cache && cache.accessToken && cache.expiresAt && now < cache.expiresAt -5000) {
 return cache.accessToken;
 }

 const authBase = cfg.authBase || cfg.authBaseUrl || cfg.baseUrl;
 const loginUrl = `${authBase}/auth/login`;
 const refreshUrl = `${authBase}/auth/refresh`;

 // Log auth endpoints for debugging
 try { console.log('AUTH_BASE(from config):', authBase, 'LOGIN URL:', loginUrl, 'REFRESH URL:', refreshUrl); } catch (e) { }

 // Read credentials from config
 const userCfg = (cfg.defaultUsers && cfg.defaultUsers[role]) || null;
 const username = userCfg && userCfg.username ? userCfg.username : null;
 const password = userCfg && userCfg.password ? userCfg.password : null;
 if (!username || !password) {
 console.log('AUTH CONFIG MISSING for role:', role);
 return null;
 }

 // If we have a refresh token cached, try to refresh first
 if (cache && cache.refreshToken) {
 try {
 const rres = http.post(refreshUrl, JSON.stringify({ refreshToken: cache.refreshToken }), { headers: { 'Content-Type': 'application/json' } });
 if (rres.status ===200) {
 try {
 const rbody = rres.json();
 const auth = extractAuthFromBody(rbody);
 if (auth && auth.accessToken) {
 const expiresAt = auth.accessTokenExpiration ? Date.parse(auth.accessTokenExpiration) : (Date.now() + (55 *60 *1000));
 tokenCache[role] = { accessToken: auth.accessToken, refreshToken: auth.refreshToken || auth.refreshTokenString || cache.refreshToken, expiresAt };
 return tokenCache[role].accessToken;
 }
 } catch (e) {
 console.log('REFRESH PARSE ERROR', e);
 // fall through to login
 }
 }
 } catch (e) {
 console.log('REFRESH ERROR', e);
 // ignore and attempt login
 }
 }

 // Perform login using config credentials
 try {
 // Use expected login payload: { emailOrUsername, password }
 const payload = JSON.stringify({ emailOrUsername: username, password: password });
 const res = http.post(loginUrl, payload, { headers: { 'Content-Type': 'application/json' } });
 if (res.status ===200) {
 try {
 const body = res.json();
 const auth = extractAuthFromBody(body);
 if (auth && (auth.accessToken || auth.refreshToken)) {
 const accessToken = auth.accessToken || auth.access_token || auth.accessTokenString || auth.token;
 const refreshToken = auth.refreshToken || auth.refresh_token || auth.refreshTokenString || null;
 const expiresAt = auth.accessTokenExpiration ? Date.parse(auth.accessTokenExpiration) : (Date.now() + (55 *60 *1000));
 tokenCache[role] = { accessToken: accessToken, refreshToken: refreshToken, expiresAt: expiresAt };
 return accessToken;
 }
 } catch (e) {
 console.log('LOGIN PARSE ERROR', e, 'body:', res.body ? res.body.substring(0,500) : '');
 return null;
 }
 } else {
 // log non-200 login response for debugging
 console.log('LOGIN FAILED', res.status, res.body ? res.body.substring(0,1000) : '');
 }
 } catch (e) {
 console.log('LOGIN ERROR', e);
 }
 return null;
}

export function jsonOk(res) {
 return check(res, { 'status is200': (r) => r.status ===200 });
}
