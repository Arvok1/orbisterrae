import type { Coordinate } from './map/projection.js';
export interface LocalSession {token:string;username:string;accountId:string}
export function storedSession(): LocalSession | null {
  if (typeof window==='undefined') return null;
  try {
    const value:unknown=JSON.parse(localStorage.getItem('orbis-session')??'null');
    return sessionValue(value);
  } catch {return null;}
}
function sessionValue(value:unknown): LocalSession | null {
  if (!value || typeof value!=='object') return null;
  if ('token' in value && typeof value.token==='string' && 'username' in value && typeof value.username==='string'
      && 'accountId' in value && typeof value.accountId==='string') return {token:value.token,username:value.username,accountId:value.accountId};
  return null;
}
export async function enterWorld(username:string,password:string,register:boolean,start:Coordinate|null): Promise<LocalSession> {
  const response=await fetch(register?'/api/register':'/api/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username,password,...(register&&start?{start}:{})})});
  const data:unknown=await response.json();
  if (!response.ok) throw new Error(data && typeof data==='object' && 'error' in data && typeof data.error==='string'?data.error:'server_error');
  const session=sessionValue(data);
  if (!session) throw new Error('invalid_server_reply');
  return session;
}
